using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.RateLimiting
{
    /// <summary>
    /// 限流扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="RateLimitingOptions"/> Options 绑定
    ///       （AddOptions&lt;RateLimitingOptions&gt;().BindConfiguration("TKWF:RateLimiting")，与
    ///       <c>AddTkfwRateLimiting</c> 幂等）</item>
    /// <item>ConfigureFilters——不调用（限流在 Web 层中间件接线，Domain 层 [RateLimit] 属主框架既有能力）</item>
    /// <item>InitializeAsync——不调用（无种子/无持久化；本扩展纯 Web 层接线，无 SG1 实体）</item>
    /// </list>
    /// <para><b>注意</b>：启用本扩展仅完成 Options 绑定；<b>限流中间件接线须消费方显式调用
    /// <c>services.AddTkfwRateLimiting(...)</c></b>（Web 层接线点由消费方决定，扩展不自动注册中间件）。</para>
    /// <para>与 Domain 层关系（Oracle C2）：Domain 层 <c>FilterBuilder.AddRateLimit()</c> + <c>[RateLimit]</c>
    /// AOP 为主框架既有能力（V4.9.49 ADR19），本扩展<b>不重建</b>——两条路径双层互补：
    /// Web 层粗粒度 IP/端点兜底 + Domain 层用户级细粒度策略。</para>
    /// </summary>
    [TKWFExtension("RateLimiting")]
    public class RateLimitingExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "RateLimiting";

        /// <summary>扩展描述。</summary>
        public override string Description => "Web 层限流接线（ASP.NET Core AddRateLimiter 中间件——固定/滑动窗口/令牌桶 + IP/用户分区 + 429/Retry-After + TKWF:RateLimiting 配置节；与 Domain 层 [RateLimit] AOP 双层互补；零第三方依赖）";

        /// <summary>
        /// 注册 RateLimitingOptions 配置绑定（消费方经 <c>TKWF:RateLimiting</c> 配置节即可配置）。
        /// <para>无状态注册（Options 单例），不注册任何服务实现——限流状态由 ASP.NET Core
        /// RateLimiter 中间件管理（进程内）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 默认值 + 配置节绑定（SG1 [Options] 特性已在消费方自动绑定 TKWF:RateLimiting 节；此处兜底）
            services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);
        }
    }
}