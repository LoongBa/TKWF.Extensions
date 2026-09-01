# TKWF.Ext.Account 账户管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (账户管理与安全策略) | **框架**: .NET 10

**核心约束**: 主框架缺口实现、FreeSql 持久化、异常静默处理、SG1 声明式实体、不重建 AuthController

---

## 一、需求分析 (Demand Analysis)

主框架已有完整认证 API（`AuthController<TUserInfo>`：登录/登出/注册/改密），但账户安全扩展点仍停留于接口层——**业务项目需各自实现**：

- **账户锁定散落**：主框架 `IAccountLockoutPolicy`（V4.9.45）仅定义契约——`IsLockedAsync`/`OnFailedLoginAsync`/`OnSuccessfulLoginAsync`/`UnlockAsync`，失败计数/锁定时长逻辑各项目重复实现。

- **密码重置无标准**：主框架 `IPasswordResetFlow`（V4.9.45）仅定义契约——`InitiateResetAsync`/`CompleteResetAsync`，重置码生成/校验/过期处理无默认实现。

- **防枚举缺失**：重置流程用户存在性判断若无统一约定，易泄漏用户存在信息。

- **与用户存储解耦**：主框架契约按 UserName 工作，不绑定 Identity 用户表——Account 保持独立，密码落地经消费方适配器注入。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"主框架缺口默认实现 + FreeSql 持久化 + 消费方适配器 + 异常静默"** 架构。

### 1. 结构分层

- **锁定记录实体 (`AccountLockoutEntity`)**：用户名/失败计数/锁定截止时间，SG1 声明式，FreeSql 持久化。

- **重置码实体 (`PasswordResetCodeEntity`)**：用户名/重置码/过期时间/使用状态，SG1 声明式，FreeSql 持久化。

- **存储抽象 (`IAccountLockoutStore` / `IPasswordResetStore`)**：锁定状态与重置码的 CRUD。扩展提供 FreeSql 默认实现。

- **策略默认实现 (`FreeSqlAccountLockoutPolicy` / `DefaultPasswordResetFlow`)**：**补主框架缺口**——实现 `IAccountLockoutPolicy` / `IPasswordResetFlow`（V4.9.45 扩展点），注册后框架 AuthController 自动调用。

- **密码落地适配器 (`IAccountPasswordManager`)**：消费方实现——将重置后的新密码散列写入用户存储（可适配 Identity 的 `IUserManager`）。扩展不提供默认实现（用户存储属于消费方）。

### 2. 安全语义

- **防用户枚举**：`InitiateResetAsync` 用户不存在也返回 true。
- **幂等消费**：重置码使用后标记 `IsUsed`，重复提交无效。
- **过期失效**：重置码超过有效期（默认 30 分钟）自动失效。
- **异常静默**：存储/策略操作失败记录 Warning 日志，不抛出异常。
- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先。
- **Scoped 生命周期**：存储/策略 Scoped，自动参与当前请求上下文。

### 3. 与主框架的关系

- 复用主框架 `IAccountLockoutPolicy` / `IPasswordResetFlow` / `ResetResult` 契约（`TKW.Framework.Core.AuthController`）。
- 本扩展提供**默认实现**，注册后框架 `AuthController.LoginByContextAsync` 自动执行锁定检查、密码重置 API 自动可用。
- 登录/注册/改密 API 本体仍由框架 `AuthController<TUserInfo>` 提供——本扩展不重建。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Account` 包，扩展经 `[TKWFExtension]` 被 SG1 发现；**V4.9.85 起发现不自动启用**——消费方须在领域初始化器上声明 `[TKWFEnabledExtension]` 白名单，三钩子才接线：

```csharp
// 消费方领域初始化器
using TKWF.Ext.Account;

[TKWFEnabledExtension(typeof(AccountExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // ...
}
```

自动注册：`IAccountLockoutStore`（`FreeSqlAccountLockoutStore`）+ `IPasswordResetStore`（`FreeSqlPasswordResetStore`）+ `IAccountLockoutPolicy`（`FreeSqlAccountLockoutPolicy`）+ `IPasswordResetFlow`（`DefaultPasswordResetFlow`）。

### 2. 注册密码落地适配器（必需）

扩展不提供 `IAccountPasswordManager` 默认实现——消费方实现并注册（适配 Identity 的 `IUserManager`）：

```csharp
// 消费方：实现 IAccountPasswordManager
public sealed class IdentityPasswordManager : IAccountPasswordManager
{
    private readonly IUserManager _userManager; // Identity 扩展注入
    public IdentityPasswordManager(IUserManager userManager) => _userManager = userManager;

    public async Task<bool> UserExistsAsync(string userName, CancellationToken ct)
        => await _userManager.FindByNameAsync(userName, ct) is not null;

    public async Task<bool> SetPasswordAsync(string userName, string newClientHash, string salt, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(userName, ct);
        if (user is null) return false;
        await _userManager.ChangePasswordAsync(user.Id, newClientHash, ct);
        return true;
    }
}

// 消费方 ConfigureServices 中注册
services.AddScoped<IAccountPasswordManager, IdentityPasswordManager>();
```

### 3. 账户锁定（框架自动调用）

注册 `IAccountLockoutPolicy` 后，框架 `AuthController.LoginByContextAsync` 自动执行：

```csharp
// 手动检查/解锁（如管理后台）
var locked = await lockoutPolicy.IsLockedAsync("alice");
await lockoutPolicy.UnlockAsync("alice");
```

### 4. 密码重置流程

```csharp
// 发起重置（框架 API 或直接调用）
await passwordResetFlow.InitiateResetAsync("alice");

// 完成重置（客户端已计算 PBKDF2：newClientHash + salt）
var result = await passwordResetFlow.CompleteResetAsync("alice", resetCode, newClientHash, salt);
```

### 5. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "Account": {
      "MaxFailedAttempts": 5,
      "DefaultLockoutMinutes": 15,
      "ResetCodeValidityMinutes": 30,
      "IsEnabled": true
    }
  }
}
```

### 6. 自定义策略/存储

TryAdd 语义确保消费方实现优先；`IAccountLockoutStore` / `IPasswordResetStore` 自定义同理。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IAccountLockoutPolicy`** | 账户锁定策略（主框架扩展点） | `FreeSqlAccountLockoutPolicy`（本扩展） |
| **`IPasswordResetFlow`** | 密码重置流程（主框架扩展点） | `DefaultPasswordResetFlow`（本扩展） |
| **`IAccountLockoutStore`** | 锁定状态存储抽象 | `FreeSqlAccountLockoutStore`（本扩展） |
| **`IPasswordResetStore`** | 重置码存储抽象 | `FreeSqlPasswordResetStore`（本扩展） |
| **`IAccountPasswordManager`** | 密码落地抽象 | 消费方实现（适配 Identity IUserManager） |
| **`AccountLockoutEntity`** | 锁定记录表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`PasswordResetCodeEntity`** | 重置码表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`AccountUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`AccountOptions`** | 配置选项（`TKWF:Account` 节） | 内置 |
| **`AccountExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现 |

---

## 五、实体表结构 (Entity Schema)

`AccountLockoutEntity` → `AccountLockout` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserName | NVARCHAR(128) | 用户名（唯一） |
| FailedCount | INT | 连续失败次数 |
| LockoutEnd | DATETIME? | 锁定截止时间（null=未锁定，本地时间语义） |
| LastFailedTime | DATETIME? | 最近失败时间（本地时间语义） |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

`PasswordResetCodeEntity` → `PasswordResetCode` 表：Id / UserName / ResetCode / ExpireTime（DATETIME，本地时间语义）/ IsUsed / CreateTime

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 账户锁定默认实现（`IAccountLockoutPolicy`——失败计数/锁定阈值/自动解锁）
- 密码重置默认实现（`IPasswordResetFlow`——随机码/过期/幂等消费/防用户枚举）
- 双实体 SG1 化 + FreeSql 持久化

### V0.2.0（规划）
- 重置码通知渠道（对接 Emailing 扩展发送邮件）
- 与 Identity 深度集成（开箱 `IAccountPasswordManager` 适配器）
- 多因素认证（MFA）

### V0.3.0（规划）
- 管理 UI（账户锁定/解锁/重置审计）
- 登录历史与异常检测