# TKWF.Ext.Account 账户管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.5.0（领域自治根治，ADR90） | **框架**: .NET 10 | **表名前缀简称**: `Acct`（扩展仓命名规则 §8.3 自声明——撞名追尾方全责，本扩展 `PasswordResetCode` 表用 `TKWF_Acct_`）

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

自动注册（V0.5.0 领域自治根治，ADR90——按"正确路线"注册形态）：

| 接口 | 实现 | 注册形态 | 消费方式 |
|------|------|---------|---------|
| `IAccountLockoutStore` | `AccountLockoutStore`（继承 `DomainServiceBase`） | **`AddConstructibleService`**（接口可构造守卫工厂 + 实现类 throw-factory） | `User.Use<IAccountLockoutStore>()` |
| `IPasswordResetStore` | `PasswordResetStore`（继承 `DomainServiceBase`） | **`AddConstructibleService`** | `User.Use<IPasswordResetStore>()` |
| `IAccountLockoutPolicy` | `FreeSqlAccountLockoutPolicy` | **接线型普通 DI**（TryAddScoped，ctor IServiceProvider/IOptions/ILogger——无 IDomainUser） | `AuthController` 经 `GetOptionalService` 自动调用 |
| `IPasswordResetFlow` | `DefaultPasswordResetFlow` | **接线型普通 DI** | `AuthController` 经 `GetOptionalService` 自动调用 |
| `ILoginHistoryService` | `LoginHistoryService`（继承 `DomainServiceBase`） | **`AddConstructibleService`**（V0.5.x 由接线型升门面——SecurityLog 契约已门面化须 AOP 帧） | `User.Use<ILoginHistoryService>()`；经基类 `User.Use<SecurityLog 契约>()` AOP 解析（C1 模式） |

> **V0.5.0（V4.10.53 ADR90 领域自治根治）**：两 Store 继承 `DomainServiceBase`（经基类 `User` 获取用户上下文——**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）+ `[DiContractIgnore]`；注册改 `AddConstructibleService`（消费方统一 `User.Use<接口>()` 解析）。Policy/Flow 改**接线型**（skill §4.2——显式 userName 参数、无需用户上下文，不继承 DomainServiceBase）：修复真实生产故障——旧 ctor 注入 IDomainUser 致 `AuthController.GetOptionalService` 构造失败、锁定检查静默失效；改后 ctor 全 DI 可解析，`IAccountLockoutPolicy`/`IPasswordResetFlow` 非 `IDomainService`（主框架契约不可改），`AddConstructibleService` 编译约束不满足、保持 TryAddScoped 普通 DI。Store 经 `IServiceProvider` C1 延迟解析（接线型边界不吞守卫语义，读取失败仍异常静默降级）。
>
> **V0.5.x（批次间交互遗留修复，2026-10-04）**：`ILoginHistoryService` 由接线型（ctor IServiceProvider + `GetRequiredService` 普通解析）升**门面**——SecurityLog V0.4.0 三契约已门面化（`AddConstructibleService` 守卫工厂），普通解析在域作用域帧外触「领域架构守卫」异常（跨扩展消费链断裂，全量测试暴露 6 用例失败）；升门面后继承 `DomainServiceBase` 经基类 `User.Use<ISecurityLogQueryService>()` / `User.Use<ISecurityLogAnalyticsService>()` AOP 路径解析（IDomainUser 永不注册 DI，D01 铁律）；注册改 `AddConstructibleService<ILoginHistoryService, LoginHistoryService>`。

### 2. 注册密码落地适配器

**V0.3.0 起（Identity 扩展提供开箱实现）**：`IAccountPasswordManager` 接口迁至 `TKWF.Ext.Account.Abstractions`（命名空间保持 `TKWF.Ext.Account`，ADR48 D7）；Identity V0.3.0 提供 `IdentityPasswordManager` 默认实现（组装方案 + 可配置迭代）——消费方启用 Identity + Account 双白名单即获得密码重置落地，**零手写**：

```csharp
// Identity 扩展自动注册（TryAddScoped）——Account 未注册默认实现，Identity 注册即生效
// 消费方覆盖须 AddScoped（扩展钩子先于消费方 OnRegisterDomainServices，TryAdd 被跳过）
```

> **组装方案说明（2026-09-07 实证）**：`IdentityPasswordManager.SetPasswordAsync(userName, newClientHash, salt)` 将 SecurePassword 语义的 clientHash(hex) + salt(hex) 组装为 PasswordHasher 格式 `"{iterations}.{base64salt}.{base64hash}"`——登录走标准 Password 模式（`VerifyCredentialsAsync` 明文验证）。迭代次数从 `IOptions<DomainOptions>.Auth.Pbkdf2Iterations` 注入（与框架 RequestChallenge 单一来源）。**勿用** `ChangePasswordAsync(userId, newClientHash)` 直接落地（会把 hex 当明文二次散列，登录无法验证）。

**未启用 Identity 扩展时**：消费方仍须自行实现 `IAccountPasswordManager`（适配自有用户存储）：

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
| **`ILoginHistoryService`** | 登录历史与异常检测查询（V0.3.0——消费 SecurityLog 扩展查询 API，不重复建表） | `LoginHistoryService`（本扩展，V0.5.x 升门面继承 `DomainServiceBase`——经基类 `User.Use<SecurityLog 契约>()` AOP 路径解析） |
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

### V0.5.0（已实施：V4.10.53 领域自治根治，ADR90——正确路线）
- **两 Store 继承 `DomainServiceBase`**（`AccountLockoutStore`/`PasswordResetStore`）——经基类 `User` 获取用户上下文（**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——v0.3.3 同根缺陷）；`[DiContractIgnore]` 豁免 DI001；DataService 仍经 `User.Use<具体类>()` NoAop 懒加载（DI004 零豁免）
- **注册形态改 `AddConstructibleService`**（Store）——接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方统一 `User.Use<IAccountLockoutStore>()` / `User.Use<IPasswordResetStore>()` 解析
- **Policy/Flow 改接线型**（不继承 DomainServiceBase、不注入 IDomainUser）——修复真实生产故障：旧 ctor 注入 IDomainUser 致 `AuthController.GetOptionalService` 构造失败、锁定检查静默失效；改 ctor `(IServiceProvider, IOptions, ILogger)` 全 DI 可解析；Store 经 `IServiceProvider` C1 延迟解析；注册保持 TryAddScoped（`IAccountLockoutPolicy`/`IPasswordResetFlow` 主框架契约非 `IDomainService`，AddConstructibleService 编译约束不满足）
- 测试宿主重写：真实 DI（Initializer ConfigureServices + FreeSql SQLite + AddLogging）+ `DomainUser<TestUserInfo>.BindScope` + `User.Use<接口>()` AOP 路径 + 注册形态断言（守卫工厂/throw-factory/TryAddScoped）；登录历史（`LoginHistoryService`）**V0.5.x 复核升门面**——SecurityLog V0.4.0 门面化后原接线型 `GetRequiredService` 普通解析触守卫异常，升 `DomainServiceBase` + `User.Use<SecurityLog 契约>()` AOP 解析
- 51 用例全绿（禁止 slnx 构建，仅 Account 项目 + 测试项目）

### V0.1.0（已实施）
- 账户锁定默认实现（`IAccountLockoutPolicy`——失败计数/锁定阈值/自动解锁）
- 密码重置默认实现（`IPasswordResetFlow`——随机码/过期/幂等消费/防用户枚举）
- 双实体 SG1 化 + FreeSql 持久化

### V0.2.0（已实施：Identity 深度集成）
- **`IAccountPasswordManager` 开箱适配器**（Identity V0.3.0 提供 `IdentityPasswordManager`——组装方案 + 可配置迭代；接口迁 `Account.Abstractions`）
- 重置码通知渠道（对接 Emailing 扩展发送邮件）
- 多因素认证（MFA）

### V0.3.0（已实施：登录历史与异常检测——无 UI 部分）
- **登录历史与异常检测查询服务**（`ILoginHistoryService` + `LoginHistoryService`）——消费 SecurityLog 扩展查询 API：
  - `GetLoginHistoryAsync`：分页/过滤登录历史（UserName/IP/Result/时间范围，EventType 固定 "Login"）+ DTO 投影（列表 DTO 不含 Detail/UserAgent，D5 安全决策）
  - `GetTopFailedUsersAsync` / `GetTopFailedIpsAsync`：窗口内失败次数 TopN（暴力破解/撞库检测，TimeSpan 窗口）
  - **划界**：SecurityLog 是登录历史唯一数据源（不重复建表）；SecurityLog 契约经 `IServiceProvider` 延迟解析（C1 模式，对齐 NotificationPublisher）——**未启用 SecurityLog 扩展 → 调用抛 `InvalidOperationException` 明确提示**；查询失败 → Warning + 空结果
  - **V0.4.0 依赖倒置**：SecurityLog 契约现经 `TKWF.Ext.SecurityLog.Abstractions`（ADR48 D7）引用，不再直引 SecurityLog 本体（原 NoWarn 债务已清偿）
- 管理 UI（账户锁定/解锁/重置审计）——规划（本次迭代不做）