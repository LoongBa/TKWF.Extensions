# TKWF.Ext.SmsCode 短信验证码扩展技术规范

**状态**: 短信验证码引擎（实体型——SG1 数据底座） | **版本**: V0.1.0（M1-M4 完成） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain（IDomainService / DomainServiceBase / [Options]）+ FreeSql（实体持久化）+ `TKWF.Ext.Captcha.Abstractions`（门禁契约，**可空依赖**，L2——Captcha 未装配时仅门禁场景守卫异常）+ TKWF.CodeGeneration（SG1 纯 Analyzer）

**核心约束**: X' 冻结项（Subject 不透明键控不叫 Phone / 错误契约分层 / sender 适配）/ SHA256 小写 hex + FixedTimeEquals 恒定时间比对 / 单次消费 CAS / 重发作废旧码（ExpireAt 置 now）/ FailedAttempts 连续失败作废 / 频控键独立前缀 `smscode:`（**严禁复用 AuthCenter `sms-ip:` 桶**）/ 门禁内联 Q6 + m7 守卫（fail-closed）/ 表 `TKWF_SmsCode` 只增语义

---

## 一、定位

短信验证码引擎——**手机号持有权验证**原语（发送/校验/频控/落库/场景注册表/单次消费/失败作废）。**不含登录编排**（归消费方：DMP `MemberSmsLoginService` 形态）；MFA/AuthCenter 引擎迁移由 X' 双轨触发器驱动。本模块是"未来统一验证核心的胚胎"——场景注册表驱动、零 DMP 逻辑。

| 能力 | 说明 |
|------|------|
| 门面 | `ISmsCodeService`（SendCodeAsync 含门禁 / VerifyCodeAsync）——`AddConstructibleService` 注册，`User.Use` 帧内解析 |
| 引擎 | `VerificationCodeEngine`（internal 原语——CSPRNG 码生成 / SHA256 散列 / FixedTimeEquals 比对 / 滑动窗口频控 / 单次消费 CAS） |
| 数据 | `SmsCodeRecordEntity` + `SmsCodeRecordEntityDataService`（SG1 生成，ADR61 零手动注册；只增语义） |
| 场景 | `SmsScenes` 常量 + 场景注册表（v1 闭集 `login`；扩场景=加条目不加引擎） |
| SPI | `ISmsSender`（发送渠道）/ `ISmsCodeScopeResolver`（租户维度归消费方） |

## 二、安装与接线

### 1. 消费方引用 + 白名单启用（V4.9.85 必需）

```xml
<ProjectReference Include="..\..\_Framework\SmsCode\TKWF.Ext.SmsCode.csproj" />
<!-- 门禁场景（login 默认 RequiresCaptcha=true）须同时引用并白名单 Captcha（主包 + 契约包） -->
<ProjectReference Include="..\..\_Framework\Captcha.Abstractions\TKWF.Ext.Captcha.Abstractions.csproj" />
<ProjectReference Include="..\..\_Framework\Captcha\TKWF.Ext.Captcha.csproj" />
```

```csharp
using TKWF.Ext.SmsCode;

// ⚠️ 场景要求门禁（login 默认 RequiresCaptcha=true）——同时声明 Captcha；未装配 → 守卫异常 fail-closed
[TKWFEnabledExtension(typeof(SmsCodeExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(CaptchaExtensionInitializer<>))]   // 门禁依赖
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

DI 注册形态：

| 服务 | 注册 | 说明 |
|------|------|------|
| `ISmsCodeService` 门面 | `AddConstructibleService` | 消费方 `User.Use<ISmsCodeService>()` 帧内解析 |
| `ISmsSender` | **消费方实现**（TryAdd 语义） | 发送渠道（腾讯云/阿里云等）；**生产未装配 → 503 fail**；dev 模式日志输出码 |
| `ISmsCodeScopeResolver` | 默认空串单例；消费方 TryAdd **Scoped** 覆盖 | 租户维度归消费方（DMP: `merchant:{MerchantUId}`，读路由值 `mid`）；返回 `null` = 租户缺失（ScopeMissing） |
| `IRateLimitCheck` | `TryAddSingleton<MemoryRateLimitCheck>` fallback | 点检查频控（verify 调用计数 + IP 桶）——消费方可替换 DB 版跨实例实现 |

### 2. 使用（User.Use 帧内）

```csharp
// 匿名面（登录等）经游客帧 guest.Use<ISmsCodeService>()
var result = await guest.Use<ISmsCodeService>()
    .SendCodeAsync(SmsScenes.Login, mobile, mobile, chaptchaToken, ct);   // 门禁场景须携带 captchaToken
var verified = await guest.Use<ISmsCodeService>()
    .VerifyCodeAsync(SmsScenes.Login, mobile, code, ct);
// captchaToken 格式冻结："{challengeId}:{answer}"（challengeId=32hex、answer 无歧义字母表均不含 ':'，分隔安全）
```

### 3. 配置（`TKWF:SmsCode` 节——领域配置，Web 层不可关闭）

| 键 | 默认 | 说明 |
|----|------|------|
| `SmsResendIntervalSeconds` | 60 | 同 subject+scene 重发间隔 |
| `SmsDailyLimitPerSubject` | 10 | 每主体日上限（oracle2 冻结，比 AuthCenter 20 收紧） |
| `SmsHourlyLimitPerSubject` | 5 | 每主体小时上限 |
| `SmsHourlyLimitPerIp` | 20 | IP 桶（send+verify 共享，IRateLimitCheck 点检查） |
| `MaxActiveCodesPerSubject` | 5 | 活跃未消费码上限（防轰炸） |
| `VerifyAttemptsPerWindow` | 5 | 校验尝试上限（15min 窗，verify 调用计数） |
| `FailedVerifyInvalidateThreshold` | 5 | 同码连续失败作废阈值 |
| `CodeTtlMinutes` | 5 | 验证码 TTL |
| `CodeLength` | 6 | 验证码位数 |
| `IsProduction` | false | 生产缺 ISmsSender → 拒绝发送（503）；dev-mock 明文日志仅 `IsProduction=false` 时输出 |
| `Scenes:login:RequiresCaptcha` | **true** | 场景门禁开关（关闭门禁仅允许场景注册表不要求的场景——每场景显式 false） |

## 三、核心组件清单

| **组件** | **职责** | **实现** |
|----------|---------|---------|
| `ISmsCodeService : IDomainService` | 门面——门禁内联（Q6）/频控编排/生成/落库/发送 | `SmsCodeService`（internal sealed，本扩展，继承 DomainServiceBase，AddConstructibleService） |
| `VerificationCodeEngine` | 引擎原语——CSPRNG 码生成（1-9 位显式校验）/SHA256 hex/滑动窗口（GetOldestInWindowAsync 精确 retryAfter） | internal sealed（本扩展） |
| `SmsCodeRecordEntity` + `SmsCodeRecordEntityDataService` | 数据底座（表 `TKWF_SmsCode`，只增语义 + FailedAttempts CAS 列） | SG1 生成（xCodeGen 产物 `.g.cs` 随源码入库） |
| `ISmsSender` | 发送渠道 SPI（send-only） | 消费方实现（生产必装） |
| `ISmsCodeScopeResolver` | 租户前缀 SPI（默认空串；DMP `merchant:{Mid}`） | 消费方实现（Scoped） |
| `SmsCodeOptions [Options("TKWF:SmsCode")]` | 配置（§二.3）+ 场景注册表 | — |
| `SmsCodeExtensionInitializer<TUserInfo>` | `[TKWFExtension]` 三钩子装配 | 本扩展 |

## 四、安全边界与局限

- **验证码不落明文**：SHA256 小写 hex 落库（明文不落库不落日志）；比对 `CryptographicOperations.FixedTimeEquals`。
- **CSPRNG**：`RandomNumberGenerator` 生成（**禁 System.Random**）；CodeLength 1-9 位显式校验（溢出防御）。
- **单次消费**：CAS `WHERE Id=? AND IsVerified=false`——重放拒绝；**重发作废旧码**（send 通过频控后本 (Subject,Scene) 未消费未过期记录 ExpireAt 置 now——非 verify-latest-only）；**FailedAttempts 作废**（同码连续失败 ≥ 阈值 → ExpireAt 置 now）。
- **频控键独立前缀**：`smscode:{scope}:{scene}:{subject}` / `smscode:verify:{scope}:{scene}:{subject}` / `smscode:ip:{scope}:{ip}`（send+verify 单桶共享）——**严禁复用 AuthCenter `sms-ip:` 桶**（X' 冻结项）。
- **防枚举**：已注册/未注册号统一响应（不区分）；login 未注册 → `invalid_credentials` 同文案；手机号归一化 `^1\d{10}$`。
- **门禁 fail-closed（Q6 + m7）**：场景要求门禁但 Captcha 未装配 / 未传或无效 captchaToken → 拒绝（守卫异常或 CaptchaFailed）；`Captcha.Enabled=false` + 场景要求门禁 = 配置冲突守卫异常（禁静默跳过）。
- **凭据不落日志**：验证码/CHAPTCHA 凭据生产不落日志；dev-mock 明文日志仅 `IsProduction=false` 时输出。
- **只增语义**：`TKWF_SmsCode` 记录追加写（无公开 Update/Delete——对齐 AuthCenter SmsRecord 先例）；跨商户单手机上限不内置（租户 COUNT 无法跨商户——DMP 消费侧裁定）。

## 五、版本与发布

- MinVerTagPrefix：`SmsCode/v`（`TKWF.Ext.SmsCode`）——独立演进，与 Captcha/契约包版本互不影响。
- **V0.1.0（2026-11-16，M1-M4 完成）**：M1（骨架/实体/引擎/门面/Options/Initializer/DataService + xCodeGen 生成物入库）→ M2（Captcha 配套）+ M3（门禁内联接线 Q6/m7——SmsCode→Captcha.Abstractions 可空依赖）→ M4（测试全矩阵 **49 用例全绿**：引擎 16 + DataService 10 + 门面 17 + 门禁 6）；Captcha 配套 29 + SmsCode 49 = 双模块 **78 用例全绿**；迭代代码审核 5 项闭环（P0 ExecuteAtomicAsync 竞态 / P1 verify 面 IP 桶共享 / P2 GenerateCode 溢出防御 / P2 retryAfter 精确化 / CaptchaService ctor 观察）+ 二遍复审 PASS。
- 使用指南见 `docs/SmsCode/SMS验证码扩展-使用指南.md`；DMP 侧接入见 `docs/SmsCode/DMP接入说明.md`。

<!-- EOF -->