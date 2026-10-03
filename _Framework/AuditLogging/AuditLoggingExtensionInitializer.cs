using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IAuditLogStore"/> 默认实现（接线型 TryAddScoped）+
    ///       <see cref="IAuditLogQueryService"/> 查询服务（AddConstructibleService）+ <see cref="IAuditLogAnalyticsService"/>
    ///       分析服务（AddConstructibleService）+ <see cref="AuditLoggingOptions"/> Options 绑定</item>
    /// <item>ConfigureFilters——不调用（消费方 opt-in，通过 FilterBuilder.AddAuditLog 启用）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态三态：</para>
    /// <list type="bullet">
    /// <item><see cref="IAuditLogStore"/>（主框架契约，非 IDomainService——不可修改主框架；主框架 AuditLogFilterAttribute
    ///      经 context.ServiceProvider.GetService&lt;IAuditLogStore&gt;() 普通 DI 解析）→ <b>接线型 TryAddScoped</b>；
    ///      实现 ctor(IServiceProvider, ILogger)——全 DI 可解析（修复旧 ctor 注入 IDomainUser 致 GetService 构造失败、
    ///      审计落库静默失效的生产故障）。</item>
    /// <item><see cref="IAuditLogQueryService"/> / <see cref="IAuditLogAnalyticsService"/>（接口 : IDomainService）→
    ///      <b>AddConstructibleService</b>——接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；
    ///      消费方统一经 <c>User.Use&lt;IAuditLogQueryService&gt;()</c> / <c>User.Use&lt;IAuditLogAnalyticsService&gt;()</c> 解析。</item>
    /// </list>
    /// </summary>
    [TKWFExtension("AuditLogging")]
    public class AuditLoggingExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "AuditLogging";

        /// <summary>扩展描述。</summary>
        public override string Description => "审计日志扩展——方法级调用事件数据库存储与查询";

        /// <summary>
        /// 注册审计日志存储 + 查询服务 + Options 配置。
        /// <para>V4.10.53（领域自治根治，ADR90）注册形态三态：</para>
        /// <list type="bullet">
        /// <item><see cref="IAuditLogStore"/>（主框架契约，非 IDomainService——不可修改主框架；主框架 AuditLogFilterAttribute
        ///     经 context.ServiceProvider.GetService&lt;IAuditLogStore&gt;() 普通 DI 解析）→ <b>TryAddScoped 接线型</b>
        ///     （消费方可自定义实现覆盖默认）；实现 ctor(IServiceProvider, ILogger)——全 DI 可解析（修复旧 ctor 注入
        ///     IDomainUser 致 GetService 构造失败、审计落库静默失效的生产故障）。</item>
        /// <item><see cref="IAuditLogQueryService"/> / <see cref="IAuditLogAnalyticsService"/>（接口 : IDomainService）→
        ///     <b>AddConstructibleService</b>（接口可构造守卫工厂 + 实现类 throw-factory）——消费方统一经
        ///     <c>User.Use&lt;IAuditLogQueryService&gt;()</c> / <c>User.Use&lt;IAuditLogAnalyticsService&gt;()</c> 解析。</item>
        /// </list>
        /// <para>Options 绑定：扩展注册 <see cref="AuditLoggingOptions"/> 默认值（通过 <c>AddOptions</c>——AnalyticsService
        /// ctor 依赖 <c>IOptions&lt;AuditLoggingOptions&gt;</c>，守卫工厂 ActivatorUtilities 解析需其可解析）；
        /// 消费方在自身 <c>ConfigureServices</c> 中调用 <c>services.Configure&lt;AuditLoggingOptions&gt;(config.GetSection("TKWF:AuditLogging"))</c>
        /// 绑定 appsettings.json 配置（<see cref="ConfigureServices(IServiceCollection)"/> 基类签名仅接收 <c>IServiceCollection</c>，
        /// 不含 <c>IConfiguration</c>，因此绑定由消费方执行）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0：Options 注册（默认值），消费方可通过 services.Configure 从 appsettings.json 覆盖
            services.AddOptions<AuditLoggingOptions>();

            // V0.1.0：审计日志写入存储——主框架契约接线型（IAuditLogStore 非 IDomainService，不可修改主框架；
            // AuditLogFilterAttribute 经 context.ServiceProvider.GetService<IAuditLogStore>() 普通 DI 解析）→ TryAddScoped
            services.TryAddScoped<IAuditLogStore, AuditLogStore>();

            // V0.2.0：审计日志查询服务——接口 : IDomainService → AddConstructibleService（接口可构造守卫工厂 +
            // 实现类 throw-factory，消费方经 User.Use<IAuditLogQueryService>() 解析）
            services.AddConstructibleService<IAuditLogQueryService, AuditLogQueryService>();

            // V0.3.0：审计日志分析服务（统计聚合 + 保留天数清理）——接口 : IDomainService → AddConstructibleService
            // （消费方经 User.Use<IAuditLogAnalyticsService>() 解析）
            services.AddConstructibleService<IAuditLogAnalyticsService, AuditLogAnalyticsService>();
        }
    }
}
