# SMS 验证码扩展（TKWF.Ext.SmsCode）使用指南

> **版本**：v0.1.0（M3+M4：门禁内联 + 测试全矩阵 78 全绿）
> **关联**：[开发方案](./v0.1.0-SmsCode-Captcha-开发方案.md)（契约冻结与设计裁定）、[设计梳理](./验证能力谱系与模块架构-设计梳理.md)（谱系基座）、DMP-Lite `v0.10.8` 需求转达（契约需求源）
> **配套**：人机验证扩展 `TKWF.Ext.Captcha`（+ `.Captcha.Abstractions`）——SmsCode 场景门禁区经契约包可空依赖

---

## 一、定位

短信验证码引擎——**手机号持有权验证**原语（发送/校验/频控/落库/场景注册表/单次消费/失败作废）。

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.SmsCode` |
| 版本 | v0.1.0（MinVerTagPrefix `SmsCode/v`） |
| 依赖 | `TKWF.Domain`（CPM）+ FreeSql + `Microsoft.Extensions.Caching.Memory` + **`TKWF.Ext.Captcha.Abstractions`**（门禁契约，可空依赖）+ `TKWF.CodeGeneration`（SG1 纯 Analyzer） |
| 数据 | 表 `TKWF_SmsCode`（只增语义——频控数据源 + 作废机制落点） |
| 边界 | **不含登录编排**（归消费方：DMP `MemberSmsLoginService` 形态）；MFA/AuthCenter 引擎迁移由 X' 双轨触发器驱动（见设计梳理 §4.3.1） |

> **X' 收敛裁定**：本模块是"未来统一验证核心的胚胎"——`Subject` 不透明键控（v1 恒=手机号，列名不叫 Phone）、场景注册表驱动、零 DMP 逻辑、错误契约分层（引擎层类型化结果，错误码映射住消费方端点层）。

---

## 二、启用方式（v4.9.85+ 白名单）

```csharp
using TKWF.Ext.SmsCode;

// ⚠️ 若消费方场景要求门禁（login 默认 RequiresCaptcha=true）——同时白名单声明 Captcha 扩展
//（Captcha 未装配 + 场景要求门禁 → 守卫异常 fail-closed，禁静默跳过）
[TKWFEnabledExtension(typeof(SmsCodeExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(CaptchaExtensionInitializer<>))]   // 门禁依赖
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

三钩子自动接线。DI 注册形态：

| 服务 | 注册 | 说明 |
|------|------|------|
| `ISmsCodeService` 门面 | `AddConstructibleService` | 接口守卫工厂 + 实现 throw-factory；消费方 `User.Use<ISmsCodeService>()` 帧内解析 |
| `ISmsSender` | **消费方实现**（TryAdd 语义） | 发送渠道（腾讯云/阿里云等）；**生产未装配 → 503 fail**；dev 模式日志输出码（dev mock） |
| `ISmsCodeScopeResolver` | 默认空串单例；消费方 TryAdd **Scoped** 覆盖 | 租户维度归消费方（DMP: `merchant:{MerchantUId}`，读路由值 `mid`）；返回 `null` = 租户缺失（ScopeMissing） |
| `IRateLimitCheck` | `TryAddSingleton<MemoryRateLimitCheck>` fallback | 点检查频控（verify 调用计数 + IP 桶）——消费方可替换 DB 版跨实例实现 |
| 实体 DataService | SG1 消费方聚合自动注册 | `SmsCodeRecordEntityDataService`（ADR61 零手动注册） |

---

## 三、消费方式

### 发送验证码（匿名端点——游客帧）

```csharp
// DMP 端点层（手写 MapPost，NoAop 直建——对齐 MemberLoginEndpoints 先例）
var svc = guest.Use<ISmsCodeService>();
var result = await svc.SendCodeAsync(SmsScenes.Login, mobile, mobile, chaptchaToken, ct);
```

- `captchaToken` 格式冻结：**`"{challengeId}:{answer}"`**（challengeId=32 hex、answer=去混淆字母表，均不含 `:`，分隔安全）
- 门禁序（Q6）：CAPTCHA 校验 → 频控检查 → 生成码 → 落库 → 发送——门禁通过才消耗短信预算
- **防枚举**：已注册/未注册号统一响应（不区分）；手机号归一化 `^1\d{10}$`（send/login 同一口径）

### 校验验证码

```csharp
var result = await svc.VerifyCodeAsync(SmsScenes.Login, mobile, code, ct);
```

- **单次消费 CAS**（IsVerified=true——重放拒绝）；**恒定时间比对**（FixedTimeEquals + SHA256 小写 hex）
- **失败作废**：同码连续失败 `FailedVerifyInvalidateThreshold`（默认 5）次 → 该码 ExpireAt 置 now 作废
- **校验频控**：15min 窗 verify 调用计数（键 `smscode:verify:{scope}:{scene}:{subject}`）——计数调用而非活跃记录（修正 AuthCenter 缺陷落点）
- **重发作废旧码**：send 通过频控后，本 (Subject,Scene) 全部未消费未过期记录 ExpireAt 置 now（旧码即时失效，防活跃计数膨胀）

### 类型化结果（错误契约分层——X' 冻结项 2）

| 结果 | Failure | DMP 映射 |
|------|---------|---------|
| `SmsCodeSendResult` | `CaptchaFailed` | 400 `chaptcha_failed` |
| | `RateLimited`（带 RetryAfterSeconds） | 429 `rate_limited` |
| | `SenderUnavailable` | 503 `sms_sender_unavailable` |
| | `UnknownScene` | 400 `invalid_scene` |
| | `ScopeMissing` | 400 `invalid_merchant` |
| `SmsCodeVerifyResult` | `NotFound`/`Mismatch`/`Expired` | 400 `invalid_code`/`code_expired` |
| | `RateLimited` | 429 `rate_limited` |
| | `UnknownScene`/`ScopeMissing` | 400 `invalid_scene`/`invalid_merchant` |

> ⚠️ **错误码 delta（DMP 前端）**：`sms_rate_limited` → **`rate_limited`**；send-code 频控 **400 → 429**（既有通道同表）。

---

## 四、端点契约（DMP 消费侧挂接——扩展零端点）

> 扩展提供门面能力；端点由消费方（DMP Merchant.WebApi）手写 MapPost 挂接（对齐 MemberLoginEndpoints 先例）。本契约 = oracle2 冻结版。

```
POST /m/{mid}/api/auth/sms/send-code   Body: {mobile, chaptcha}
  → 200 {sent:true, retryAfterSeconds:60}
  → 400 chaptcha_failed|invalid_mobile        （400 仅参数校验/门禁失败）
  → 429 rate_limited (+retryAfterSeconds)     （频控统一 429）
  → 503 sms_sender_unavailable
POST /m/{mid}/api/auth/sms/login       Body: {mobile, code}
  → 200 IdentitySession（assurance="sms"/channelType="sms"/channelId=null/nextAction=open_vip，与 DMP 通道同构零适配）
  → 400 invalid_code|code_expired|invalid_merchant|invalid_credentials（未注册统一文案）
  → 403 member_disabled  → 429 rate_limited  → 500 session_create_failed|internal_error
POST /m/{mid}/api/auth/sms/chaptcha     Body: {}
  → 200 {challengeId, payload, ttlSeconds}     （CHAPTCHA 取题端点——经 Captcha 门面 IssueAsync）
```

- **防枚举**：send-code 对已注册/未注册号统一响应（不区分）；login 未注册 → `invalid_credentials` 同文案
- **IdentitySession 组装**：结构同 ticket-login（`assurance="sms"` / `channelType="sms"` / `channelId=null` / `nextAction=open_vip` / `capabilities` 固定集 / `membership` = {status, memberUId, level}）——前端按字段位置消费，不增删改键名
- 安全边界：匿名端点游客帧（`guest.Use<门面>()` + UseWebSession 同装配）；验证码/CHAPTCHA 凭据不落日志

---

## 五、配置（`TKWF:SmsCode` 节——领域配置，Web 层不可关闭）

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
| `IsProduction` | false | 生产缺 ISmsSender → 拒绝发送（503） |
| `Scenes:login:RequiresCaptcha` | **true** | 场景门禁开关（M3 接线完成态；关闭门禁仅允许场景注册表不要求的场景——每场景显式 false） |

> ⚠️ **门禁 fail-closed（m7）**：场景要求门禁但 Captcha 未装配 / `Captcha.Enabled=false` → **恒守卫异常**（禁静默跳过——防运维误关导致短信预算裸奔）。

---

## 六、CHAPTCHA 人机验证（配套 `TKWF.Ext.Captcha`）

- 类型拼写约定（M4）：C# 类型/命名空间/配置键一律 `Captcha`；**wire 层保留 `chaptcha`**（端点路径/错误码/body 字段——DMP 前端已按转达预留）
- 配置（`TKWF:Captcha` 节）：`Enabled`（默认 true，fail-closed）/ `EnabledProviderType`（默认 `image`）/ `ChallengeTtlSeconds` 300 / `AttemptsPerChallenge` 3 / `IssuePerMinutePerIp` 10
- v1 内置 `ImageCaptchaProvider`（SVG 零第三方依赖——System.Drawing 不可跨平台）；第三方 Provider（极验/腾讯云）经 `ICaptchaProvider` SPI 纯增量接入
- 挑战态 v1 in-memory（单实例局限见风险 §七；第三方 Provider 无状态可水平扩展）

---

## 七、约束与风险

- **只增语义**：`TKWF_SmsCode` 记录追加写（无公开 Update/Delete——对齐 AuthCenter SmsRecord 先例）；`FailedAttempts` 为可更新列（CAS 自增）
- **安全**：验证码 SHA256 hex 小写落库（明文不落库不落日志）；CSPRNG 生成（禁 System.Random）；FixedTimeEquals 恒定时间比对；单次消费 CAS 防重放
- **挑战态 in-memory 局限**：Captcha 挑战态存储 v1 内存（单实例）；多实例部署须第三方 Provider（无状态）或升级 DB 化（v1.1）
- **跨商户单手机上限**：租户库 COUNT 无法跨商户限额——**DMP 消费侧裁定**（接受按商户语义 or 全局桶），扩展记录不内置
- **表名**：`TKWF_SmsCode`（ADR100 `TKWF_` 前缀；索引 `TKWFIX_SmsCode_SubjectSceneTime`/`TKWFIX_SmsCode_IpTime`）

---

## 八、测试基线

`Extension.SmsCode.Tests` —— **49 用例全绿**（引擎原语 16 + DataService 原子方法 10 + 门面全矩阵 17 + **门禁集成 6**）。Tier 1.5 隔离契约（每 Fact 独立 SQLite 文件库）。门禁测试经 `Captcha.Abstractions` 契约面 fake 装配（不引 Captcha 主包——聚焦门禁契约语义）。Captcha 配套 29 用例（门面 16 + Initializer 13）——双模块合计 **78 用例全绿**。

---

## 变更记录

| 日期 | 版本 | 变更 |
|------|------|------|
| 2026-11-15 | v0.1.0 | 首发：SmsCode 门面/引擎/实体/频控/场景注册表 + M3 门禁内联接线（Q6/m7）+ 49 测试全绿（M4 全矩阵刷新） |