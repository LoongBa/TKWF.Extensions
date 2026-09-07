# TKWF.Ext.Identity 身份管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (用户与角色管理) | **框架**: .NET 10

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

- **存储抽象 (`IUserStore` / `IRoleStore`)**：定义用户/角色 CRUD 与用户-角色分配操作。扩展提供 FreeSql 默认实现。

- **持久化实现 (`FreeSqlUserStore` / `FreeSqlRoleStore`)**：将 `UserEntity` / `RoleEntity` / `UserRoleEntity` 持久化到数据库。异常静默处理。

- **管理门面 (`IUserManager` / `UserManager`)**：组合 UserStore + RoleStore，提供用户 CRUD、**凭据验证**（供消费方登录钩子调用）、密码修改、角色分配、角色 CRUD。

- **声明式实体 (`UserEntity` / `RoleEntity` / `UserRoleEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

### 2. 安全语义

- **密码散列**：复用主框架 `PasswordHasher`（PBKDF2 + 350k 迭代 + 随机盐），**不引第三方身份库**。

- **异常静默**：存储/管理操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IUserStore` / `IRoleStore` / `IUserManager` Scoped，自动参与当前请求上下文。

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

自动注册：`IUserStore`（默认 `FreeSqlUserStore`）+ `IRoleStore`（默认 `FreeSqlRoleStore`）+ `IUserManager`（默认 `UserManager`）。

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

### 5. 自定义 IUserManager / IUserStore

TryAdd 语义确保消费方实现优先；`IRoleStore` 自定义同理。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IUserStore`** | 用户存储抽象（CRUD + 角色分配） | `FreeSqlUserStore`（本扩展） |
| **`IRoleStore`** | 角色存储抽象（CRUD） | `FreeSqlRoleStore`（本扩展） |
| **`IUserManager`** | 用户管理门面（CRUD + 凭据验证 + 角色分配） | `UserManager`（本扩展） |
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

### V0.1.0（当前）
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

### V0.4.0（规划）
- **`IAccountPasswordManager` 适配器对接 Account V0.2.0 通知渠道**（重置码邮件）
- 与 Permissions 深度集成（逐用户权限门控）
- 多租户用户隔离