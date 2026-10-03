using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.MFA;

/// <summary>
/// 多因素认证扩展初始化器——经 <c>[TKWFExtension("MFA")]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——Options 绑定（TKWF:Mfa 节）+ SG1 DataService（ADR61 自动注册）+
///       <see cref="IMfaService"/>（AddConstructibleService）+ <see cref="IMfaMethod"/> 双实现（TryAddEnumerable）</item>
/// <item><see cref="ConfigureFilters"/>——空实现（v0.1.0 无过滤器）</item>
/// <item><see cref="InitializeAsync"/>——空实现（v0.1.0 无种子数据；密钥 fail-fast 经 KeyStore 惰性初始化）</item>
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
    /// <item><see cref="IMfaMethod"/> 双实现（TOTP/SMS 策略）保持 <c>TryAddEnumerable</c>——多实现并存
    ///     （Oracle C5 防 SMS 静默丢失）；消费方可自定义扩展新方法。</item>
    /// <item><see cref="IMfaSmsSender"/> 不注册默认实现（接线型——消费方实现短信渠道，TryAdd 语义无默认）。</item>
    /// </list>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // 1. Options 绑定（TKWF:Mfa 节——ChallengeTtl/TOTP/频控/恢复码/密钥路径）
        services.AddOptions<MfaOptions>().BindConfiguration("TKWF:Mfa");

        // 2. SG1 DataService——ADR61 起自动注册（可构造工厂），不再手动 TryAddScoped

        // 3. 门面注册（V4.10.53 领域自治根治）：AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory；
        //    消费方统一经 User.Use<IMfaService>() 解析（旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）
        services.AddConstructibleService<IMfaService, MfaService>();

        // ⚠️ 多 IMfaMethod 实现必须 TryAddEnumerable（TryAddScoped 同 ServiceType 仅注册首个 → SMS 静默丢失，Oracle C5）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMfaMethod, TotpMfaMethod>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMfaMethod, SmsMfaMethod>());

        // IMfaSmsSender 不注册默认（消费方实现，TryAdd 语义——对齐 ISmsSender 先例）
    }

    /// <summary>过滤器不注册（v0.1.0 无过滤器）。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
    {
        // 空实现
    }

    /// <summary>系统就绪后初始化（空实现——密钥 fail-fast 经 KeyStore 惰性初始化，首次服务解析时触发）。</summary>
    public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
}
