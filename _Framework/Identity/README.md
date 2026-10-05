# TKWF.Ext.Identity 身份管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.5.1 (用户与角色管理 + VEntity 跨表 JOIN + 能力完善 + REST 直接暴露 + 领域自治根治 ADR90 + **Development 启动崩溃修复 V0.5.1——`InitializeAsync` 的 `BeginSystemScopeAsync(sp)` 传 root 不建子 scope → 改不传参，同 AuthCenter V0.5.4 defect 复制链**) | **框架**: .NET 10

**核心约束**: 用户/角色持久化、PasswordHasher 凭据验证、FreeSql 存储、异常静默处理、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，用户与角色的持久化管理是权限体系的地基——登录凭据验证、角色分配、用户生命周期管理。

- **持久化空白**：主框架已有用户上下文抽象（`IUserInfo`/`IDomainUser`/`AuthController`）但**无用户/角色表**——业务项目各自建表、代码重复。

- **凭据无标准实现**：框架 `DomainUserHelperBase.OnLoginByPasswordAsync` 默认抛 `NotSupportedException`，业务方需自行实现密码散列与校验。

- **角色无生命周期**：框架仅有角色字符串"运行时判定"（`IUserInfo.Roles.Contains`），无角色实体、无用户-角色分配。

- **配置割裂**：密码策略、默认角色散落在代码中。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"存储抽象 + FreeSql 持久化 + 主框架 PasswordHasher + 异常静默"** 架构。

### 1. 结构分层

- **存储抽象 (`IUserStore` / `IRoleStore`)**：定义用户/角色 CRUD 与用户-角色分配操作。扩展提供默认实现。

- **持久化实现 (`UserStore` / `RoleStore`)**：将 `UserEntity` / `RoleEntity` / `UserRoleEntity` 持久化到数据库（委托 SG1 DataService，异常静默处理）。

- **管理门面 (`IUserManager` / `UserManager`)**：组合 UserStore + RoleStore，提供用户 CRUD、**凭据验证**（供消费方登录钩子调用）、密码修改、角色分配、角色 CRUD。

- **声明式实体 (`UserEntity` / `RoleEntity` / `UserRoleEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

### 2. 安全语义

- **密码散列**：复用主框架 `PasswordHasher`（PBKDF2 + 350k 迭代 + 随机盐），**不引第三方身份库**。

- **异常静默**：存储/管理操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **域作用域守卫（V0.5.0）**：三接口经 `AddConstructibleService` 注册——接口 = 可构造守卫工厂（`User.Use<接口>()` 调用链内解析，`CurrentAopUser` 守卫），实现类 = throw-factory（禁直接 DI 解析）。旧 TryAddScoped 构造注入 `IDomainUser`（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）。

- **Scoped 生命周期**：`IUserStore` / `IRoleStore` / `IUserManager` Scoped，自动参与当前请求上下文。消费方统一经 `User.Use<接口>()` 解析（AOP 路径——先设 CurrentAopUser 再 GetRequiredService，守卫工厂经 ActivatorUtilities 直建实现）。

### 3. 与主框架的关系

- 复用主框架 `PasswordHasher`（密码散列/校验）、`IUserInfo`（角色列表载体）、`DomainUserHelperBase`（登录钩子）。
- 本扩展提供三类实体（用户/角色/映射）+ 存储/管理器实现 + `IdentityExtensionInitializer` 注册。
- 登录衔接：消费方 `DomainUserHelperBase.OnLoginByPasswordAsync` 内调用 `IUserManager.VerifyCredentialsAsync` + `GetUserRolesAsync` 填充 `IUserInfo.Roles`。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Identity` 包，扩展经 `[TKWFExtension]` 被 SG1 发现；**V4.9.85 起发现不自动启用**——消费方须在领域初始化器上声明 `[TKWFEnabledExtension]` 白名单，三钩子才接线：

```csharp
// 消费方领域初始化器
using TKWF.Ext.Identity;

[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // ...
}
```

自动注册（V0.5.0 领域自治根治，ADR90——按"正确路线"注册形态）：

| 接口 | 实现 | 注册形态 | 消费方式 |
|------|------|---------|---------|
| `IUserStore` | `UserStore`（继承 `DomainServiceBase`） | **`AddConstructibleService`**（接口可构造守卫工厂 + 实现类 throw-factory） | `User.Use<IUserStore>()` |
| `IRoleStore` | `RoleStore`（继承 `DomainServiceBase`） | **`AddConstructibleService`** | `User.Use<IRoleStore>()` |
| `IUserManager` | `UserManager`（继承 `DomainServiceBase`） | **`AddConstructibleService`** | `User.Use<IUserManager>()` |
| `IAccountPasswordManager` | `IdentityPasswordManager` | **接线型普通 DI**（TryAddScoped，ctor IServiceProvider + IOptions——无 IDomainUser） | Account `DefaultPasswordResetFlow` 经 `GetService` 解析 |
| `IRoleProvider<TUserInfo>` | `IdentityRoleProvider<TUserInfo>` | **接线型普通 DI**（AddScoped，ctor IServiceProvider——覆盖 Permissions 默认） | Permissions `PermissionChecker` 构造注入 |

> **V0.5.0（V4.10.53 ADR90 领域自治根治）**：三接口 Store/Manager 继承 `DomainServiceBase`（经基类 `User` 获取用户上下文——**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）+ `[DiContractIgnore]` 豁免 DI001；注册改 `AddConstructibleService`（消费方统一 `User.Use<接口>()` 解析）。两跨扩展契约（`IAccountPasswordManager` / `IRoleProvider<TUserInfo>`，Abstractions 契约非 IDomainService 不可修改）改**接线型**（skill §4.2——ctor `IServiceProvider` + C1 延迟解析 `IUserManager`）：修复真实生产故障——旧 ctor 注入 `IDomainUser` 致 Account Flow `GetService` 解析 / Permissions `PermissionChecker` ActivatorUtilities 构造失败（密码重置静默不可用 / 角色查库失效）。

### 2. 用户管理与凭据验证

```csharp
// 注入 IUserManager
public class AuthService(IUserManager userManager)
{
    // 创建用户（自动密码散列）
    public async Task<UserEntity?> RegisterAsync(string name, string password, string displayName)
        => await userManager.CreateUserAsync(name, password, displayName);

    // 登录钩子调用（消费方 DomainUserHelperBase.OnLoginByPasswordAsync 内）
    public async Task<UserEntity?> ValidateAsync(string name, string password)
        => await userManager.VerifyCredentialsAsync(name, password);

    // 角色分配
    public async Task AssignAdminAsync(long userId)
    {
        var admin = await userManager.FindByNameAsync("admin"); // 或按角色名查
        var roles = await userManager.GetUserRolesAsync(userId);
        await userManager.AssignRolesAsync(userId, new long[] { adminRole.Id });
    }
}
```

### 3. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "Identity": {
      "PasswordMinLength": 6,
      "IsEnabled": true
    }
  }
}
```

### 4. 登录衔接（消费方 UserHelper）

```csharp
protected override async Task<MyUserInfo> OnLoginByPasswordAsync(
    DomainUser<MyUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
{
    var idUser = await _userManager.VerifyCredentialsAsync(userName, credential);
    if (idUser == null) throw new InvalidCredentialException();

    var roles = await _userManager.GetUserRolesAsync(idUser.Id);
    // 填充 Roles → 框架 IsInRole / Permissions 角色级判定自动工作
    return CreateUserInstance(idUser.UserName, idUser.DisplayName, roles.Select(r => r.Name));
}
```

### 5. 自定义 IUserStore / IRoleStore / IUserManager

V0.5.0 起三接口为 AddConstructibleService 注册。消费方自定义实现方式：经 `services.AddScoped<IUserStore, MyCustomStore>()` 显式覆盖（后注册覆盖 AddConstructibleService 默认——扩展钩子先于消费方 OnRegisterDomainServices，消费方可见 AddScoped 覆盖默认；自定义实现同样须继承 `DomainServiceBase` 且禁构造注入其它域服务/DataService——DI004 零豁免）。<see cref="IAccountPasswordManager"/>（TryAddScoped 接线型）与 <see cref="IRoleProvider{TUserInfo}"/>（AddScoped 接线型）的消费方覆盖保持 TryAdd/AddScoped 语义。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IUserStore`** | 用户存储抽象（CRUD + 角色分配） | `UserStore`（本扩展，继承 `DomainServiceBase`，AddConstructibleService 注册） |
| **`IRoleStore`** | 角色存储抽象（CRUD） | `RoleStore`（本扩展，继承 `DomainServiceBase`，AddConstructibleService 注册） |
| **`IUserManager`** | 用户管理门面（CRUD + 凭据验证 + 角色分配） | `UserManager`（本扩展，继承 `DomainServiceBase`，AddConstructibleService 注册） |
| **`UserEntity`** | 用户表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`RoleEntity`** | 角色表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`UserRoleEntity`** | 用户-角色映射实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`IdentityUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`IdentityOptions`** | 配置选项（`TKWF:Identity` 节） | 内置 |
| **`IdentityExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现 |

---

## 五、实体表结构 (Entity Schema)

`UserEntity` → `IdentityUser` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserName | NVARCHAR(128) | 登录名 |
| NormalizedUserName | NVARCHAR(128) | 规范化用户名（大写，查询用） |
| DisplayName | NVARCHAR(128) | 显示名 |
| PasswordHash | NVARCHAR(256) | 密码散列（PasswordHasher 输出） |
| Email | NVARCHAR(256) | 邮箱（可空） |
| Phone | NVARCHAR(32) | 手机号（可空） |
| IsActive | BIT | 是否启用 |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

`RoleEntity` → `IdentityRole` 表：Id / Name / DisplayName / IsSystemRole / CreateTime / UpdateTime

`UserRoleEntity` → `IdentityUserRole` 表：Id / UserId / RoleId

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（已实施）
- 用户/角色/映射实体 + FreeSql 存储
- 凭据验证（PasswordHasher）+ 用户/角色 CRUD + 角色分配
- Admin 系统角色种子（幂等）

### V0.2.0（已实施：VEntity 跨表查询升级）
- **`GetRolesAsync` VEntity 化**：新增 `UserRoleView`（VEntity，JOIN `IdentityUserRole` → `IdentityRole` 单查询下推 DB），替代两步查询（先查 RoleId 集合再查 Role）——消除两次往返 + IN 子句，顺带返回角色完整字段（含 CreateTime/UpdateTime）
- 手写只读 DataService `UserRoleViewDataService`（`DomainReadOnlyDataServiceBase` + `IEntityReadOnlyDAC<T>`，红线合规）
- Store 接口签名不变（视图行映射回 `RoleEntity`），消费方零迁移
- **生产部署要求**：框架 SyncViewsAsync 只跑开发环境建视图；**生产需 DBA 手动执行 ViewSql**（PG 默认方言，SQL Server 等需补变体）

### V0.3.0（已实施：能力完善）
- **开箱密码重置适配器 `IdentityPasswordManager`**（`IAccountPasswordManager`，组装方案 + 可配置迭代——`IOptions<DomainOptions>.Auth.Pbkdf2Iterations` 与框架单一来源）——消费方启用 Identity + Account 零手写获得密码重置落地
- **Permissions 角色实时查库 `IdentityRoleProvider<TUserInfo>`**（`IRoleProvider`，经 VEntity JOIN 链路 + Scoped 缓存消除 PermissionChecker 2N 放大；`AddScoped` 覆盖默认，双白名单时生效）——角色变更即时生效，无需重新登录
- **注册/登录 API `IdentityAuthService`**（普通 `[GenerateController]` + `DomainServiceBase`；`POST /auth/register` + `POST /auth/login`，匿名守卫双控）——明文注册/登录开箱即用（框架 AuthController 仅 SecurePassword）
- **登录衔接基类 `IdentityUserHelperBase<TUserInfo>`**（预实现 `OnLoginByPasswordAsync` 调 `IUserManager`，消费方仅实现 `CreateUserInfoFromEntity` 工厂——样板 171→8 行）
- **框架侧配套**：`DomainUserHelperBase.OnNewGuestSessionCreatedAsync` abstract→virtual + 默认 guest（主框架独立提交）
- 拆包 `TKWF.Ext.Account.Abstractions`（`IAccountPasswordManager` 契约，ADR48 D7）；`UserEntity` 加 NormalizedUserName 唯一索引（重名竞态 DB 兜底）
- **生产部署**：注册端点重名预检 + DB 唯一索引双保险；IdentityUser 表结构变更（唯一索引）需 DBA 迁移

### V0.4.0（已实施：REST 直接暴露——VEntity DTO 一等公民）
- **`UserRoleViewQueryService`**（`[GenerateController]` + `DomainServiceBase`，消费方 SG1b 自动注册——无 TryAddScoped）：
  `GET /api/identity/user-roles` 返回**当前登录用户**角色视图（`UserRoleViewDto`——含 DisplayName/IsSystemRole 完整 JOIN 字段，替代"视图行映射回原实体"保守形态）
- **仅本人防护（防 IDOR）**：userId 从 `IDomainUser` 解析（服务端上下文），不信任客户端传参；用户上下文无效 → `UnauthorizedAccessException`；管理员全量查询另设 `[RequirePermission]` 守卫端点（留待管理需求）
- **门面链路零改动**：`IUserManager.GetUserRolesAsync` 仍返回 `RoleEntity`（`IdentityRoleProvider`/`IdentityUserHelperBase` 业务消费不变）；`UserRoleViewDataService` public 化（公开 ctor 依赖 + 只读查询面可注入）
- **生产部署**：`vw_UserRoleView` 视图 ViewSql 沿用 V0.2.0（生产 DBA 手动建视图）

### V0.5.0（已实施：V4.10.53 领域自治根治，ADR90——正确路线）
- **三接口 Store/Manager 继承 `DomainServiceBase`**（`UserStore`/`RoleStore`/`UserManager`）——经基类 `User` 获取用户上下文（**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——v0.3.3 同根缺陷）；`[DiContractIgnore]` 豁免 DI001；DataService/Store 仍经 `User.Use<具体类>()` NoAop / `User.Use<接口>()` AOP 懒加载（DI004 零豁免）
- **注册形态改 `AddConstructibleService`**（三接口）——接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方统一 `User.Use<IUserStore>()` / `User.Use<IRoleStore>()` / `User.Use<IUserManager>()` 解析
- **两跨扩展契约改接线型**（不继承 DomainServiceBase、不注入 IDomainUser）——`IdentityRoleProvider<TUserInfo>` / `IdentityPasswordManager` ctor 改 `IServiceProvider`（+ IO<DomainOptions>），`IUserManager` 经 C1 延迟解析（GetRequiredService）。修复真实生产故障：旧 ctor(IDomainUser) 致 Permissions `PermissionChecker` ActivatorUtilities 构造 `IRoleProvider` 失败（角色查库静默失效）/ Account Flow `GetService<IAccountPasswordManager>` 构造失败（密码重置落地静默不可用）；注册保持 AddScoped（覆盖 Permissions 默认）/ TryAddScoped（AddConstructibleService 编译约束 where TInterface : IDomainService 不满足——Abstractions 契约不可修改）
- **登录衔接基类修复（DomainUserHelper 侧消费守卫契约）**：`IdentityUserHelperBase.OnLoginByPasswordAsync` 原 `user.GetService<IUserManager>()`（裸 GetRequiredService，**不设 CurrentAopUser**）→ `IUserManager` 守卫工厂下触发「CurrentAopUser 为空」守卫生产必抛（登录静默失败）；改 `user.Use<IUserManager>()`（AOP 路径设 CurrentAopUser=调用方，守卫工厂经 ActivatorUtilities 直建实现并注入基类 User）
- **`InitializeAsync` 种子改 System 作用域**（方案 A'，对齐 Tagging/Permissions/Authentication）——IRoleStore 经 `BeginSystemScopeAsync` + `sysScope.System.Use<IRoleStore>()` 解析（裸 GetService 触守卫必抛）；种子核心提取 `SeedAdminRoleAsync(roleStore)` internal static 供直测；无 IEntityDAC<RoleEntity>（真实持久化未接线）时守卫跳过种子
- 测试宿主重写：真实 DI（Initializer ConfigureServices + FreeSql SQLite + AddLogging）+ `DomainUser<TestUserInfo>.BindScope` + `User.Use<接口>()` AOP 路径 + 注册形态断言（守卫工厂/throw-factory/接线型普通 DI）+ 种子核心直测 + 守卫跳过路径直测；接线型直构（`new IdentityPasswordManager(sp, options)` / `new IdentityRoleProvider<TUserInfo>(sp)` + 真实解析链）
- 73 用例全绿（禁止 slnx 构建，仅 Identity 项目 + 测试项目）

### V0.6.0（规划）
- **`IAccountPasswordManager` 适配器对接 Account V0.2.0 通知渠道**（重置码邮件）
- 与 Permissions 深度集成（逐用户权限门控）
- 多租户用户隔离