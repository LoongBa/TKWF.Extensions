# TKWF.Ext.Captcha 人机验证扩展技术规范

**状态**: 人机验证扩展（纯内存型——零实体零持久化） | **版本**: V0.1.0（M2 里程碑，未发布） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain（IDomainService / DomainServiceBase / [Options]）+ `TKWF.Ext.Captcha.Abstractions`（契约包，L2）+ Microsoft.Extensions.Caching.Memory（挑战态）+ FrameworkReference Microsoft.AspNetCore.App（IHttpContextAccessor——取题 IP 频控）

**核心约束**: 零第三方依赖图形渲染（SVG 字符串）/ 答案 SHA256 散列永不进客户端载荷 / in-memory 挑战态 per-challengeId 自锁（CAS 非原子防护）/ 恒 fail-closed（全局关 + 场景门禁 = 配置冲突守卫异常）/ 扩展零端点（wire 契约归消费方 DMP 端点层）

---

## 一、定位

人机验证（CAPTCHA）模块——`ICaptchaProvider` SPI + 本地图形 Provider（v1）+ 挑战门面（Issue/Verify）+ 挑战态存储（v1 in-memory）。为短信验证码发送（SmsCode 门禁内联）、登录/注册匿名面等提供人机验证能力。**场景无关**："哪些场景要门禁"归消费方场景注册表；评分/音频/滑块/第三方 Provider 适配（极验/腾讯）属未来增量。

| 能力 | 说明 |
|------|------|
| SPI | `ICaptchaProvider`（Type/IssueAsync/VerifyAsync——锁形状不锁媒介，第三方 Provider 纯增量实现） |
| 本地图形 | `ImageCaptchaProvider`（v1 唯一内置——随机 4-6 位无歧义码 → SHA256 散列落挑战态 → SVG 载荷零第三方依赖） |
| 门面 | `ICaptchaService`（Provider 选中 + Issue IP 频控 + 全局开关 fail-closed + 委托 Provider） |
| 挑战态 | `CaptchaChallengeStateStore`（v1 in-memory IMemoryCache——TTL 5min/答错上限 3/成功即消费/per-challengeId SemaphoreSlim 自锁） |

## 二、安装与接线

### 1. 消费方引用 + 白名单启用（V4.9.85 必需）

```xml
<ProjectReference Include="..\..\_Framework\Captcha\TKWF.Ext.Captcha.csproj" />
<!-- 使用 SPI/门面契约的消费方须同时引用独立契约包（与主包同命名空间，零 using 迁移） -->
<ProjectReference Include="..\..\_Framework\Captcha.Abstractions\TKWF.Ext.Captcha.Abstractions.csproj" />
```

```csharp
using TKWF.Ext.Captcha;

[TKWFEnabledExtension(typeof(CaptchaExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
// 自动注册：门面 AddConstructibleService<ICaptchaService, CaptchaService>（User.Use 帧内解析）
//   + Provider 集合 TryAddEnumerableConstructible<ICaptchaProvider, ImageCaptchaProvider>（ADR92）
//   + Options（TKWF:Captcha 节）/ IRateLimitCheck·IMemoryCache fallback（TryAdd 消费方可覆盖）
```

### 2. 使用（User.Use 帧内）

```csharp
// 匿名面（登录/注册等）经游客帧 guest.Use<ICaptchaService>()
var challenge = await user.Use<ICaptchaService>().IssueAsync("login");   // → {challengeId, payload(SVG), ttlSeconds}
var result = await user.Use<ICaptchaService>().VerifyAsync(challenge.ChallengeId, userInput);  // → {passed}
// 前端展示：<img src="data:image/svg+xml;base64,..." />（payload base64 后直出）
```

### 3. 配置选项（`TKWF:Captcha` 节）

```jsonc
{ "Captcha": {
    "Enabled": true,                 // fail-closed：false 仅 dev——场景要求门禁时门面恒抛守卫异常（配置冲突拒绝）
    "EnabledProviderType": "image",  // Provider 选中（对 ICaptchaProvider.Type 精确匹配，未匹配守卫异常）
    "ChallengeTtlSeconds": 300,      // 挑战有效期（秒，默认 5min）
    "AttemptsPerChallenge": 3,       // 单挑战答错上限（耗尽作废）
    "IssuePerMinutePerIp": 10        // 取题频控（每 IP 每分钟——防挑战表洪泛）
}}
```

## 三、核心组件清单

| **组件** | **职责** | **实现** |
|----------|---------|---------|
| `ICaptchaProvider : IDomainService`（Abstractions） | Provider SPI——`Type` / `IssueAsync` / `VerifyAsync`（失败统一 false 不区分判因） | 第三方 Provider 纯增量（TryAddEnumerableConstructible 追加） |
| `ImageCaptchaProvider` | 本地图形 Provider（v1 唯一内置，Type="image"）——随机码/散列/挑战态/绘制 SVG | internal sealed（本扩展，继承 DomainServiceBase） |
| `CaptchaChallengeStateStore` | 挑战态 in-memory 存储（IMemoryCache + per-challengeId SemaphoreSlim 自锁） | internal sealed 单例（本扩展） |
| `ICaptchaService : IDomainService`（Abstractions） | 门面——Provider 选中 + Issue IP 频控（`captcha:issue:{ip}` 10/min）+ 全局开关 fail-closed | `CaptchaService`（internal sealed，本扩展，AddConstructibleService） |
| `CaptchaOptions [Options("TKWF:Captcha")]` | 配置（§二.3） | — |

## 四、安全边界与局限

- **恒 fail-closed**：`Enabled=false` + 消费方场景要求门禁 = 配置冲突 → 门面恒抛守卫异常（禁静默放行——防误关导致短信预算裸奔）；Provider 未注册/Type 未匹配 → 守卫异常。
- **答案永不进载荷**：客户端只见 SVG Payload + challengeId；答案 SHA256 散列存挑战态；比对 `CryptographicOperations.FixedTimeEquals`；消费即删（重放/重复提交 → 状态不存在 → false）。
- **取题频控**：`captcha:issue:{ip}`（IP 空白 → `unknown` 桶）——防挑战表洪泛。
- **图形码弱 OCR 容忍**：SVG 基础干扰（旋转字符 + 干扰线）非强对抗——高安全场景用评分型第三方 Provider（SPI 预留 Score）。
- **in-memory 局限**（v1，多实例部署注意）：挑战只在签发实例可验（各实例独立缓存 + 独立锁表）；单实例内并发安全（per-challengeId 自锁）；DB/分布式挑战态留 v1.1。第三方无状态 Provider（极验等）可水平扩展。
- **异常链**：Provider 异常原样向上传播（noexcept 不 catch 不降级——失败即拒绝）。

## 五、版本与发布

- MinVerTagPrefix：`Captcha/v`（`TKWF.Ext.Captcha`）；契约包 `Captcha.Abstractions/v`（`TKWF.Ext.Captcha.Abstractions`）——独立演进。
- **M2 里程碑**（2026-11-15）：SPI + 本地图形 Provider + 挑战态存储 + 门面 + Options + Initializer + 契约包（零实体零 SG1——无 xCodeGen 配置）；M4 测试、M3 SmsCode 门禁内联接线待排期。

<!-- EOF -->