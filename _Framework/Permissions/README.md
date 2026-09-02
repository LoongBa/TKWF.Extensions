# TKWF.Ext.Permissions 权限扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.7.0 (建表迁移工具 + 消费方集成验证 + Admin.All 种子高级化) | **框架**: .NET 10

**核心约束**: 细粒度权限、fail-closed 安全语义、ORM 无关持久化、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对业务操作进行细粒度权限控制——超越角色级授权（`[RequireRole]`），精确到具体操作（如 `"Order.Create"`、`"Invoice.Approve"`）。

- **角色粒度不足**：`[RequireRole("Admin")]` 无法区分"管理员可删除订单"与"管理员可审批发票"——需要操作级权限。

- **权限声明分散**：权限名硬编码在过滤器参数中，拼写错误只能运行时发现。

- **持久化耦合**：权限授予存储直接依赖特定 ORM（FreeSql/EF Core），扩展无法做到 ORM 无关。

- **配置割裂**：权限配置（默认策略、缓存 TTL）散落在代码中，无法通过 `appsettings.json` 统一管理。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"声明式权限定义 + 运行时 fail-closed 检查 + ORM 无关持久化"** 架构。

### 1. 结构分层

- **声明级 (`IPermissionDefinitionContributor`)**：业务模块通过 `[PermissionContributor]` 标记贡献者类，`Define()` 方法声明权限定义（名称 + 显示名 + 分组）。SG1 编译期扫描 → 生成 `GeneratedPermissionContributors` → 启动时实例化收集。

- **检查级 (`IPermissionChecker`)**：运行时判断当前用户是否拥有指定权限。默认 `PermissionChecker<TUserInfo>` 经 `DomainUserContext.CurrentAopUser` 解析当前用户，调 `IPermissionStore.GetAsync` 真实判定。fail-closed：未知权限名 → 拒绝。

- **持久化级 (`IPermissionStore`)**：权限授予的 CRUD 抽象。扩展自带 `EntityDACPermissionStore`（基于 `IEntityDAC<T>`，ORM 无关）+ `NoOpPermissionStore`（默认回退，读恒拒绝）。

- **过滤器级 (`PermissionFilterAttribute`)**：`[RequirePermission]` 标记方法/控制器，PreProceed 阶段调 checker 检查。无标记 → 短路跳过（零开销）。

### 2. 安全语义

- **fail-closed**：权限名未定义 → 拒绝；用户未认证 → 拒绝；store 未注册 → 拒绝（NoOp 恒拒绝）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IPermissionStore`/`IPermissionChecker` 均 Scoped，自动参与当前请求 UoW 事务。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Permissions` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(PermissionExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`IPermissionChecker`（默认 `PermissionChecker<TUserInfo>`）+ `IPermissionStore`（默认 NoOp）+ `IPermissionDefinitionRepository` + `PermissionFilterAttribute`（Tier-S）。

### 2. 声明权限定义（贡献者）

业务模块用 `[PermissionContributor]` 标记一个实现 `IPermissionDefinitionContributor` 的类：

```csharp
[PermissionContributor]
public class OrderPermissions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context)
    {
        context.Add(new PermissionDefinition
        {
            Name = "Order.Create",       // 点分层级约定
            DisplayName = "创建订单",
            Group = "Order"
        });
        context.Add(new PermissionDefinition
        {
            Name = "Order.Delete",
            DisplayName = "删除订单",
            Group = "Order"
        });
    }
}
```

### 3. 方法级权限门

在服务接口方法或控制器接口上标记 `[RequirePermission]`：

```csharp
public interface IOrderService
{
    [RequirePermission("Order.Create")]
    Task CreateOrderAsync(CreateOrderInput input);

    // 多权限 + 任一逻辑（Any）：销售或管理员任一权限即可
    [RequirePermission("Order.Delete", "Order.ForceDelete", Logic = PermissionLogic.Any)]
    Task DeleteOrderAsync(long id);
}
```

无权限时抛 `DomainException`（`ErrorCode = FORBIDDEN`）。

### 4. 真实启用权限（注册持久化）

默认 NoOp store 恒拒绝——注册 `EntityDACPermissionStore` 后权限检查才真正生效：

```csharp
// DomainInitializer / ConfigureServices 中
services.AddScoped<IPermissionStore, EntityDACPermissionStore>();
services.AddScoped<IEntityDAC<PermissionGrantEntity>>( /* FreeSql/EF Core DAC */ );
```

消费方调用 `UseFreeSqlEntityDAC()` 注册 `FreeSqlEntityDAC<PermissionGrantEntity>` 即可自动接线。

### 5. 权限检查 API

```csharp
public class MyService
{
    private readonly IPermissionChecker _checker;
    public MyService(IPermissionChecker checker) => _checker = checker;

    public async Task DoSomethingAsync()
    {
        if (await _checker.IsGrantedAsync("Order.Create"))
        {
            // 有权限
        }

        // 批量检查
        var results = await _checker.IsGrantedAsync("Order.Create", "Order.Delete");
    }
}
```

> ⚠️ 代码级检查是**软判断**（不抛异常）——只做业务分支。真正的**安全门**用 `[RequirePermission]`（fail-closed 抛异常）。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IPermissionDefinitionContributor`** | 业务模块声明权限定义的接口 | 消费方实现（`[PermissionContributor]` 标记） |
| **`IPermissionDefinitionRepository`** | 权限定义仓库（查询/校验） | `InMemoryPermissionDefinitionRepository`（内部） |
| **`IPermissionChecker`** | 运行时权限检查（fail-closed） | `PermissionChecker<TUserInfo>`（内部，泛型化） |
| **`IPermissionStore`** | 权限授予持久化（Get/Set） | `NoOpPermissionStore`（内部，读恒拒绝）+ `EntityDACPermissionStore`（扩展自带，ORM 无关） |
| **`PermissionFilterAttribute<TUserInfo>`** | 方法级权限门（`[RequirePermission]`） | 内置，注册到 Tier-S |
| **`PermissionExtensionInitializer<TUserInfo>`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现（能力清单）+ 消费方 `[TKWFEnabledExtension]` 白名单启用 |
| **`PermissionGrantEntity`** | 权限授予表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]`（SubDomain=Permissions） |
| **`PermissionGrantEntityDto`** | 权限授予 DTO（xCodeGen 生成 record） | 内置，`IDomainDto<PermissionGrantEntity>` 实现 |
| **`PermissionGrantEntityDataService`** | 权限管理 DataService（CRUD + 自定义查询 + REST 管理 API） | 内置，`DomainDataServiceBase<,>` 非泛型版 + `[GenerateController(FromDataService=true)]` |
| **`PermissionOptions`** | 配置（`TKWF:Permissions` 节，含 `SeedAdminRoleName`） | 内置，SG1 `GeneratedOptionsBindings` 自动绑定 |
| **`IRoleProvider<TUserInfo>`** | 角色提供者——为权限检查器解析用户角色列表（V0.6.0） | `DefaultRoleProvider<TUserInfo>`（内部，从 `IUserInfo.Roles` 解析；消费方可 DI 覆盖） |
| **`PermissionNames`** | 系统权限名常量（V0.7.0，含 `Admin.All` 通配） | 内置，静态常量 |

---

## 五、扩展与维护规范 (Extension & Maintenance)

### 1. 新增权限定义

在消费方业务模块中创建新的 `[PermissionContributor]` 类：

1. 实现 `IPermissionDefinitionContributor`。
2. 在 `Define()` 中调用 `context.Add(new PermissionDefinition { ... })`。
3. SG1 编译期自动发现（源码 + 引用程序集）。

### 2. 自定义权限存储

若需替换 `EntityDACPermissionStore`（如使用不同 ORM 或缓存层）：

1. 实现 `IPermissionStore`。
2. 在 `ConfigureServices` 中 `services.AddScoped<IPermissionStore, MyCustomStore>()`（TryAdd 后注册，覆盖默认）。

### 3. 扩展 Provider 模型

当前 providers 约定主谓用户权限 `("User", UserIdString)`。若需支持角色/成员 providers：

1. `IPermissionStore` 实现中按 `providerName` 路由查询逻辑。
2. `IPermissionChecker` 已支持传入 `providerName`/`providerKey`（当前默认 "User"）。

### 4. 生产部署（建表迁移）

`PermissionGrant` 扩展实体不在 `FreeSqlTableStructureSynchronizer` 自动建表范围（`SyncTables` 只扫消费方 assembly）——**生产部署需含建表迁移脚本**（V0.7.0 W2，随 NuGet 发布至 `scripts\PermissionGrant.sql`）：

```sql
-- PermissionGrant 表结构（参考，完整脚本见包内 scripts\PermissionGrant.sql）
CREATE TABLE [PermissionGrant] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [PermissionName] NVARCHAR(200) NOT NULL,
    [ProviderName] NVARCHAR(64) NOT NULL,
    [ProviderKey] NVARCHAR(128) NOT NULL,
    [IsGranted] BIT NOT NULL DEFAULT 0,
    CONSTRAINT UK_PermissionGrant UNIQUE ([PermissionName], [ProviderName], [ProviderKey])
);
```

**V0.7.0 W1 扩展自建表**：注册了 `ITableStructureSynchronizer`（FreeSql）时，扩展 `InitializeAsync` 会对 `PermissionGrantEntity` 所在程序集主动 `SyncStructure`（幂等建表，创建缺失表/列）——开发环境自动建表，生产环境仍建议用 `scripts\PermissionGrant.sql` 正式迁移（DBA/CI 执行）。

---

## 六、架构演进路线 (Architecture Roadmap)

### 1. V0.3.0 / V0.4.0：权限管理 Service（已实施）

- **V0.3.0**：手写 `PermissionGrantDto` + `PermissionGrantEntityDataService`（DMP-Lite `.cs` 骨架模式）已实施。
- **V0.4.0（G1）**：xCodeGen 环境修复，生成 `PermissionGrantEntityDto`（record）+ `.g.cs`（CRUD）；业务方法迁入 `.cs` 骨架，手写 DTO/DataService 移除。`.cs` 骨架入库、`.g.cs` 不入库（Debug 构建自动重新生成）。
- **V0.4.0（G2）**：`[GenerateController(FromDataService=true)]`——消费方 SG1 自动生成 REST 管理 API（subdomain `Permissions`）。
- **V0.4.0（G3）**：种子初始化——`PermissionOptions.SeedAdminRoleName` 幂等预置 admin 角色全权限。

### 1b. V0.5.0：编译期权限名校验（已规划）

- **状态**：ADR + 开发方案已编写（`PermissionNameValidatorGenerator` + `PERM001` Warning），实施待启动。

### 1c. V0.6.0：角色→权限映射内置（已实施）

- **新增 `IRoleProvider<TUserInfo>`**：为权限检查器解析用户角色列表；默认实现 `DefaultRoleProvider<TUserInfo>` 从 `IUserInfo.Roles` 解析（消费方可 DI 覆盖）。
- **`PermissionChecker<TUserInfo>` 改造**：用户+角色双重检查——用户级显式授予优先；未授予时回退角色级判定（任一角色授予即通过）；fail-closed 不变。

### 1d. V0.7.0：建表迁移工具 + 消费方集成验证 + Admin.All（已实施）

- **W1 扩展自建表**：`InitializeAsync` 复用 `ITableStructureSynchronizer` 对扩展程序集主动 `SyncStructure`（幂等建表）。
- **W2 SQL 迁移脚本**：`scripts\PermissionGrant.sql` 随 NuGet 发布（生产 DBA/CI 迁移）。
- **W3 Admin.All 系统权限**：`PermissionNames.AdminAll`——用户/任一角色拥有即对所有已定义权限放行；种子预置 admin 角色 Admin.All（替代 V0.4.0 逐权限授予）。
- **W4 消费方集成验证**：`_Tests/Extension.Permissions.Consumer` 激活 SG1b 验证控制器接口名在消费方记录 + 三钩子接线 + TryAdd 语义。

### 2. 编译期权限名校验（ADR38 D7）

- **当前状态**：贡献者 `Define()` 是运行时方法，SG 看不到体内字符串 → 未知权限名只能运行时 fail-closed 兜底。
- **演进方向**：若需编译期校验，需 SG 扩展（如 Source Generator 扫描 `Define()` 中的字符串常量）或静态分析工具。
- **V0.5.0 状态**：ADR + 开发方案已编写（`PermissionNameValidatorGenerator` + `PERM001` Warning 诊断），实施待启动。

### 3. 种子初始化（V0.4.0 G3 + V0.7.0 W3）

- **当前状态**：`InitializeAsync` 幂等预置 admin 角色 <see cref="PermissionNames.AdminAll"/> 系统权限（V0.7.0 起，替代 V0.4.0 的逐权限授予）——Admin.All 拥有者对全部已定义权限放行，未来新增权限自动覆盖。
- **演进方向**：默认角色授权映射（角色→权限的种子配置化）、成员级权限（Member provider）。

### 4. 后续开发项（待规划）

| 项 | 说明 | 优先级 |
|----|------|:---:|
| **V0.5.0 编译期权限名校验** | ADR38 D7——SG 扫描 `Define()` 字符串常量（见 §六.2） | 中 |
| **成员级权限（Member provider）** | `IPermissionStore` 已支持任意 provider；`PermissionChecker` 扩展成员级判定 | 低 |
| **默认角色授权映射** | 角色→权限的种子配置化（`PermissionOptions` 支持） | 低 |
| **Navigation 迁出** | Navigation 依赖 Permissions，待 Permissions 稳定后迁出至扩展仓库（主框架 `总览和跟踪.md` §3.2） | 待定 |
| **发布 tag** | V0.1.0~V0.6.0 已发布；后续迭代完成须征得同意后打 tag | — |

---

## 七、AI Agent 协作契约 (AI Agent Prompting Guide)

> [!CAUTION]
> 
> **绝对指令：在进行任何代码生成或重构时，严禁触碰以下红线。**

1. **fail-closed 不可妥协**：权限名未定义 → 拒绝；用户未认证 → 拒绝；store 未注册 → 拒绝。永远不要改为 fail-open。

2. **TryAdd 语义不可破坏**：DI 注册必须用 `TryAddScoped`/`TryAddSingleton`——消费方自定义实现优先，扩展默认实现不覆盖。

3. **Scoped 生命周期**：`IPermissionStore`/`IPermissionChecker` 必须 Scoped（参与 UoW 事务），不可改为 Singleton。

4. **扩展实体不指定 UserType**：`PermissionGrantEntity` 的 `[DomainGenerateCode]` 不带 UserType 参数——ADR42 D4 决策，扩展不知道消费方 UserInfo 类型。

5. **FreeSql `[Column]` 全限定**：列映射必须用 `FreeSql.DataAnnotations.Column`（避免与 BCL `System.ComponentModel.DataAnnotations.Schema.Column` 冲突）。

6. **`[Table]` 保留**：BCL `[Table("PermissionGrant")]` 不可删除——`FreeSqlTableStructureSynchronizer` 依赖它发现实体建表。

---

## 八、维护备忘与回归防护 (Maintenance Memo)

> 本节记录 V0.3.0/V0.4.0 开发中踩过的坑与关键机制，**修改/重构前必读**，避免回归。

### 1. xCodeGen 双文件模式（DMP-Lite）

| 文件 | 角色 | 入库 | 覆盖策略 |
|------|------|:---:|---------|
| `Entities/**/*.biz.cs`、`DataServices/*.cs` | 业务扩展骨架（partial） | ✅ 入库 | **仅首次生成，绝不覆盖**（`xCodeGen.Core/Engine.cs` L203-204 双保险：存在即跳过 + `Write(overwrite:false)`） |
| `**/*.g.cs` | 机械 CRUD/DTO 逻辑 | ❌ 不入库（`.gitignore` 含 `*.g.cs`） | 每次 Debug 构建经 `_XCG_Run`（`TKWF.Domain.targets` AfterBuild）按需重新生成 |

- **业务方法只写在 `.cs` 骨架**，禁止改 `.g.cs`（会被覆盖）。
- `.g.cs` 缺失时 Debug 构建自动补全；Release/无 `_XCG_ConfigPath` 时不生成（骨架文件已入库不受影响）。
- 手动强制重新生成：`dotnet run --project "$TKWFRoot_xCodeGen\xCodeGen.Cli" -- gen -j .xCodeGen\permissions.xCodeGen.json --force`。

### 2. DataService 关键约束

- **必须 `public`**：扩展为预编译库，控制器/DataService 由**消费方 SG1** 经 `ReferencedAssemblySymbols` 发现生成（`ControllerGenerator.cs` L1002 设计）——internal 会导致消费方生成代码无法跨程序集引用。
- **`[GenerateController(FromDataService=true)]`**：扩展自身**不生成**控制器（无具体 TUser），由消费方生成；`CTRL003`/`CTRL006` 是**预期诊断**，已在 csproj `<NoWarn>` 抑制，勿移除。
- **主构造函数模式**：生成的骨架用主构造函数 `(IDomainUser user, IEntityDAC<TEntity> dac)`，业务方法直接使用参数 `dac`/`user`（不要另建字段——会被 `.g.cs` 的 `sealed partial` 冲突）。

### 3. DTO 命名与持久状态语义

- 生成 DTO 是 **`PermissionGrantEntityDto`**（record，`init` 属性，`TKWF.Ext.Permissions.DTOs`），**不是**手写 `PermissionGrantDto`——测试/文档引用以 `PermissionGrantEntityDto` 为准。
- **`IsFromPersistentSource` 语义**：由框架**读路径**（`DomainReadOnlyDataServiceBase`）取数后统一置位 true；**写路径（Insert/Update）不置位**。新插入实体返回的 DTO 该标志 = false。测试断言勿硬编码 true。

### 4. SubDomain 传导（管理 API 路由）

- 实体 `[DomainGenerateCode(SubDomain="Permissions", SubDomainRoutePrefix="/Permissions")]` → 消费方生成的 REST 管理 API subdomain。
- 修改 SubDomain 后需 **`dotnet build -t:Rebuild`** 清 obj 缓存（SG 元数据增量缓存可能导致不生效）。

### 5. 骨架文件缺 using

- xCodeGen 生成的 `.biz.cs`/`Dto.cs` 骨架**不自带** `System.Collections.Generic`——含 `List<ValidationResult>` 的 partial 方法实现须手动补 `using`，否则 CS0246 → CS0759 连锁报错（已修，勿删）。

### 6. 测试基线

- **51/51 通过**（xunit.v3）：单元测试 47（初始化器 3 + PermissionChecker 8 + EntityDACPermissionStore 9 + DataService 9 + SeedInitializer 5 + Role/Admin.All 13）+ 消费方集成验证 4。
- 测试桩 `InMemoryEntityDac`/`StubDomainUser` 是各测试文件私有内部类——新增测试可复用但需自行内嵌。

### 7. 版本与发布

- 扩展独立版本（MinVer），tag 前缀 `v`，**须征得同意**才打 tag。当前未打 tag（V0.3.0/V0.4.0 未发布）。
- 生产建表：`PermissionGrant` 不在 `SyncTables` 自动建表范围，需迁移脚本（见 §五.4）。

### 文档信息

- **归档日期**: 2026-08-31
- **维护团队**: play / TKW Framework Team
- **审批状态**: 定稿 (V0.4.0)