# MFA 多因素认证扩展 使用指南

> `TKWF.Ext.MFA`——在既有主认证（密码/短信/微信等）之上提供**第二因素验证服务**：TOTP（RFC 6238 自研）+ 短信验证码双方法，含绑定/解绑、挑战-验证流、尝试频控与恢复码。
> **独立扩展零依赖**（ADR-MFA-独立扩展与零依赖边界）：不引 Authentication、不改主框架——登录编排归消费方。

---

## 1. 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.MFA` |
| 版本 | v0.1.0（+ **V0.1.2** V4.10.53 领域自治根治 / **V0.1.3** V4.10.55 ADR92——IMfaMethod 双实现 TryAddEnumerableConstructible 集合版守卫工厂 / **V0.2.0** E4 密钥管理抽象——删 `MfaSecretKeyStore`、`TotpMfaMethod` 注入 keyed `ISymmetricKeyProvider`） |
| 依赖 | 主框架 `TKWF.Domain`（CPM）+ SG1（框架既有）；**零扩展间依赖**（短信渠道经消费方抽象 `IMfaSmsSender` 注入） |
| 数据 | 表 `MfaSecret` + `MfaChallenge` + `MfaRecoveryCode`（框架 `SyncTables` 统一建表） |
| 职责 | 第二因素验证服务（绑定管理 + 挑战验证 + 频控 + 恢复码）；**不签发令牌、不维护登录状态、不做主认证** |

## 2. 启用与配置

> 📌 **公共接线机制**（发现≠启用 / 三钩子 / Web 钩子正交 / 启动期安全解析）见 [《扩展模块通用指南》](../扩展模块通用指南.md) §二/§四——以下仅列本扩展特有接线。

### 2.1 白名单声明 + 依赖

```csharp
using TKWF.Ext.MFA;

[TKWFEnabledExtension(typeof(MFAExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

- **短信方法**：必须实现 `IMfaSmsSender`（TryAdd 注册）——未装配时发送抛 `MfaMockForbiddenException`（503 语义 fail-fast）。
- **TOTP 方法**：必须配置 `TKWF:Mfa:SecretEncryptionKeyPath`（生产缺密钥 fail-fast 拒启动）。

### 2.2 配置选项（`TKWF:Mfa` 节）

```jsonc
{
  "TKWF": {
    "Mfa": {
      "ChallengeTtlSeconds": 300,            // 挑战票据/SMS 码 TTL（5min）
      "TotpTimeStepSeconds": 30,             // TOTP 时间步（RFC 6238）
      "TotpDigits": 6,                       // TOTP 码位数
      "TotpClockSkewWindows": 1,             // TOTP ±容差窗口（防时钟漂移）
      "MaxVerifyAttemptsPerWindow": 5,       // 验证尝试频控上限（5 次）
      "VerifyAttemptWindowMinutes": 5,       // 验证尝试频控窗口（5min）
      "SmsMaxPerHour": 5,                    // 短信发码每用户小时上限
      "RecoveryCodeCount": 8,                // 恢复码数量（8 位字母数字）
      "SecretEncryptionKeyPath": "/keys/mfa-aes.key",   // TOTP secret AES-GCM 密钥（前 32 字节；生产必填）
      "IsProduction": true                   // 生产门（密钥缺失 fail-fast）
    }
  }
}
```

| 键 | 默认 | 说明 |
|----|------|------|
| `ChallengeTtlSeconds` | `300` | 挑战票据/SMS 码 TTL（对齐 OAuthTicket 同窗） |
| `TotpTimeStepSeconds` | `30` | TOTP 时间步（RFC 6238 标准） |
| `TotpDigits` | `6` | TOTP 码位数（RFC 6238 标准） |
| `TotpClockSkewWindows` | `1` | TOTP 前后容差窗口（±1 步，对齐 Google Authenticator） |
| `MaxVerifyAttemptsPerWindow` | `5` | 验证尝试频控上限（滑动窗口，按用户×方法） |
| `VerifyAttemptWindowMinutes` | `5` | 验证尝试频控窗口 |
| `SmsMaxPerHour` | `5` | 短信发码每用户小时上限（⚠️ 多实例见 §6） |
| `RecoveryCodeCount` | `8` | 恢复码数量（8 位字母数字，去易混淆字符） |
| `SecretEncryptionKeyPath` | `null` | TOTP secret AES-GCM 密钥文件路径（前 32 字节；生产必填） |
| `IsProduction` | `false` | 生产门——true 时密钥缺失 fail-fast 拒启动 |

## 3. 快速开始

### 3.1 TOTP 绑定（验证器扫码）

```csharp
var mfa = sp.GetRequiredService<IMfaService>();   // 或注入

// ① 发起绑定——生成 secret + provisioning URI（前端转 QR）
var enroll = await mfa.EnrollAsync(userId, "totp", new MfaEnrollContext(Issuer: "MyApp", DisplayName: userName));
//   enroll.ProvisioningUri → 二维码（otpauth://totp/...）

// ② 用户用验证器 App 扫码 → 输入 6 位码
var recoveryCodes = await mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, code);
//   ⚠️ recoveryCodes 一次性明文返回——提示用户保存（丢失后经 GenerateRecoveryCodesAsync 再生成）
```

### 3.2 登录流编排（主认证 → MFA 挑战 → 签发）

```csharp
// ① 主认证（消费方既有：框架 AuthController / Authentication 扩展 / 自有）——成功后拿到 userId

// ② 判定是否需要第二因素
if (await mfa.IsMfaEnabledAsync(userId))
{
    // ③ 发起挑战（TOTP：前端引导输入验证器码；SMS：自动发码）
    var challenge = await mfa.RequestChallengeAsync(userId, "totp");   // 或 "sms"

    // ④ 用户提交码 → 验证（失败统一 false——不区分挑战不存在/码错/过期，防枚举）
    bool verified = await mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, userCode);

    // ⑤ 验证通过 → 消费方签发令牌/会话（既有流程——MFA 不签令牌）
    if (verified) { /* tokenService.IssueTokenAsync(...) 或登录会话 */ }
}
```

### 3.3 SMS 方法

```csharp
// 绑定：EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: "138xxxx")) —— 自动发码到手机
var enroll = await mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
var codes = await mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, smsCode);

// 挑战：RequestChallengeAsync(userId, "sms") —— 自动发码（经消费方 IMfaSmsSender）
// 验证：VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, smsCode)
```

### 3.4 恢复码

```csharp
// 验证器/手机丢失——凭恢复码解绑重绑（防锁死）
bool ok = await mfa.VerifyRecoveryCodeAsync(userId, recoveryCode);
if (ok) await mfa.DisableAsync(userId, "totp");   // 解绑后重新绑定

// 再生成（全量替换旧码——消费方提示用户保存新码）
var newCodes = await mfa.GenerateRecoveryCodesAsync(userId);
```

### 3.5 解绑

```csharp
await mfa.DisableAsync(userId, "totp");   // 级联删除：绑定 + 未消费挑战 + 恢复码
```

## 4. 服务契约速查

| API | 说明 |
|-----|------|
| `IsMfaEnabledAsync(userId)` | 用户是否已启用 MFA（任一方法已激活） |
| `GetEnabledMethodsAsync(userId)` | 方法启用列表（含未启用方法信息） |
| `EnrollAsync(userId, method, context)` | 发起绑定——TOTP 返回 ProvisioningUri；SMS 存绑手机 + 发码；已启用返回统一形态（防枚举） |
| `ConfirmEnrollAsync(userId, method, enrollToken, code)` | 确认绑定（校验 EnrollToken 单次消费 + 一次码）→ 激活 + **首次生成恢复码返回** |
| `DisableAsync(userId, method)` | 解绑（级联删挑战 + 恢复码） |
| `RequestChallengeAsync(userId, method, context?)` | 发起挑战——TOTP 票据句柄 / SMS 发码落库；未启用返回统一"已发起"（防枚举） |
| `VerifyChallengeAsync(userId, method, challengeId, code)` | 验证挑战——TOTP 时窗 / SMS 落库比对；失败统一 false |
| `GenerateRecoveryCodesAsync(userId)` | 再生成恢复码（全量替换） |
| `VerifyRecoveryCodeAsync(userId, code)` | 验证恢复码（SHA256 + 单次消费 + 频控） |

## 5. 与既有扩展划界

| 扩展 | 边界 |
|------|------|
| `TKWF.Ext.Authentication` | 认证中心做主认证 + 令牌——MFA **不引**其契约（零扩展间依赖，ADR48 D7 L2 门控零违规）；短信渠道经消费方 `IMfaSmsSender` 抽象（可包装 Authentication 的 `ISmsSender` 或自有短信） |
| `TKWF.Ext.Account` | 账户锁定/密码重置（首因素域）——MFA 是第二因素，正交 |
| 主框架 `AuthController.RequestChallengeAsync` | ⚠️ 是**密码传输加固**（PBKDF2/HMAC 防重放），非第二因素——MFA 挑战用独立方法/独立票据，勿混用 |

## 6. 生产注意事项

- **多实例部署 ⚠️（Oracle C8）**：v0.1.0 频控为**内存单实例**（验证尝试 + 短信发码）——SMS MFA 多实例 = 5×N 条/小时/用户（短信计费滥用），**须外部限流器（Redis 等）或单实例部署**；TOTP MFA 无此限制（不发码）。
- **TTL 内重发拒绝（理论 TOCTOU）⚠️**：活动挑战检查与建行非原子——单实例**极端并发**下两请求可同时通过检查、同时创建挑战双发 SMS（理论窗口）；`SmsMaxPerHour` 频控器提供二级防护（至多 5 条/小时/用户）；v0.2.0 拟加 DB 唯一约束无条件消除。
- **密钥 fail-fast**：`SecretEncryptionKeyPath` 生产缺失 → 拒启动（对齐 Authentication 密钥策略）；密钥文件前 32 字节为 AES-GCM 密钥，**不进代码库**。
- **加密边界（实施注记）**：TOTP secret 的 AES-GCM 加解密在**方法实现层**（`TotpMfaMethod` 注入 keyed `ISymmetricKeyProvider`——框架 TKW.Framework.Domain.KeyManagement v4.10.61，`SymmetricKeyProviderKeys.Mfa`，E4 V0.2.0）；密文落库、DB 无明文、生产缺密钥 fail-fast（`FileSymmetricKeyProvider` 自 `TKWF:Mfa:SecretEncryptionKeyPath` 派生密钥），与方案"DataService 边界"（Oracle C6 意图）安全语义等价。**解密异常语义**：格式非法（单段 blob 结构损坏）/认证失败（tag 不匹配）抛 `CryptographicException`（`AuthenticationTagMismatchException` 为其派生）；非法 base64 抛 `FormatException`。
- **防枚举**：`VerifyChallengeAsync` 失败统一 `false`（不区分原因）；`RequestChallengeAsync` 对未启用用户返回统一"已发起"——日志层自行区分。
- **恢复码保存**：激活/再生成时一次性明文返回——消费方提示用户保存；丢失后凭已保存恢复码解绑重绑（无恢复码 + 设备丢失 = 锁死，防锁死是恢复码的意义）。
- **UTC 时间**：挑战 TTL/过期经 `DateTime.UtcNow` 判定；消费方时区无关。
- **建表**：三表由框架 `SyncTables` 统一托管（ADR49），无手工 DDL 前置。

## 7. 边界与语义速查

| 场景 | 行为 |
|------|------|
| 绑定 TOTP | `EnrollAsync` 生成 secret（AES-GCM 密文落库）+ provisioning URI → `ConfirmEnrollAsync` 输一次码激活 |
| 绑定 SMS | `EnrollAsync` 存绑手机 + 自动发码 → `ConfirmEnrollAsync` 输码激活 |
| 已启用用户重复绑定 | 统一形态返回（不抛——防枚举） |
| 挑战验证失败 | 统一 `false`（挑战不存在/码错/过期不区分） |
| 验证尝试超限 | `InvalidOperationException`（含剩余等待；5 次/5min 滑动窗口按用户×方法） |
| SMS 发码超限 | `InvalidOperationException`（5 条/小时/用户；TTL 内重发拒绝） |
| 短信渠道未装配 | `MfaMockForbiddenException`（503 语义 fail-fast） |
| 短信发送失败（渠道异常） | 孤儿挑战行清理 + 异常自然传播（消费方可感知失败立即重试——无 TTL 阻塞；Oracle7 C1） |
| TOTP secret 密钥缺失（生产） | fail-fast 拒启动 |
| 恢复码 | 8 位字母数字 × 8 枚，SHA256 落库，单次消费，验证纳入频控，再生成全量替换 |
| 解绑 | 级联删绑定 + 未消费挑战 + 恢复码 |
| 多实例 SMS 频控 | ⚠️ 内存单实例——外部限流器或单实例部署 |

## 8. 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-06 | V0.2.0 | **E4 密钥管理抽象（框架 v4.10.61 配套）**：删 `MfaSecretKeyStore` 静态密钥类——`TotpMfaMethod` ctor 注入 keyed `ISymmetricKeyProvider`（`SymmetricKeyProviderKeys.Mfa`，消费点即注入点）；`MfaService` 构造不再有 Initialize 密钥副作用；测试去反射（`ResetMfaSecretKeyStore` 改注入式 internal Reset）；修正 `MfaSecretEntityDataService` 陈旧注释；AES-GCM 单段规范格式（AeadEncryptionUtil）。消费方零配置变更。65 用例全绿 |
| 2026-10-01 | v0.1.0 | 首版——TOTP（RFC 6238 自研）+ 短信验证码双方法：绑定/解绑 + 挑战-验证流 + 尝试频控 + 恢复码；独立扩展零依赖（消费方编排）；Oracle 评审 PASS WITH CONDITIONS 11 条件全吸收（方案 `docs/MFA/MFA多因素认证-开发方案.md`） |
| 2026-10-01 | —（v0.1.0 后置补丁） | Oracle7 审核 C1 修复 + C2/C3 文档对齐——SMS 发送失败清理孤儿挑战行（`SendCodeAsync` 发送 try/catch → 物理删除挑战 + 再抛，防 TTL 内孤儿阻塞重发）；新增 2 测试用例（失败清理无残留 + 渠道恢复立即重发）；文档修正解密异常语义（`CryptographicException`/`FormatException`）并补充 TTL 重发 TOCTOU 边界 |
