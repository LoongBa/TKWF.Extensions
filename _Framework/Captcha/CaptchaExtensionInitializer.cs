using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 人机验证扩展初始化器（方案 §5.6 装配）——经 [TKWFExtension] 被 SG1 编译期发现，三钩子接线。
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——Options 兜底 + 基础设施 fallback + 门面/Provider 注册（见方法注释）；</item>
/// <item>ConfigureFilters——不调用（本扩展无全局过滤器）；</item>
/// <item>InitializeAsync——不调用（零实体零持久化，无种子/无预检——纯内存型扩展，Tagging/Dashboard 先例）。</item>
/// </list>
/// <para>本扩展为<b>纯内存型</b>（方案 §2 边界：挑战态 v1 in-memory；DB 化留 v1.1）——零实体零 SG1 生成
/// 零 DataService 注册；<see cref="ICaptchaProvider"/> 集合经 Adapter92 集合版守卫工厂注册
/// （<c>TryAddEnumerableConstructible</c>——实现继承 DomainServiceBase，帧内 CurrentAopUser 供给 IDomainUser）。</para>
/// </summary>
[TKWFExtension("Captcha")]
public class CaptchaExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Captcha";

    /// <summary>扩展描述。</summary>
    public override string Description => "人机验证扩展——ICaptchaProvider SPI + 本地图形 Provider（v1 SVG 零第三方依赖）+ 挑战门面（Issue/Verify fail-closed）+ 挑战态存储（v1 in-memory TTL/尝试上限/单次消费）+ 取题 IP 频控；扩展零端点（wire 契约归消费方）";

    /// <summary>
    /// 注册人机验证服务（方案 §5.6 装配——同 SmsCode §4.7 形态）。
    /// <list type="bullet">
    /// <item><b>Options</b>——<c>AddOptions&lt;CaptchaOptions&gt;()</c> 兜底默认值（SG1 [Options] 已在消费方自动绑定
    ///     <c>TKWF:Captcha</c> 节；守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析，对齐 Settings/Identity 先例）；</item>
    /// <item><b>频控 fallback</b>——<c>TryAddSingleton&lt;IRateLimitCheck, MemoryRateLimitCheck&gt;()</c>（框架 v4.10.67）：
    ///     取题 IP 频控"始终在"（fail-closed）；消费方显式注册 DB 版频控器时静默替换（TryAdd 语义，MFA V0.2.1 先例）；</item>
    /// <item><b>缓存 fallback</b>——<c>TryAddSingleton&lt;IMemoryCache, MemoryCache&gt;()</c>（挑战态存储底座；消费方显式
    ///     注册分布式缓存替代时静默替换——多实例共享挑战态，但 in-memory 自锁仅本实例内有效，多实例局限写入使用指南）；</item>
    /// <item><b>挑战态存储</b>——<c>TryAddSingleton&lt;CaptchaChallengeStateStore&gt;()</c>（internal 基础设施单例——
    ///     per-challengeId 自锁表跨门面/Provider 实例共享，in-memory CAS 防护前提，方案 §5.2）；</item>
    /// <item><b>门面</b>——<c>AddConstructibleService&lt;ICaptchaService, CaptchaService&gt;()</c>（接口可构造守卫工厂 +
    ///     实现类 throw-factory；消费方 <c>User.Use&lt;ICaptchaService&gt;()</c> 帧内解析，匿名面游客帧）；</item>
    /// <item><b>Provider 集合</b>——<c>TryAddEnumerableConstructible&lt;ICaptchaProvider, ImageCaptchaProvider&gt;()</c>
    ///     （多实现集合版守卫工厂，ADR92——v1 唯一内置实现；第三方 Provider 未来 TryAdd 追加）。</item>
    /// </list>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（门面 ctor 依赖 IOptions<CaptchaOptions>；SG1 [Options] 消费方自动绑定 TKWF:Captcha 节）
        services.AddOptions<CaptchaOptions>();

        // 频控 fallback（v4.10.67）：取题 IP 频控"始终在"（fail-closed）；消费方显式注册 DB 跨实例版时静默替换
        services.TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>();

        // 缓存 fallback：挑战态存储（IMemoryCache）底座——消费方可覆盖（多实例共享挑战态，自锁仅实例内）
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // 挑战态存储（internal 基础设施单例——per-challengeId SemaphoreSlim 自锁表实例级共享）
        services.TryAddSingleton<CaptchaChallengeStateStore>();

        // 门面（ADR90 正确路线——接口可构造守卫工厂 + 实现类 throw-factory；消费方 User.Use<ICaptchaService>() 解析）
        services.AddConstructibleService<ICaptchaService, CaptchaService>();

        // Provider 多实现集合（v4.10.55/ADR92——集合版守卫工厂；帧内枚举，禁帧外 [FromServices] 集合预绑定）
        services.TryAddEnumerableConstructible<ICaptchaProvider, ImageCaptchaProvider>();
    }
}