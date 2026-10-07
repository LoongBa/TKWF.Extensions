# TKWF.Ext.MFA

> TKWF 扩展：**多因素认证**——TOTP（RFC 6238 自研，零第三方）+ 短信验证码双方法：绑定/解绑 + 挑战-验证流 + 尝试频控 + 恢复码。
> **独立扩展零依赖**（ADR-MFA-独立扩展与零依赖边界）：不引 Authentication/不改主框架——短信渠道经消费方抽象 `IMfaSmsSender` 注入（TryAdd 无默认）；登录编排归消费方（MFA 不签令牌/不维护登录状态）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.MFA` |
| 版本 | v0.1.0（独立起点）+ **V0.1.2（V4.10.53 ADR90 领域自治根治，V0.1.1 已为既有 tag——`MfaService` 继承 `DomainServiceBase`（经基类 `User` 取上下文——IDomainUser 永不注册 DI）+ 注册改 `AddConstructibleService`（接口可构造守卫工厂），消费方统一 `User.Use<IMfaService>()` 解析）** + **V0.1.3（V4.10.55 ADR92/T3 闭环——2 Method 改 `TryAddEnumerableConstructible` + 继承 DomainServiceBase，集合版守卫工厂帧内供给）** + **V0.2.0（E4 密钥管理抽象——删 `MfaSecretKeyStore`；`TotpMfaMethod` 注入 keyed `ISymmetricKeyProvider`（`SymmetricKeyProviderKeys.Mfa`）；MfaService 不再 Initialize 密钥副作用；修正 DataService 陈旧注释；格式统一单段）** |
| 依赖 | `TKWF.Domain`（CPM）+ SG1（框架既有）；**零扩展间依赖** |
| 数据 | 表 `MfaSecret` + `MfaChallenge` + `MfaRecoveryCode`（框架 `SyncTables` 统一建表） |

## 架构分层

```
IMfaService / MfaService                      # 门面（绑定管理 + 挑战-验证编排 + 恢复码 + 频控）——继承 DomainServiceBase + AddConstructibleService 注册（V4.10.53 领域自治根治）
├── IMfaMethod（TryAddEnumerableConstructible 多实现——V4.10.55 ADR92 集合版守卫工厂）
│     ├── TotpMfaMethod                       # TOTP（RFC 6238 自研——TotpGenerator，零 NuGet）
│     └── SmsMfaMethod                        # 短信（SmsMfaMethod + 消费方 IMfaSmsSender 渠道）
├── MfaSecretEntityDataService                # SG1 DataService（secret 密文落库；加解密在方法层 TotpMfaMethod 经 keyed ISymmetricKeyProvider）
├── MfaChallengeEntityDataService             # 挑战票据（一次性 TTL + 单次消费）
└── MfaRecoveryCodeEntityDataService          # 恢复码（SHA256 + 单次消费）

IMfaSmsSender / MfaSmsMessage                 # 消费方实现（TryAdd 无默认——对齐 ISmsSender 先例）
IRateLimitCheck（框架 Utility v4.10.67）      # 频控契约（DI 注入 + Initializer TryAddSingleton fallback MemoryRateLimitCheck——
                                               # 消费方显式注册 SqlCountRateLimitCheck 等实现时静默替换；未启用 RateLimiting 扩展频控"始终在"）
```

- **数据访问红线**：Service 层只依赖 SG1 DataService（ADR61 自动注册，零手动注册）——零 IFreeSql/IEntityDAC 直注入。
- **零扩展间依赖（ADR48 D7 L2 门控）**：不引 `TKWF.Ext.Authentication` 本体、不拆其 Abstractions——短信渠道经消费方 `IMfaSmsSender` 注入（对齐 Account「验证码渠道归消费方」裁定）。
- **AES-GCM 加密在方法实现层**：`TotpMfaMethod` 经 keyed `ISymmetricKeyProvider`（框架 TKW.Framework.Domain.KeyManagement v4.10.61，`SymmetricKeyProviderKeys.Mfa`——`FileSymmetricKeyProvider` 自 `TKWF:Mfa:SecretEncryptionKeyPath` 派生密钥，生产缺密钥 fail-fast）加解密 secret——密文落库（明文不落库）。`MfaService` 构造不再有 Initialize 密钥副作用。

## 核心能力

### 绑定管理

| API | 说明 |
|-----|------|
| `IsMfaEnabledAsync(userId)` | 用户是否已启用 MFA（任一方法已激活） |
| `GetEnabledMethodsAsync(userId)` | 方法启用列表 |
| `EnrollAsync(userId, method, context)` | 发起绑定——TOTP 生成 secret + provisioning URI；SMS 存绑手机 + 自动发码；**已启用返回统一形态（防枚举）** |
| `ConfirmEnrollAsync(userId, method, enrollToken, code)` | 确认绑定（EnrollToken 单次消费 + TTL + 一次码）→ 激活 + **首次生成恢复码一次性返回** |
| `DisableAsync(userId, method)` | 解绑（级联删绑定 + 未消费挑战 + 恢复码） |

### 挑战-验证

| API | 说明 |
|-----|------|
| `RequestChallengeAsync(userId, method, context?)` | 发起挑战——TOTP 票据句柄（无码落库）/ SMS 生成码 SHA256 落库 + 发送；**未启用返回统一"已发起"（防枚举）** |
| `VerifyChallengeAsync(userId, method, challengeId, code)` | 验证挑战——TOTP RFC 6238 时窗（±1 容差）/ SMS 落库比对（恒定时间）；**失败统一 false**；尝试频控（5 次/5min 滑动窗口按用户×方法，超限抛异常含剩余等待） |

### 恢复码（防锁死）

| API | 说明 |
|-----|------|
| `GenerateRecoveryCodesAsync(userId)` | 生成/再生成（8 位字母数字 × 8 枚，去易混淆字符；全量替换旧码） |
| `VerifyRecoveryCodeAsync(userId, code)` | 验证（SHA256 + 单次消费 + 纳入频控）——凭码解绑重绑 |

## Options 配置（`TKWF:Mfa`）

| 键 | 默认 | 说明 |
|----|------|------|
| `ChallengeTtlSeconds` | `300` | 挑战票据/SMS 码 TTL（5min，对齐 OAuthTicket） |
| `TotpTimeStepSeconds` | `30` | TOTP 时间步（RFC 6238） |
| `TotpDigits` | `6` | TOTP 码位数 |
| `TotpClockSkewWindows` | `1` | TOTP ±容差窗口（防时钟漂移，对齐 Google Authenticator） |
| `MaxVerifyAttemptsPerWindow` | `5` | 验证尝试频控上限 |
| `VerifyAttemptWindowMinutes` | `5` | 验证尝试频控窗口 |
| `SmsMaxPerHour` | `5` | 短信发码每用户小时上限（⚠️ 多实例外部限流器/单实例，Oracle C8） |
| `RecoveryCodeCount` | `8` | 恢复码数量 |
| `SecretEncryptionKeyPath` | `null` | TOTP secret AES-GCM 密钥文件（前 32 字节；生产必填 fail-fast） |
| `IsProduction` | `false` | 生产门（密钥缺失 fail-fast） |

## 约束与语义

- **挑战票据模型（ADR-MFA-挑战票据与验证模型）**：`MfaChallengeEntity` 一次性 TTL + `IsConsumed` 原子翻转单次消费（防重放）；TOTP 无码落库（票据仅句柄 + 频控挂点）；不设 Attempts 列（频控归内存窗口）。
- **防枚举（Oracle Q6）**：验证失败统一 `false`；未启用挑战返回统一"已发起"；已启用重复绑定返回统一形态。
- **恒定时间比较**：SMS 码/恢复码/TOTP 比对经 `CryptographicOperations.FixedTimeEquals`。
- **secret 加密（ADR-MFA-TOTP自研与密钥存储）**：TOTP 自研 RFC 6238（HMAC-SHA1 + 30s + 6 位 + Base32 + ±1 窗口，附录 B 向量锚定）；secret AES-GCM 密文落库（`SecretEncryptionKeyPath` 派生密钥，生产缺密钥 fail-fast）；解密异常语义：**格式非法（单段 blob 结构损坏）/认证失败（tag 不匹配）抛 `CryptographicException`（`AuthenticationTagMismatchException` 为其派生）；非法 base64 抛 `FormatException`**。
- **频控单实例（Oracle Q3/C8）**：v0.1.0 内存滑动窗口仅本实例生效——TOTP/SMS 验证暴力多实例可忽略（6 位码 + 单次消费）；**SMS 发码多实例 = 短信计费滥用 → 外部限流器/单实例前置**；DB 化跨实例频控 v0.2.0 候选。⚠️ **TTL 内重发拒绝在单实例极端并发下存在理论 TOCTOU 窗口**（两并发请求可同时过活动挑战检查 → 同时创建挑战双发 SMS）——`SmsMaxPerHour` 频控器提供二级防护（至多 5 条/小时/用户）；v0.2.0 拟加 DB 唯一约束无条件消除。
- **频控 Provider 切换注记（V4.10.67 R3，Oracle7 RC2）**：频控经 DI 注入 `IRateLimitCheck`（Initializer fallback `MemoryRateLimitCheck` 滑动窗口）——消费方显式注册 `SqlCountRateLimitCheck` 时窗口边界行为由 sliding 变为 fixed，窗口边界突发差异（fixed 窗口边界重计可能瞬时放行）MFA 验证低频可接受；R4 文档统一成文。
- **恢复码**：SHA256 落库（明文不落库）、单次消费、验证纳入频控、再生成全量替换；丢失恢复码 + 设备丢失 = 锁死（防锁死是恢复码的意义）。
- **零扩展间依赖**：`SmsMfaMethod` 与 Authentication `SmsVerificationService` 逻辑重叠但语义不同（第二因素 per-user×method vs 首因素 per-phone×scene + IP）——独立实现正确（Oracle Q1 裁决）；收敛出口 = 后续评估 `Authentication.Abstractions` 拆包（独立迭代）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.MFA;

[TKWFEnabledExtension(typeof(MFAExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 短信方法——消费方实现 IMfaSmsSender（TryAdd 注册；未装配 → MfaMockForbiddenException 503 fail-fast）
services.TryAddScoped<IMfaSmsSender, MySmsSender>();

// V4.10.53（领域自治根治，ADR90 正确路线）：门面经 User.Use<IMfaService>() 消费
//（禁构造注入——DI004 零豁免；领域服务继承 DomainServiceBase 经基类 User 取上下文）
public class LoginService : DomainServiceBase
{
    public LoginService(IDomainUser user) : base(user) { }

    public async Task<bool> RequireSecondFactorAsync(string userId, string code)
    {
        var mfa = User.Use<IMfaService>();
        var challenge = await mfa.RequestChallengeAsync(userId, "totp");
        return await mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, code);
    }
}
```

三钩子自动接线。DI 注册形态（V4.10.53 → V4.10.55）：`IMfaService` 经 `AddConstructibleService`（接口可构造守卫工厂 + 实现类 throw-factory——域作用域外解析即抛）；`IMfaMethod` 双实现 **`TryAddEnumerableConstructible`**（V4.10.55 ADR92 集合版守卫工厂——`MfaService` 经 `User.Use<IMfaService>()` 帧内创建时枚举集合，守卫工厂经 CurrentAopUser 供给 ctor IDomainUser；帧外枚举抛守卫；消费方自定义实现优先）；`IMfaSmsSender` 消费方 `TryAddScoped`（无默认实现）。

## 数据模型

```sql
MfaSecret(id BIGINT PK, user_id VARCHAR(128), method VARCHAR(20), secret_encrypted VARCHAR(512) NULL,
          phone VARCHAR(32) NULL, is_confirmed BOOLEAN, enroll_token_hash VARCHAR(64) NULL,
          enroll_expire_at TIMESTAMP NULL, create_time TIMESTAMP, update_time TIMESTAMP)
MfaChallenge(id BIGINT PK, user_id VARCHAR(128), method VARCHAR(20), code_hash VARCHAR(64) NULL,
             expire_at TIMESTAMP, is_consumed BOOLEAN, create_time TIMESTAMP)
MfaRecoveryCode(id BIGINT PK, user_id VARCHAR(128), code_hash VARCHAR(64), is_consumed BOOLEAN, create_time TIMESTAMP)
-- 索引：UX_MfaSecret_User_Method（user_id,method 联合唯一）/ IX_MfaSecret_User
--      IX_MfaChallenge_User_Method / IX_MfaChallenge_Expire
--      UX_MfaRecovery_User_CodeHash（user_id,code_hash 联合唯一）/ IX_MfaRecovery_User
```

- 删除语义：**物理删除**（不声明 `IsDeleted`，`hasSoftDelete:false`）——Disable 解绑删绑定 + 未消费挑战 + 恢复码（级联）。
- `secret_encrypted` 为 AES-GCM 密文（单段 `base64(nonce[12]‖cipher‖tag[16])`——明文不落库，`DtoFieldIgnore` 不入 DTO）；`code_hash`/`enroll_token_hash`/`recovery code_hash` 为 SHA256 十六进制小写（明文不落库）。
- 绑定激活后 `secret_encrypted`/`phone` 不可变（`CanUpdate=false`）——解绑重绑走 Disable+Enroll。
- 生产建表：框架 `SyncTables` 统一托管（ADR49），**无手工 DDL 前置**。

## 后续演进（v0.2.0+ 候选）

跨实例 DB 频控（对齐 SecurityLog 清理范式）；挑战票据过期清理任务（对齐 BackgroundJobs 清理）；SecurityLog 挑战事件接入（独立安全事件类型——框架 `Challenge` 事件是密码传输加固，勿混用）；TOTP 算法收纳主框架 `TKW.Framework.Utility`（ADR52 先例）；设备可信/记住设备；多因子组合（TOTP + SMS 同时要求）。

<!-- EOF -->
