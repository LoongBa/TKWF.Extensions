using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.MFA;

/// <summary>
/// 多因素认证扩展初始化器——经 <c>[TKWFExtension("MFA")]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——Options 绑定（TKWF:Mfa 节）+ keyed <see cref="ISymmetricKeyProvider"/>
///       （SymmetricKeyProviderKeys.Mfa——FileSymmetricKeyProvider 单例工厂，替代原静态 MfaSecretKeyStore）+
///       SG1 DataService（ADR61 自动注册）+ <see cref="IMfaService"/>（AddConstructibleService）+
///       <see cref="IMfaMethod"/> 双实现（TryAddEnumerable）</item>
/// <item><see cref="ConfigureFilters"/>——空实现（v0.1.0 无过滤器）</item>
/// <item><see cref="InitializeAsync"/>——空实现（v0.1.0 无种子数据；密钥 fail-fast 经 keyed 单例工厂构造即触发）</item>
/// </list>
/// <para>零扩展间依赖（ADR-MFA-独立扩展与零依赖边界）：不引 Authentication/不拆其 Abstractions——短信渠道经
/// 消费方实现 <see cref="IMfaSmsSender"/> 注入（TryAdd 语义无默认）。</para>
/// <para>V4.9.85 起发现不自动启用——消费方须在自身领域初始化器上标注
/// <c>[TKWFEnabledExtension(typeof(MFAExtensionInitializer&lt;&gt;))]</c> 白名单声明，三钩子才执行。</para>
/// </summary>
[TKWFExtension("MFA")]
public class MFAExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "MFA";

    /// <summary>扩展描述。</summary>
    public override string Description => "多因素认证扩展——TOTP（RFC 6238 自研）+ 短信验证码双方法：绑定/解绑 + 挑战-验证流 + 尝试频控 + 恢复码（独立扩展零依赖——消费方登录流编排）";

    /// <summary>
    /// 注册 Options + 服务（SG1 DataService 经 v4.10.8 ADR61 基类类型判定 + 消费方聚合自动注册）。
    /// <para>V4.10.53（领域自治根治，正确路线）：</para>
/// <list type="bullet">
/// <item><see cref="IMfaService"/>（接口 : IDomainService，门面）用 <c>AddConstructibleService</c>——接口可构造
///     守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方经 <c>User.Use&lt;IMfaService&gt;()</c> 解析
///     （AOP 路径先设 CurrentAopUser 再 GetRequiredService，工厂据此显式传入 DomainUser）。</item>
/// <item><see cref="IMfaMethod"/> 双实现（TOTP/SMS 策略）注册由 TryAddEnumerable 改 <c>TryAddEnumerableConstructible</c>
///     （V4.10.55 ADR92——集合版守卫工厂：帧内 CurrentAopUser 供给集合元素 ctor 的 IDomainUser；帧外枚举抛守卫）——
///     多实现并存（Oracle C5 防 SMS 静默丢失）；实现继承 <see cref="DomainServiceBase"/>（经基类 User 取上下文）；
///     消费方可自定义扩展新方法。</item>
/// <item><see cref="IMfaSmsSender"/> 不注册默认实现（接线型——消费方实现短信渠道，TryAdd 语义无默认）。</item>
/// </list>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // 1. Options 绑定（TKWF:Mfa 节——ChallengeTtl/TOTP/频控/恢复码/密钥路径）
        services.AddOptions<MfaOptions>().BindConfiguration("TKWF:Mfa");

        // 1.1 keyed 对称密钥提供者（E4 密钥管理抽象 V0.2.0——TOTP secret AES-GCM 密钥单例工厂；构造即加载，
        //     生产缺密钥 fail-fast 拒启动 / 开发按 FileSymmetricKeyProvider 三态语义；TotpMfaMethod 经
        //     [FromKeyedServices(SymmetricKeyProviderKeys.Mfa)] 注入——替代原 MfaSecretKeyStore 静态持有）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(SymmetricKeyProviderKeys.Mfa, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<MfaOptions>>().Value;
            return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
        });

        // 2. SG1 DataService——ADR61 起自动注册（可构造工厂），不再手动 TryAddScoped

        // 2.1 IRateLimitCheck fallback（V4.10.67 R3 迁移，Oracle7 C1 方案 B + RC1 排序裁定）：
        //     TryAddSingleton 首注册胜出——消费方显式注册 SqlCountRateLimitCheck 等实现时静默替换；
        //     MFA 依赖扩展（RateLimiting）自动注册序不可靠，消费方显式注册为确定性路径；
        //     未启用 RateLimiting 扩展时频控"始终在"（MemoryRateLimitCheck 进程级单例，安全语义不降级）
        services.TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>();

        // 3. 门面注册（V4.10.53 领域自治根治）：AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory；
        //    消费方统一经 User.Use<IMfaService>() 解析（旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）
        services.AddConstructibleService<IMfaService, MfaService>();

        // ⚠️ 多 IMfaMethod 实现必须 TryAddEnumerableConstructible（V4.10.55 ADR92——集合版守卫工厂：
        // 集合内 DomainServiceBase 派生实现 ctor 的 IDomainUser 由帧内 CurrentAopUser 供给（门面 MfaService
        // 经 User.Use<IMfaService>() 帧内创建时枚举集合）；TryAddScoped 同 ServiceType 仅注册首个 → SMS 静默丢失（Oracle C5）
        services.TryAddEnumerableConstructible<IMfaMethod, TotpMfaMethod>();
        services.TryAddEnumerableConstructible<IMfaMethod, SmsMfaMethod>();

        // IMfaSmsSender 不注册默认（消费方实现，TryAdd 语义——对齐 ISmsSender 先例）
    }

    /// <summary>过滤器不注册（v0.1.0 无过滤器）。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
    {
        // 空实现
    }

    /// <summary>系统就绪后初始化（空实现——密钥 fail-fast 经 keyed FileSymmetricKeyProvider 单例工厂构造即触发）。</summary>
    public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
}
