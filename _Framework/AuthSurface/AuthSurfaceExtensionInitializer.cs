using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 授权面扩展初始化器——口令兑换体系 + 我的应用聚合 + 授权快照候选（数据属主 = 兑换/授权记录）。
/// <para>DI 注册（V0.1.0）：4 门面 <c>AddConstructibleService</c>（守卫工厂，消费方 <c>User.Use&lt;接口&gt;()</c> 解析）；
/// 实体 DataService + VEntity 只读 DataService 由 SG1/ADR61 自动注册（Initializer 零手动）；<c>IRateLimitCheck</c>
/// 内存默认 fallback（v4.10.67——未启用 RateLimiting 扩展时兑换频控不消失；R2 SqlCount 提供后可替换，TryAdd 语义）。</para>
/// </summary>
[TKWFExtension("AuthSurface")]
public class AuthSurfaceExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    public override string Name => "AuthSurface";

    public override string Description
        => "授权面——口令兑换体系（code/redemption/grant/batch + 管理端 v0.2.0）+ 我的应用聚合 + 授权快照候选；行级 FK=AuthAccount.UId（P4）；兑换/应用查询经本扩展自有门面（跨扩展 VEntity——UserCenter 退役后唯一通道）；兑换码 SHA256 哈希存储 + CAS 原子兑换 + 频控";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值注册（[Options("TKWF:AuthSurface")]——SG1 自动绑定消费方配置节）
        services.AddOptions<AuthSurfaceOptions>();

        // v4.10.67：点检查限流原语内存默认（未启用 RateLimiting 扩展时频控不消失；R2 SqlCountRateLimitCheck 替换后自动切换）
        services.TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>();

        // E4 密钥管理抽象（v0.2.0 核验场景）：keyed ISymmetricKeyProvider（FileSymmetricKeyProvider——构造即加载密钥：
        // 生产缺密钥 fail-fast / 开发随机兜底；AddKeyedSingleton 惰性构造，首次解析门面时触发）——
        // RedemptionCommandService ctor 经 [FromKeyedServices] 注入（对齐 AuthCenter/Federation/MFA 先例）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(SymmetricKeyProviderKeys.AuthSurface, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<AuthSurfaceOptions>>().Value;
            return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
        });

        // 门面（ADR90 正确路线——接口可构造守卫工厂 + 实现类 throw-factory；消费方 User.Use<接口>() 解析）
        services.AddConstructibleService<IRedemptionCommandService, RedemptionCommandService>();
        services.AddConstructibleService<IRedemptionQueryService, RedemptionQueryService>();
        services.AddConstructibleService<IUserAppsQueryService, UserAppsQueryService>();
        services.AddConstructibleService<IAuthAppService, AuthAppService>();
    }

    // ConfigureFilters: 空（非过滤器扩展）
    // InitializeAsync: 空（无种子/无持久化前置——兑换码/应用目录由运营侧经门面写入）
}
