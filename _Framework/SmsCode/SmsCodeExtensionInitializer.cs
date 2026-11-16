using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.SmsCode;

/// <summary>
/// 短信验证码扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（方案 §4.7）：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——正确路线（ADR90）注册：门面 <see cref="ISmsCodeService"/> →
///     <c>AddConstructibleService</c>（接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 解析）；
///     SPI 默认（ScopeResolver 空串单例）/ Options 兜底；零 DataService 手动注册（SG1 消费方聚合自动注册）。</item>
/// <item>ConfigureFilters——空（非过滤器扩展）。</item>
/// <item>InitializeAsync——空（无启动预载；验证码无种子数据）。</item>
/// </list>
/// </summary>
[TKWFExtension("SmsCode")]
public class SmsCodeExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "SmsCode";

    /// <summary>扩展描述。</summary>
    public override string Description => "短信验证码引擎——发送/校验/频控/落库/场景注册表/重发作废/单次消费（手机号持有权验证原语，X' 收敛裁定胚胎）";

    /// <summary>
    /// 注册短信验证码服务。
    /// <para>门面 <see cref="ISmsCodeService"/> → <c>AddConstructibleService</c>（接口守卫工厂 + 实现 throw-factory）；
    /// <see cref="ISmsSender"/> 不注册默认实现——消费方实现（TryAdd 语义，对齐 AuthCenter ISmsSender 先例；
    /// 生产缺省 → 503 fail，dev 模式日志输出码 dev mock）。</para>
    /// <para><see cref="ISmsCodeScopeResolver"/> 默认空串实现（单例）供无租户消费方——消费方 TryAdd Scoped 实现覆盖
    /// （DMP: merchant:{MerchantUId}——m4 闭环：Scoped + ambient 读取路由值 mid）。</para>
    /// <para>m1 闭环（评审）：<c>TryAddSingleton&lt;IRateLimitCheck, MemoryRateLimitCheck&gt;</c> +
    /// <c>TryAddSingleton&lt;IMemoryCache&gt;</c> 兜底（守卫工厂经 ActivatorUtilities 解析剩余参数需可解析；
    /// 消费方显式注册 DB 版频控器/缓存时静默替换——MFA V0.2.1/AuthCenter V0.4.0 同款）。</para>
    /// <para>Options：SmsCodeOptions 标 [Options("TKWF:SmsCode")]——SG1 消费方自动绑定（模式 A 双通道）；
    /// 此处 <c>AddOptions</c> 兜底默认值。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（门面 ctor 依赖 IOptions<SmsCodeOptions>；SG1 [Options] 已在消费方自动绑定 TKWF:SmsCode 节）
        services.AddOptions<SmsCodeOptions>();

        // m1 闭环（评审）：点检查限流原语 fallback——MemoryRateLimitCheck（框架 v4.10.67）；
        // 消费方显式注册 SqlCountRateLimitCheck（RateLimiting 扩展）得 DB 跨实例版（TryAdd 语义业务可覆盖）
        services.TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>();

        // 租户维度归消费方（oracle3/m4 闭环）：默认空串实现（单例）——无租户消费方；消费方 TryAdd Scoped 覆盖
        services.TryAddSingleton<ISmsCodeScopeResolver, EmptyScopeResolver>();

        // 门禁序占位（M3 接线）——Captcha.Abstractions 可空依赖；m7 守卫（场景要门禁但 Captcha 未装配 → 守卫异常）留 M3
        // 正确路线（ADR90）：门面 AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory
        services.AddConstructibleService<ISmsCodeService, SmsCodeService>();
    }

    /// <summary>默认空串作用域解析器（单例）——无租户消费方默认；租户消费方 TryAdd 覆盖。</summary>
    internal sealed class EmptyScopeResolver : ISmsCodeScopeResolver
    {
        public string? GetScopePrefix() => "";
    }
}