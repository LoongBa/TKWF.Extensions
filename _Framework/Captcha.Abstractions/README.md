# TKWF.Ext.Captcha.Abstractions 人机验证契约包技术规范

**状态**: 契约抽象包（零实体、零 SG1、零 Initializer——对齐 Federation.Abstractions 先例） | **版本**: V0.1.0（2026-11-16） | **框架**: .NET 10 | **依赖**: `TKWF.Domain`（IDomainService）

---

## 一、定位

人机验证（CAPTCHA）的**跨扩展消费契约**（L2 门控：扩展间只引 Abstractions，不引实现主包——`TKWF0022`）：

| 类型 | 说明 |
|------|------|
| `ICaptchaService : IDomainService` | 门面契约——`IssueAsync(scene, ct)` 签发挑战 / `VerifyAsync(challengeId, answer, ct)` 校验作答（失败统一 `Passed=false`，不区分判因）；Provider 选中、IP 频控、全局开关裁决归实现层 |
| `ICaptchaProvider` | 验证实现 Provider 契约（扩展点——短信/图形/评分型 Provider 各自实现） |
| `CaptchaChallenge` / `CaptchaVerifyResult` | 不可变结果模型（挑战描述 + 校验结果） |

namespace：`TKWF.Ext.Captcha`（跨程序集同 namespace——契约包与主包共享，消费方零 using 迁移）。

## 二、消费方式

```xml
<PackageReference Include="TKWF.Ext.Captcha.Abstractions" Version="0.1.0" />
```

```csharp
// 已认证面：域作用域内解析
var result = await user.Use<ICaptchaService>().VerifyAsync(challengeId, answer, ct);

// 匿名面（登录/注册）：游客帧解析
var challenge = await guest.Use<ICaptchaService>().IssueAsync("login", ct);
```

> 实现（`Captcha` 主包）经 `AddConstructibleService` 注册；本契约包不注册任何服务。

## 三、版本与发布

- MinVerTagPrefix：`Captcha.Abstractions/v`（独立版本线）
- 首发 V0.1.0（2026-11-16，与 `Captcha/v0.1.0`、`SmsCode/v0.1.0` 同批 tag）
- 契约变更（接口签名/DTO 形状）须走 ADR + 消费方适配通知（SmsCode 为直接消费方）

<!-- EOF -->
