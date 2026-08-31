# TKWF.Ext.Permissions 权限扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.4.0 (权限管理 Service + 种子初始化 + xCodeGen 生成) | **框架**: .NET 10

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

消费方引用 `TKWF.Ext.Permissions` 包，扩展经 `[TKWFExtension]` 被 SG1 自动发现，三钩子自动接线——**无需手动注册**：

```csharp
// 消费方 .csproj
<ProjectReference Include="..\Framework\Permissions\TKWF.Ext.Permissions.csproj" />
```

自动注册：`IPermissionChecker`（默认 `PermissionChecker<TUserInfo>`）+ `IPermissionStore`（默认 NoOp）+ `IPermissionDefinitionRepository` + `PermissionFilterAttribute`（Tier-S）。

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
| **`PermissionExtensionInitializer<TUserInfo>`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现 |
| **`PermissionGrantEntity`** | 权限授予表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]`（SubDomain=Permissions） |
| **`PermissionGrantEntityDto`** | 权限授予 DTO（xCodeGen 生成 record） | 内置，`IDomainDto<PermissionGrantEntity>` 实现 |
| **`PermissionGrantEntityDataService`** | 权限管理 DataService（CRUD + 自定义查询 + REST 管理 API） | 内置，`DomainDataServiceBase<,>` 非泛型版 + `[GenerateController(FromDataService=true)]` |
| **`PermissionOptions`** | 配置（`TKWF:Permissions` 节，含 `SeedAdminRoleName`） | 内置，SG1 `GeneratedOptionsBindings` 自动绑定 |

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

`PermissionGrant` 扩展实体不在 `FreeSqlTableStructureSynchronizer` 自动建表范围（`SyncTables` 只扫消费方 assembly）——**生产部署需含建表迁移脚本**：

```sql
-- PermissionGrant 表结构（参考）
CREATE TABLE [PermissionGrant] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [PermissionName] NVARCHAR(200) NOT NULL,
    [ProviderName] NVARCHAR(64) NOT NULL,
    [ProviderKey] NVARCHAR(128) NOT NULL,
    [IsGranted] BIT NOT NULL DEFAULT 0,
    CONSTRAINT UK_PermissionGrant UNIQUE ([PermissionName], [ProviderName], [ProviderKey])
);
```

开发期可通过 `FreeSql结构同步` lazy 建表兜底（`[Table]` + `[Column]` 特性驱动），但生产环境建议使用正式迁移工具（如 FluentMigrator、EF Core Migrations）管理表结构变更。

---

## 六、架构演进路线 (Architecture Roadmap)

### 1. V0.3.0 / V0.4.0：权限管理 Service（已实施）

- **V0.3.0**：手写 `PermissionGrantDto` + `PermissionGrantEntityDataService`（DMP-Lite `.cs` 骨架模式）已实施。
- **V0.4.0（G1）**：xCodeGen 环境修复，生成 `PermissionGrantEntityDto`（record）+ `.g.cs`（CRUD）；业务方法迁入 `.cs` 骨架，手写 DTO/DataService 移除。`.cs` 骨架入库、`.g.cs` 不入库（Debug 构建自动重新生成）。
- **V0.4.0（G2）**：`[GenerateController(FromDataService=true)]`——消费方 SG1 自动生成 REST 管理 API（subdomain `Permissions`）。
- **V0.4.0（G3）**：种子初始化——`PermissionOptions.SeedAdminRoleName` 幂等预置 admin 角色全权限。

### 2. 编译期权限名校验（ADR38 D7）

- **当前状态**：贡献者 `Define()` 是运行时方法，SG 看不到体内字符串 → 未知权限名只能运行时 fail-closed 兜底。
- **演进方向**：若需编译期校验，需 SG 扩展（如 Source Generator 扫描 `Define()` 中的字符串常量）或静态分析工具。

### 3. 种子初始化（已实施，V0.4.0 G3）

- **当前状态**：`InitializeAsync` 实现 `SeedAdminRoleName` 幂等预置（仅缺失记录插入，不覆盖既有授予/撤销）。
- **演进方向**：预置系统权限（如 `Admin.All`）、默认角色授权映射。

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

### 文档信息

- **归档日期**: 2026-08-31
- **维护团队**: play / TKW Framework Team
- **审批状态**: 定稿 (V0.4.0)