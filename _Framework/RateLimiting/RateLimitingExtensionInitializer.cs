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
    ///       <c>RateLimitingWebExtension.ConfigureServices</c> 幂等）</item>
    /// <item>ConfigureFilters——不调用（限流在 Web 层中间件接线，Domain 层 [RateLimit] 属主框架既有能力）</item>
    /// <item>InitializeAsync——不调用（无种子/无持久化；本扩展纯 Web 层接线，无 SG1 实体）</item>
    /// </list>
    /// <para><b>注意</b>：启用本扩展仅完成 Options 绑定；<b>限流中间件接线须消费方显式
    /// <c>UseWebExtensions(e =&gt; e.Add&lt;RateLimitingWebExtension&gt;(...))</c></b>（v4.10.45 收敛迁移——
    /// 旧 <c>services.AddTkfwRateLimiting(...)</c> + <c>app.UseRateLimiter()</c> 静态接线已删除）。</para>
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
        /// 注册 Options + 数据访问门面（SG1 DataService 经 v4.10.8 ADR61 基类类型判定 + 消费方聚合自动注册）。
        /// <para>v0.3.0（点检查原语 DB Provider）：<see cref="IRateLimitCounterDataService"/> 经
        /// <c>AddConstructibleService</c>（接口可构造守卫工厂 + 实现类 throw-factory）——<see cref="SqlCountRateLimitCheck"/>
        /// （接线型，消费方显式 <c>AddSingleton&lt;IRateLimitCheck, SqlCountRateLimitCheck&gt;()</c>）在域作用域帧内
        /// <c>GetRequiredService&lt;IRateLimitCounterDataService&gt;()</c> 解析，守卫工厂从 CurrentAopUser 供给 ctor
        /// IDomainUser；帧外解析抛守卫（正确 fail）。<see cref="SqlCountRateLimitCheck"/> 不在此注册（RC1：消费方显式
        /// 注册为确定性路径——依赖扩展自动注册序不可靠）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 默认值 + 配置节绑定（SG1 [Options] 特性已在消费方自动绑定 TKWF:RateLimiting 节；此处兜底）
            services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

            // v0.3.0：限流计数表数据访问门面（AddConstructibleService——SqlCountRateLimitCheck 帧内解析通道）
            services.AddConstructibleService<IRateLimitCounterDataService, RateLimitCounterDataService>();
        }
    }
}