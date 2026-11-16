# DMP 侧接入说明 —— SmsCode + Captcha 扩展

> **关联**：DMP-Lite `v0.10.8-手机号验证码登录与CHAPTCHA扩展模块-需求转达.md`（契约需求源）、SmsCode 使用指南（[「SMS 验证码扩展（TKWF.Ext.SmsCode）使用指南」](../SmsCode/SMS验证码扩展-使用指南.md)）
> **状态**：扩展侧 M1-M4 完成（门禁内联接线 + 测试全矩阵 78 全绿——SmsCode 49 + Captcha 29；迭代审核 5 项闭环），待 DMP 侧接入

---

## 一、WebApi 装配路径

### 1. 引用与白名单（Merchant.Domain 领域初始化器）

```xml
<!-- Merchant.Domain.csproj -->
<ProjectReference Include="..\..\_TKWF.Extensions\_Framework\SmsCode\TKWF.Ext.SmsCode.csproj" />
<ProjectReference Include="..\..\_TKWF.Extensions\_Framework\Captcha.Abstractions\TKWF.Ext.Captcha.Abstractions.csproj" />
<ProjectReference Include="..\..\_TKWF.Extensions\_Framework\Captcha\TKWF.Ext.Captcha.csproj" />
```

```csharp
using TKWF.Ext.SmsCode;
using TKWF.Ext.Captcha;

[TKWFEnabledExtension(typeof(SmsCodeExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(CaptchaExtensionInitializer<>))]   // 门禁依赖——login 场景默认 RequiresCaptcha=true
public class MerchantDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // 门面（AddConstructibleService）：
    //   ISmsCodeService（SmsCode）——发送/校验；ICaptchaService（Captcha）——取题（sms/chaptcha 端点）
}
```

### 2. 领域侧实现（消费方 SPI）

| SPI | DMP 实现 | 说明 |
|-----|---------|------|
| `ISmsSender` | 腾讯云/阿里云 SDK 封装（send-only） | **生产必须装配**——未装配 → 503 `sms_sender_unavailable`；开发可跳过（dev mock 日志输出码） |
| `ISmsCodeScopeResolver` | `merchant:{MerchantUId}`（Scoped） | 经 `IHttpContextAccessor` 读路由值 `mid` → 商户 UId；mid 缺失返回 null → `invalid_merchant` |

```csharp
// ISmsCodeScopeResolver——Scoped（每请求读取路由值，m4 闭环）
public sealed class MerchantSmsCodeScopeResolver : ISmsCodeScopeResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public MerchantSmsCodeScopeResolver(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string? GetScopePrefix()
    {
        var mid = _httpContextAccessor.HttpContext?.Request.RouteValues["mid"]?.ToString();
        return string.IsNullOrWhiteSpace(mid) ? null : $"merchant:{mid}";
    }
}
// 注册（Scoped——非单例；扩展默认空串单例被 TryAdd 语义覆盖）
services.AddScoped<ISmsCodeScopeResolver, MerchantSmsCodeScopeResolver>();
```

### 3. 端点挂接（Merchant.WebApi 手写 MapPost）

> 扩展零端点（对齐 MemberLoginEndpoints 先例——匿名端点不走生成暴露面）。**必须游客帧**（OBSERVATION ②闭环）：`UseWebSession` 同装配 + `guest.Use<门面>()`。

```csharp
// MemberSmsLoginEndpoints.cs（对齐 MemberLoginEndpoints 形态）
app.MapPost("/m/{mid}/api/auth/sms/send-code", async (string mid, SendCodeRequest body, ...) =>
{
    var guest = /* UseWebSession 游客 DomainUser */;
    var svc = guest.Use<ISmsCodeService>();
    var result = await svc.SendCodeAsync(SmsScenes.Login, body.Mobile, body.Mobile, body.Chaptcha, ct);

    return result switch
    {
        { Sent: true } => Results.Ok(new { sent = true, retryAfterSeconds = 60 }),
        { Failure: SmsCodeSendFailure.CaptchaFailed } => Results.BadRequest(new { error = "chaptcha_failed" }),
        { Failure: SmsCodeSendFailure.RateLimited, RetryAfterSeconds: var retry } => Results.Json(
            new { error = "rate_limited", retryAfterSeconds = retry }, statusCode: 429),
        { Failure: SmsCodeSendFailure.SenderUnavailable } => Results.Json(new { error = "sms_sender_unavailable" }, statusCode: 503),
        { Failure: SmsCodeSendFailure.ScopeMissing } => Results.BadRequest(new { error = "invalid_merchant" }),
        _ => Results.BadRequest(new { error = "internal_error" })
    };
});

app.MapPost("/m/{mid}/api/auth/sms/login", async (string mid, LoginRequest body, ...) =>
{
    // MemberSmsLoginService（登录适配）：校验验证码 → 命中 MemberInfo（未删除+Normal）→ VerifyCode
    // → LoginAsUserAsync 建会话（BR-36/BR-43）→ IdentitySession（assurance="sms"/channelType="sms"/channelId=null/nextAction=open_vip）
    // 状态语义对齐 MemberLoginService：Disabled/Frozen → 403 member_disabled；未注册 → 400 invalid_credentials（同文案防枚举）
});

app.MapPost("/m/{mid}/api/auth/sms/chaptcha", async (string mid, ...) =>
{
    var guest = /* UseWebSession 游客 DomainUser */;
    var captcha = guest.Use<ICaptchaService>();
    try
    {
        var challenge = await captcha.IssueAsync("login", ct);
        return Results.Ok(new { challengeId = challenge.ChallengeId, payload = challenge.Payload, ttlSeconds = challenge.TtlSeconds });
    }
    catch (RateLimitedException)   // using TKW.Framework.Domain.Exceptions——取题频控命中（IssuePerMinutePerIp）
    {
        return Results.Json(new { error = "rate_limited" }, statusCode: 429);
    }
});
```

> **chaptcha 端点契约补充（P1-3）**：`POST /m/{mid}/api/auth/sms/chaptcha` → **`429 rate_limited`（取题频控命中——`IssuePerMinutePerIp`）**——`IssueAsync` 频控命中抛 `RateLimitedException`，端点须 catch 映射 429（见上样例）。

---

## 二、UserH5 前端切换（退 mock）

| 项 | 现状（mock） | 切换后（真实） |
|----|------------|--------------|
| 验证码 Tab | `sms-login-sim.ts` 模拟层（固定码 `888888`） | 真实端点：send-code → sms/chaptcha 取题 → 图形码作答 → send-code 携带 `chaptcha` 凭据 |
| CHAPTCHA | `.env.example` `VITE_CHAPTCHA=off | simulate` | `VITE_CHAPTCHA=real`（扩展交付后）——`simulate/off` 仅开发环境 |
| 错误码 | 按 DMP 既有表 | ⚠️ **delta**：`sms_rate_limited` → **`rate_limited`**；send-code 频控 **400 → 429**；新增 `chaptcha_failed`(400)/`sms_sender_unavailable`(503) |

### 前端流程（send-code 门禁序）

1. `POST /m/{mid}/api/auth/sms/chaptcha`（body: `{}`）→ `{challengeId, payload, ttlSeconds}`（payload = 图形码 SVG/图片）；**取题频控命中 → 429 rate_limited（前端提示稍后再试）**
2. 用户看图输入答案 → 调 `POST /m/{mid}/api/auth/sms/send-code` body: `{mobile, chaptcha: "{challengeId}:{answer}"}`
3. 门禁通过 + 频控允许 → 200 `{sent:true, retryAfterSeconds}`；失败 → `chaptcha_failed`(400)/`rate_limited`(429)
4. 用户输入短信码 → `POST /m/{mid}/api/auth/sms/login` body: `{mobile, code}` → 200 IdentitySession / 400/403/429/500 错误码

> ⚠️ **captchaToken 格式冻结**：`"{challengeId}:{answer}"`（challengeId 与 answer 均不含 `:`，分隔安全）——前端按此直接拼装。

### SDK 适配（@tkwf/tsclient v1.1.0——建议前端直接采用）

前端**无需手写 fetch/拼装凭据**——v1.1.0 已内置三方法（组串内化 + wire 键 `chaptcha` 单点保证，
urlMap 覆盖 DMP 路径，仅 REST 通道；GraphQL 下抛 `EXTERNAL_LOGIN_FAILED` fail-closed）：

```typescript
const guest = Tkwf.Guest;
// ① 取题 → ② 发码（内部拼 "{challengeId}:{answer}"）→ ③ 登录（泛型返回 IdentitySession，不持久化）
const chaptcha = await guest.issueChaptcha();                            // { challengeId, payload, ttlSeconds }
await guest.sendSmsCode(mobile, chaptcha.challengeId, userAnswer);        // 429 → err.retryAfterSeconds
const session = await guest.loginBySmsCode<IdentitySession>(mobile, code);
```

- urlMap 覆盖示例：`urlMap: { "auth/sms/chaptcha": "/m/{mid}/api/auth/sms/chaptcha", "auth/sms/send-code": "/m/{mid}/api/auth/sms/send-code", "auth/sms/login": "/m/{mid}/api/auth/sms/login" }`（`{mid}` 运行时替换）
- 错误码：门禁/频控/发送失败经 `body.error` 透传（`chaptcha_failed`/`rate_limited`/`sms_sender_unavailable`）+ `err.retryAfterSeconds` 驱动倒计时——前端错误码表同步 v1.1.0 语义

---

## 三、配置

```jsonc
// appsettings.json（DMP 侧按需覆盖；缺省用扩展默认值）
{
  "TKWF": {
    "SmsCode": {
      "IsProduction": true,                 // 生产必填——缺 ISmsSender 拒绝发送（fail-fast）
      "Scenes": { "login": { "RequiresCaptcha": true } }   // 默认已 true，可显式声明
    },
    "Captcha": {
      "Enabled": true,                      // fail-closed——false + 场景要求门禁 = 守卫异常
      "EnabledProviderType": "image"        // v1 内置图形码
    }
  }
}
```

---

## 四、验收要点（DMP 侧 §7 对齐）

- [ ] send-code：未传/错 chaptcha → 400 `chaptcha_failed`；频控 → 429 `rate_limited`；成功 → 200（dev 模式日志见码）
- [ ] **绕过前端直调 send-code（无门禁凭据）→ 必须被拒**（Q6/m7 fail-closed——门禁测试已锁定）
- [ ] 重发使旧码失效；同码连续 5 次失败作废；成功码不可重放
- [ ] login：正确码 → IdentitySession（assurance="sms"）；未注册 → 统一文案；Disabled/Frozen → 403
- [ ] 匿名端点游客帧 + 凭据不落日志

---

## 五、部署顺序（对齐 AuthCenter V0.14.0 redeem 交付链路先例 e853890）

1. 扩展侧：代码提交 + 用户同意打 tag（`SmsCode/v0.1.0` + `Captcha/v0.1.0` + `Captcha.Abstractions/v0.1.0`）+ 本地全量部署（`build-ext-deployment.ps1 -Cleanup`）→ 部署根 refs DLL 即时可见
2. DMP 侧：引用 + 白名单 + SPI 实现 + 端点挂接 → 重编译验证（部署根版本实证——先例教训 R6：回执"refs 已刷新"须伴随真实提交/tag/发布动作）
3. 前端：`VITE_CHAPTCHA=real` + 端点切换 + 错误码表更新
4. 验收（§四）→ 扩展侧 L1 定向回归 + 全量 L2（发布前）