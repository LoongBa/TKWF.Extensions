# v0.1.0 — MFA 多因素认证 开发方案

> **扩展**: `TKWF.Ext.MFA` | **版本**: v0.1.0（独立起点） | **依赖**: 主框架 v4.10.39+（CPM 现有）+ 无扩展间依赖（零耦合——见 §四 决策 1）
> **日期**: 2026-10-01 | **状态**: 📋 提议（初稿——待 Oracle 评审）
> **关联**: 对标清单 §2.2（`TKWF.Ext.MFA`，P1，能力 TOTP + 短信验证码，ABP Pro 对标 ✅）；状态跟踪 §3.1 P1 未实施 15 项（MFA 设计分类 ② 借鉴但更优——借 ABP 定位、实现走 TKWF 原生路径）；Account v0.1.0 方案（"验证码发送渠道归消费方接入"裁定）；P1 剩余 15 项用户裁定切入点（2026-10-01）

---

## 一、目的与目标

为 TKWF 增加**多因素认证（MFA）扩展**——在既有主认证（密码/短信/微信等，主框架 `AuthController` 或 `TKWF.Ext.Authentication` 或消费方自有）之上提供第二因素验证服务：**TOTP（RFC 6238 时间基一次性密码）**与**短信验证码**双方法，含绑定/解绑、挑战-验证流、尝试频控与恢复码。

目标状态（3 句内）：①`TKWF.Ext.MFA` 独立扩展（零主框架改动、零扩展间依赖）——提供 `IMfaService`（绑定管理 + 挑战-验证编排）+ `IMfaMethod`（TOTP/SMS 双实现）+ 消费方短信通道抽象 `IMfaSmsSender`；②消费方在既有登录流中编排：主认证成功 → `IsMfaEnabledAsync` 判定 → `RequestChallengeAsync` 发起挑战（短信发码/TOTP 待输入）→ `VerifyChallengeAsync` 验证 → 消费方签发令牌（MFA 不碰令牌/会话，职责单一）；③TOTP 自研 RFC 6238（零第三方 NuGet）、TOTP secret AES-GCM 加密落库、SMS 码 SHA256 落库单次消费、按（用户×方法）尝试频控 + 恢复码防锁死。

---

## 二、问题

### 问题现象

1. **主框架无任何 MFA 契约**（勘察实证：全仓 grep `Mfa|TwoFactor|Totp|MultiFactor|Authenticator|OTP` 零命中）——`IAuthController.RequestChallengeAsync` 是**密码传输加固**（PBKDF2/HMAC 防重放，challenge→verify-inline→签发一体），非第二因素；无 `VerifyChallengeAsync`、无 pending/部分认证状态、`EnumLoginAuthType` 无 MFA 成员。
2. **Account 路线图曾规划 MFA 但未落地**——Account v0.2.0 实际交付 Identity 集成 + 重置码通知渠道，MFA 始终跳过（Account 现 V0.4.0）；对标清单则独立命名 `TKWF.Ext.MFA`（归属待裁定，本方案取独立——见 §四 决策 1）。
3. **`TKWF.Ext.Authentication` 扩展无 Abstractions 包**——若 MFA 消费其 `ISmsVerificationService`/`ITokenService` 违反 ADR48 D7 L2 门控（TKWF0022 Error）；拆包是重前置（契约迁移 + 打包 + 版本升级），非 v0.1.0 必需。
4. **无现成 TOTP 设施**——`AuthAccountEntity` 无 TOTP secret 列；Authentication 扩展亦无 TOTP 能力（`ISmsVerificationService` 仅短信）。

### 触发场景

- 消费方要求"密码/短信登录 + TOTP 或短信二次验证"（网银/后台/高权限操作前强制第二因素）。
- 用户丢失手机/验证器 → 需要恢复码防锁死。
- 消费方已有主认证（框架 `AuthController` 或 Authentication 扩展或自有 IdP），只缺第二因素层——**MFA 应是可插拔的验证服务，而非重建认证**。

### 现有方案不足

- 主框架 challenge 机制：仅传输加固，**不提供第二因素语义**；复用即语义混淆（SecurityLog 已将 `RequestChallengeAsync` 映射为 `Challenge` 事件——二因素挑战须独立事件类型）。
- 主框架 `IAccountLockoutPolicy` 先例（框架调用扩展）**不适用于 MFA**：框架在 `LoginByContextAsync` 有 lockout 调用点，但**无 MFA 调用点**（加 `IMfaPolicy` + 调用点 = 框架破坏性改动 + CPM lockstep 发布周期）。
- 若并入 Account：Account 已 V0.4.0 稳定，MFA 与账户锁定/重置流程正交（第二因素 ≠ 账户策略），并包增大 Account 面。

---

## 三、使用场景

1. **后台管理强制 MFA**（常态）：消费方启用 `[TKWFEnabledExtension(MFA)]`，配置 `TKWF:Mfa` 节（默认 `RequireOnLogin: false` 由消费方判定调用）。登录编排：主认证成功 → 消费方查用户 MFA 状态 → `RequestChallengeAsync(userId, "totp")` → 前端引导输入验证器码 → `VerifyChallengeAsync` 通过 → 消费方签发令牌（可携带 `AuthLevel` 提升）。绑定流：`EnrollAsync(userId, "totp")` 生成 secret → 前端显示 provisioning URI/QR → `ConfirmEnrollAsync(userId, code)` 激活。
2. **短信二次验证**：用户未绑定验证器但绑定手机 → `EnrollAsync(userId, "sms", phone)` 存绑手机 → 挑战时 `RequestChallengeAsync` 经消费方 `IMfaSmsSender` 发码 → 验证。发送渠道归消费方（对齐 Account v0.1.0 裁定——消费方接入 Emailing/自有短信）。
3. **恢复码**：绑定/激活 MFA 时生成 8×6 位恢复码（SHA256 落库单次消费，可再生成）——验证器/手机丢失时凭恢复码解绑并重新绑定。
4. **不适用**：无第二因素需求的单因素应用（不启用即零影响）；MFA 不替代主认证（不做密码/短信首因素）；不签发令牌/不维护登录状态（登录编排归消费方）；跨实例共享频控（v0.1.0 内存窗口单实例，v0.2.0 候选 DB 化）；**⚠️ SMS MFA 多实例部署须配外部限流器（Redis 等）或单实例部署（Oracle C8——v0.1.0 内存 `SmsMaxPerHour` 不跨实例，N 实例 = 5×N 条/小时/用户是短信计费滥用真实风险）；TOTP MFA 无此限制（不发码）**。

---

## 四、决策

### 核心机制（零耦合独立扩展 + 消费方编排）

```
消费方登录流（装配层驱动，对齐框架范式）
  ├─ ① 主认证（消费方既有：框架 AuthController / Authentication 扩展 / 自有）
  ├─ ② mfaService.IsMfaEnabledAsync(userId) → 需要第二因素?
  ├─ ③ mfaService.RequestChallengeAsync(userId, method, ctx)
  │      ├─ TOTP → 无状态（返回 challengeId，前端输验证器码）
  │      └─ SMS → 生成码 SHA256 落库 + 经 IMfaSmsSender 发送 + TTL/单次消费
  ├─ ④ mfaService.VerifyChallengeAsync(userId, method, challengeId, code)
  │      └─ TOTP：RFC 6238 时窗校验（HMAC-SHA1, 30s, 6 位, ±1 窗口）+ 尝试频控
  │      └─ SMS：比对落库码（单次消费） + 尝试频控
  └─ ⑤ 消费方签发令牌/会话（既有流程——MFA 不碰令牌）
```

### 1. 归属与依赖：独立 `TKWF.Ext.MFA`，零扩展间依赖（对标清单裁定落地）

- **独立扩展**（对标清单已定名 `TKWF.Ext.MFA`；Account README 路线图并入 Account 的旧规划**作废**——Account 已 V0.4.0 稳定，第二因素与账户策略正交）。
- **零主框架改动**（不新增框架 MFA 契约/调用点——避免破坏性 + CPM lockstep 周期；`IAccountLockoutPolicy` 先例需框架调用点，MFA 无此调用点，不可套用）。
- **零扩展间依赖**（不引 Authentication 本体/不拆其 Abstractions——L2 门控零违规；短信通道经消费方抽象 `IMfaSmsSender` 注入，对齐 Account v0.1.0「验证码渠道归消费方」裁定）。
- **职责边界**：MFA = 第二因素验证服务（绑定管理 + 挑战验证 + 频控 + 恢复码）；**不签发令牌、不维护登录状态、不做主认证**——登录编排归消费方（对齐框架装配层驱动）。

### 2. 服务契约

```csharp
public interface IMfaService
{
    // 绑定管理
    Task<bool> IsMfaEnabledAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<MfaMethodInfo>> GetEnabledMethodsAsync(string userId, CancellationToken ct = default);
    Task<MfaEnrollResult> EnrollAsync(string userId, string method, MfaEnrollContext context, CancellationToken ct = default);      // TOTP: 生成 secret; SMS: 存绑手机
    Task ConfirmEnrollAsync(string userId, string method, string enrollToken, string code, CancellationToken ct = default);         // 激活（校验一次码）
    Task DisableAsync(string userId, string method, CancellationToken ct = default);                                                 // 解绑（删 secret/挑战/恢复码）
    // 挑战-验证
    Task<MfaChallengeResult> RequestChallengeAsync(string userId, string method, MfaChallengeContext? context = null, CancellationToken ct = default);
    Task<bool> VerifyChallengeAsync(string userId, string method, string challengeId, string code, CancellationToken ct = default);
    // 恢复码
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default);
    Task<bool> VerifyRecoveryCodeAsync(string userId, string code, CancellationToken ct = default);
}

public interface IMfaMethod
{
    string Method { get; }                                   // "totp" / "sms"
    Task<MfaChallengeRequest?> RequestChallengeAsync(MfaUser user, MfaChallengeContext? context, CancellationToken ct);
    Task<MfaVerifyResult> VerifyChallengeAsync(MfaUser user, string? challengeId, string code, CancellationToken ct);
    Task<MfaEnrollRequest?> EnrollAsync(MfaUser user, MfaEnrollContext context, CancellationToken ct);
    Task<MfaVerifyResult> ConfirmEnrollAsync(MfaUser user, string enrollToken, string code, CancellationToken ct);
}
```

**DTO 契约（Oracle C2 补齐——消费方编排依赖其内容）**：

```csharp
public sealed record MfaChallengeResult(string ChallengeId, string Method, string? ProvisioningUri = null);
    // TOTP：ChallengeId 为流程句柄（无码落库，验证无状态）；SMS：ChallengeId 定位落库码
public sealed record MfaEnrollResult(string Method, string EnrollToken, string? ProvisioningUri = null, IReadOnlyList<string>? RecoveryCodes = null);
    // TOTP：ProvisioningUri（otpauth:// 供 QR）；激活成功后 RecoveryCodes 一次性明文返回（消费方提示保存）
    // SMS：EnrollToken 绑定待激活句柄（无 ProvisioningUri）
public sealed record MfaVerifyResult(bool Success, int RemainingAttempts, string? FailReason = null);   // FailReason 内部日志用，对外统一 false
public sealed record MfaMethodInfo(string Method, bool IsEnabled, string? DisplayName = null);
public sealed record MfaUser(string UserId);                       // 方法上下文载体（UserId + 服务解析 enabled 状态）
public sealed record MfaEnrollContext(string? Phone = null, string? Issuer = null, string? DisplayName = null);
    // TOTP：Issuer/DisplayName 进 provisioning URI；SMS：Phone 绑定
public sealed record MfaChallengeContext(string? Phone = null, string? DeviceInfo = null);   // SMS 重发时可用 Phone 覆盖
public sealed record MfaChallengeRequest(string? ChallengeId = null, string? ProvisioningUri = null);   // IMfaMethod 内部返回
public sealed record MfaEnrollRequest(string EnrollToken, string? ProvisioningUri = null);              // IMfaMethod 内部返回
public sealed record MfaSmsMessage(string Phone, string Content);
```

- `IMfaMethod` 多实现经 `TryAddEnumerable` 注册（对齐 Authentication Provider 先例）；TOTP 无状态挑战（challengeId 为流程句柄）、SMS 落库挑战。
- `IMfaSmsSender`（消费方实现，TryAdd 语义，无默认——对齐 `ISmsSender` 先例）：`Task SendAsync(MfaSmsMessage message, CancellationToken ct)`；生产未装配 → `MfaMockForbiddenException`（503 语义，对齐 `SmsMockForbiddenException`）。

### 3. 实体（SG1 声明式，`.g.cs` 入库）+ 数据访问（红线合规具象）

| 实体 | 关键列 | 语义 |
|------|--------|------|
| `MfaSecretEntity` | UserId + Method 唯一（UX）；TOTP 场景存 **AES-GCM 加密 secret**（对齐 `PlatformCredentialEntity` 加密切钥先例）；SMS 场景存绑手机（可空 Phone）；**`IsConfirmed` + `EnrollTokenHash` + `EnrollExpireAt`（C3——绑定待激活态）** | 绑定记录（启用 = 有**已激活**记录）；`CanUpdate=false`（激活后不可改——解绑重绑走 Disable+Enroll） |
| `MfaChallengeEntity` | UserId + Method + CodeHash（SMS 场景可空——TOTP 无码）+ ExpireAt + IsConsumed（**C4：无 Attempts 列——频控归内存滑动窗口，DB 列冗余**） | 挑战票据（TTL 5min 单次消费，对齐 `OAuthTicketEntity` 一次性票据先例）；TOTP 场景仅作流程句柄（无码落库） |
| `MfaRecoveryCodeEntity` | UserId + CodeHash + IsConsumed（CanUpdate=false） | 恢复码（SHA256 落库单次消费；**8×8 位字母数字（P9——去易混淆字符 0/O/1/I/l，对齐 Google/ABP 熵量级）**；再生成 = 全量替换） |

- **数据访问（Oracle C7 具象）**：`MfaService` **直接注入三个 SG1 生成 DataService**（`MfaSecretEntityDataService`/`MfaChallengeEntityDataService`/`MfaRecoveryCodeEntityDataService`，ADR61 自动注册、零手动注册）——**零 Store 中间层**（AGENTS §8 标准路径 DataService → IEntityDAC → 实现层，扩展只依赖 DataService；如需隔离可加 internal Store 转发，非必需）。关键委托方法签名（手写分部 `.cs`，对齐 FileManagement DataService 先例）：
  ```csharp
  public Task<MfaSecretEntity?> GetActiveSecretAsync(string userId, string method, CancellationToken ct);   // WHERE user_id+method AND is_confirmed
  public Task<MfaChallengeEntity?> GetActiveChallengeAsync(string userId, string method, CancellationToken ct);
  public Task ConsumeChallengeAsync(long id, CancellationToken ct);                                         // IsConsumed=true 原子翻转（单次消费）
  public Task<MfaRecoveryCodeEntity?> GetByCodeHashAsync(string userId, string codeHash, CancellationToken ct);
  public Task ConsumeRecoveryCodeAsync(long id, CancellationToken ct);                                       // IsConsumed=true 原子翻转
  ```
- 实体经 `[DomainGenerateCode]` SG1 化；`.g.cs` 重生成纪律见下（生成物纪律）。

### 4. TOTP 自研 RFC 6238（零第三方 NuGet）

- 算法（~100 行，入扩展内部）：HMAC-SHA1 + 30s 时间步 + 6 位 + Base32 secret（16 字节随机） + **±1 时窗容差**（防时钟漂移）；输出 provisioning URI（`otpauth://totp/{issuer}:{user}?secret=...&issuer=...`）供前端 QR。
- **不引 `Otp.NET` 等 NuGet**（零外部依赖——免 ADR 外部依赖变更；RFC 6238 附录 B 测试向量可自证正确性；对齐 Calendar RRULE/Tagging 算法自研先例）。
- 正确性锚点：RFC 6238 附录 B 向量（SHA1, 8 位测试向量 → 本项目 6 位按规范截取）+ 时间步边界用例。
- **算法收纳候选**：`TKW.Framework.Utility`（对齐 ADR52 收纳先例）留作后续（需主框架改动 + 发布周期，v0.1.0 不入）。

### 5. 频控（按用户×方法尝试窗口，内存实现）

- 验证尝试频控：每（UserId, Method）**滑动窗口 5 次/5 分钟**（对齐 `SmsVerificationService` 校验 5 次/小时同量级）——内存 `ConcurrentDictionary` 实现，超限 → `InvalidOperationException`（含剩余等待）。**恢复码验证纳入 per-(UserId, "recovery") 同一频控原语（Oracle C4——恢复码 8 码 × 无限尝试可枚举，必须限流）**。
- **⚠️ 单实例语义（Oracle 裁决 Q3）**：内存窗口仅本实例生效（多实例需 DB 化）——v0.1.0 文档化"单实例频控"；跨实例共享 DB 频控列 v0.2.0 候选（对齐配额"先到先得"文档化哲学）。TOTP/SMS 验证暴力在多实例可忽略（6 位码 + 单次消费）；**SMS 发码频控（SmsMaxPerHour）在多实例 = 5×N 条/小时/用户是短信计费滥用真实风险——C8 明示单实例或外部限流器前置**。
- SMS 发码频控：沿用挑战 TTL（5min 内重发拒绝）+ 每用户小时 5 条（内存窗口，v0.1.0 单实例）。

### 6. 安全设计

- **secret AES-GCM 加密在 DataService 边界（Oracle C6 修正——`ICredentialProtector` 接口不存在）**：对齐 `PlatformCredentialEntityDataService` 先例（`AuthCenterOptions.SecretEncryptionKeyPath` 派生 AES-GCM 密钥；DB 无明文）；MFA 增 `TKWF:Mfa:SecretEncryptionKeyPath` 配置（生产缺密钥 → **fail-fast 拒启动**，对齐 Authentication 密钥 fail-fast 先例）。
- **SMS 码/恢复码 SHA256 落库**（明文不落库）、单次消费（IsConsumed 原子翻转）、TTL 5min。
- **验证码恒定时间比较**（`CryptographicOperations.FixedTimeEquals`，对齐 `ValidateSecurePasswordAsync` 先例）。
- **防枚举**：`VerifyChallengeAsync` 失败返回统一 `false`（不区分"挑战不存在/码错误/已过期"——调用方日志层自行区分）；`RequestChallengeAsync` 对未启用用户返回统一"挑战已发起"（防用户存在性探测，对齐主框架 `GetSaltAsync` 防枚举先例）；`EnrollAsync` 对已启用用户返回统一响应（Oracle Q6 补充，防绑定状态探测——v0.1.0 若仅用户自调则枚举面小，仍统一以省分支）。

### 7. Options（`[Options("TKWF:Mfa")]`，模式 A 双通道）

| 键 | 默认 | 说明 |
|----|------|------|
| `ChallengeTtlSeconds` | `300`（5min） | 挑战票据/SMS 码 TTL |
| `TotpTimeStepSeconds` | `30` | TOTP 时间步 |
| `TotpDigits` | `6` | TOTP 码位数 |
| `TotpClockSkewWindows` | `1` | TOTP 前后容差窗口 |
| `MaxVerifyAttemptsPerWindow` | `5` | 验证尝试频控上限（分钟窗口） |
| `VerifyAttemptWindowMinutes` | `5` | 验证尝试频控窗口 |
| `SmsMaxPerHour` | `5` | 短信发码每用户小时上限（内存窗口） |
| `RecoveryCodeCount` | `8` | 恢复码数量 |

### 8. Initializer（`[TKWFExtension("MFA")]`）

```csharp
services.TryAddScoped<IMfaService, MfaService>();
// ⚠️ 多 IMfaMethod 实现必须 TryAddEnumerable（TryAddScoped 同 ServiceType 仅注册首个 → SMS 静默丢失，Oracle C5）
services.TryAddEnumerable(ServiceDescriptor.Scoped<IMfaMethod, TotpMfaMethod>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IMfaMethod, SmsMfaMethod>());
// IMfaSmsSender 不注册默认（消费方实现，TryAdd 语义——对齐 ISmsSender 先例）
```

### 9. 破坏性声明

- **绿地扩展**：新包 `TKWF.Ext.MFA` 零既有 API 破坏；不触碰主框架/既有扩展。
- **消费方接线**：白名单声明 `[TKWFEnabledExtension(typeof(MFAExtensionInitializer<>))]` + `TKWF:Mfa` 配置节（缺省行为零影响——未启用即无感知）。
- **消费方前置（Oracle C8）**：启用 SMS 方法必须实现 `IMfaSmsSender`（未装配 → `MfaMockForbiddenException` fail-fast，503 语义）+ **多实例部署须外部限流器或单实例**（使用指南明示 ⚠️ 段）；启用 TOTP 方法须配置 `TKWF:Mfa:SecretEncryptionKeyPath`（缺密钥 fail-fast 拒启动）。
- **与 Authentication 的关系**：不引、不改、不拆其 Abstractions（v0.1.0）；`SmsMfaMethod` 与 `SmsVerificationService` 有逻辑重叠（6 位码 + SHA256 + 频控 + 单次消费）但语义不同（MFA 第二因素 per-user×method vs Authentication 首因素 per-phone×scene + IP——Oracle Q1 裁决：非简单复制，独立最小化正确）；后续若消费方希望 MFA 直接复用 `ISmsVerificationService`，再评估 `Authentication.Abstractions` 拆包（独立迭代，非本方案范围）。

### 10. ADR 落档（AGENTS §4——关键设计取舍）

> **编写时机（Oracle P10）**：ADR-2（独立扩展边界）+ ADR-3（TOTP 自研）是**实施前定调**决策——在编码首日前落档（三问必填，避免"先写代码再补理由"）；ADR-1（挑战票据模型）在票据实体编码时同步编写。

- `docs/MFA/ADR/ADR-MFA-挑战票据与验证模型.md`（三问必填）：挑战票据实体模型（`MfaChallengeEntity` 一次性 TTL）vs 无状态验证 vs 装饰器管道——选票据模型（对齐 `OAuthTicketEntity` 先例 + 频控挂点 + SMS 落码）；**须论证"为何 TOTP 也建票据行"**（答案：频控挂点 + 统一流，Oracle Q5 裁决）。
- `docs/MFA/ADR/ADR-MFA-独立扩展与零依赖边界.md`（三问必填）：独立 `TKWF.Ext.MFA`（对标清单裁定）vs 并入 Account vs 挂主框架 `IMfaPolicy`——选独立 + 零主框架改动 + 消费方编排（`IAccountLockoutPolicy` 先例不适用：框架无 MFA 调用点）。
- `docs/MFA/ADR/ADR-MFA-TOTP自研与密钥存储.md`（三问必填）：TOTP 自研 RFC 6238（零 NuGet，免 ADR 外部依赖变更）vs Otp.NET（`手写 RS256 JWT` 自研先例 + 附录 B 向量锚定）；secret AES-GCM 加密在 DataService 边界 + `SecretEncryptionKeyPath` 派生密钥 + 缺密钥 fail-fast。

---

## 五、测试计划

| 层 | 测试 | 断言 |
|----|------|------|
| TOTP 算法 | RFC 6238 附录 B 向量（SHA1） | 已知时间戳 → 期望码（6 位截取）；时间步边界（步切换前后 ±1 窗口内可验） |
| TOTP 算法 | Base32 secret 生成 | 16 字节随机 → Base32 可逆；provisioning URI 格式（`otpauth://totp/...`） |
| TOTP 算法 | **运行时交叉验证（P11）** | 生成 secret → 同 secret 按当前时间步算码 → 立即验证通过（自洽——附录 B 是固定时间戳，运行时正确性需自洽锚定） |
| TotpMfaMethod | 验证成功/失败/时窗漂移 | 正确码通过；错码失败；±1 窗口内通过、±2 失败 |
| SmsMfaMethod | 发码/验证/过期/单次消费 | 发码经 `IMfaSmsSender` 收到；正确码通过；重复使用同码失败（单次消费）；过期失败；TTL 内重发拒绝 |
| SmsMfaMethod | **并发单次消费竞态（C1）** | 两并发 `VerifyChallengeAsync` 同 challengeId → 恰一成功一失败（IsConsumed 原子翻转——文件模式 SQLite，对齐 FileManagement v0.3.0 并发加固先例） |
| MfaService | Enroll/Confirm/Disable 全流程 | TOTP 生成 secret → Confirm（校验一次码）激活 → 启用判定 true；SMS 绑手机 → 挑战发码 → 验证通过；Disable 后 IsEnabled false + 挑战拒绝 |
| MfaService | **EnrollToken 过期/重放（C3）** | ConfirmEnroll 用过期 enrollToken → 失败；激活成功后同 enrollToken 重放 → 失败（单次消费）；激活前 EnrollTokenHash 落库 + 激活后清空 |
| MfaService | 挑战-验证编排 | RequestChallenge → VerifyChallenge 通过；挑战不存在/已消费/过期 → false（统一失败不区分） |
| MfaService | 尝试频控 | 5 次/5 分钟窗口超限 → `InvalidOperationException`（含剩余等待）；窗口滑动后恢复；**恢复码验证同窗口生效（C4）** |
| MfaService | 恢复码 | 生成 8 码 → 验证正确码通过单次消费 → 再使用失败；验证错误码失败（计入频控）；再生成全量替换；**恢复码明文不落库（DB 仅 CodeHash，C1 补充断言）**；**并发验证同码 → 恰一成功（C1）** |
| MfaService | 防枚举 | 未启用用户 RequestChallenge 返回统一"已发起"；VerifyChallenge 统一 false；已启用用户 EnrollAsync 返回统一响应（Oracle Q6） |
| DataService/实体 | `.g.cs` 映射 + 持久化 | 重跑 `run-xcodegen.ps1 -Ext MFA` 后 DTO/DataService 含三实体映射（AGENTS §8 #4）；secret 密文落库（明文不落库）；**缺 `SecretEncryptionKeyPath` 拒启动 fail-fast（Oracle Q8-4）** |
| 回归 | slnx 全量 | 绿地扩展零回归（全量 1512 用例基线） |

> 测试宿主对齐：`MfaTestHost`（SQLite 内存 + Noop/Recording 事务 + `StubDomainUser` 模式 + 真实 DataService 链——照 `FileManagementTestHost` 先例）；`IMfaSmsSender` 用记录型 Fake（捕获 `MfaSmsMessage`）。

## 六、风险与缓解

| 风险 | 缓解 |
|------|------|
| 消费方编排复杂度（主认证 → MFA → 签发需自行组装） | 使用指南给完整编排样例（含框架 AuthController + Authentication 双路径）；MFA 职责单一换来零耦合/零破坏 |
| 频控单实例（多实例部署绕过） | v0.1.0 文档化"单实例频控"边界 + 明确 v0.2.0 DB 化候选；**SMS 发码频控多实例 = 短信计费滥用 → C8 明示外部限流器或单实例前置**（TOTP 暴力多实例可忽略） |
| TOTP 自研正确性风险 | RFC 6238 附录 B 官方向量锚定 + 时窗边界用例 + **P11 运行时自洽交叉验证** + 恒定时间比较 |
| SMS 渠道未装配（生产 503） | `MfaMockForbiddenException` fail-fast（对齐 `SmsMockForbiddenException` 先例）；使用指南明示必须实现 `IMfaSmsSender` |
| secret 密钥缺失（生产） | `SecretEncryptionKeyPath` 未配置 → fail-fast 拒启动（对齐 Authentication 密钥 fail-fast） |
| 恢复码丢失（绑定激活时未保存） | 激活成功响应含恢复码一次性明文返回（消费方提示用户保存）；再生成覆盖；**格式 8 位字母数字（P9——熵量级对齐 Google/ABP）** |
| EnrollToken 重放劫持绑定 | EnrollTokenHash 落库 + 单次消费 + TTL 5min（C3）；过期/重放测试 |
| 生成物陈旧 | 实体变更重跑 `run-xcodegen.ps1 -Ext MFA` 提交 `.g.cs`（AGENTS §8 纪律）+ 测试断言映射 |
| 误用框架 `RequestChallengeAsync`（语义混淆） | 使用指南 + ADR 明示：框架 challenge 是密码传输加固（SecurityLog Challenge 事件），MFA 挑战独立方法/独立安全事件（v0.2.0 可接 SecurityLog 新事件类型） |

## 七、Oracle 评审状态

| 轮次 | 结论 | 说明 |
|------|------|------|
| 一次（bg_025c18fc） | **PASS WITH CONDITIONS** | 11 条件全部吸收（8 CONDITION + 3 P2）：①C1 测试计划补并发挑战单次消费竞态（SMS 挑战 + 恢复码，文件模式 SQLite）②C2 服务契约 DTO 定义块补齐 ③C3 EnrollToken 落 MfaSecretEntity（IsConfirmed/EnrollTokenHash/EnrollExpireAt）+ 过期/重放测试 ④C4 恢复码验证纳入频控 + 删 MfaChallengeEntity.Attempts 列 ⑤C5 Initializer 代码修正（仅 TryAddEnumerable）⑥C6 删虚构 `ICredentialProtector`，改 DataService 边界 AES-GCM + `SecretEncryptionKeyPath` + 缺密钥 fail-fast ⑦C7 数据访问具象（MfaService 直注三 DataService + 委托方法签名）⑧C8 多实例 SMS 滥用文档化（外部限流器/单实例前置）+ P9 恢复码 8 位字母数字 + P10 ADR 实施前落档 + P11 TOTP 运行时自洽交叉验证。开放问题 8 项逐项裁决全确认（Q1 独立+消费方编排最优 / Q2 自研正确 / Q3 单实例频控可接受（SMS 加强文档）/ Q4 恢复码进 v0.1.0 / Q5 三实体无冗余 / Q6 防枚举充分 / Q7 Options 默认合理 / Q8 测试主干覆盖） |

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-10-01 | 📋 提议 | 初稿——基于三路勘察（Authentication 扩展面 + 主框架认证面 + 既有 MFA 规划：对标清单命名 `TKWF.Ext.MFA` + 状态跟踪分类 ② + Account 裁定「验证码渠道归消费方」）；设计定调：独立扩展零依赖 + 消费方编排 + TOTP 自研 + 挑战票据模型；待 Oracle 评审 |
| 2026-10-01 | 📋 提议（修订 1） | **Oracle 评审 PASS WITH CONDITIONS（bg_025c18fc）11 条件全部吸收**：DTO 契约块补齐（C2）、EnrollToken 落实体列 + 过期/重放测试（C3）、恢复码纳入频控 + 删 Attempts 列（C4）、Initializer 修正（C5）、加密机制修正为 DataService 边界 AES-GCM + SecretEncryptionKeyPath + fail-fast（C6）、数据访问具象化（C7）、多实例 SMS 文档化（C8）+ 恢复码 8 位字母数字（P9）+ ADR 实施前落档（P10）+ TOTP 运行时交叉验证（P11） |
