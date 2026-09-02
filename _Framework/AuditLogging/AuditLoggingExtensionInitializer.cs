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
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IAuditLogStore"/> 默认 FreeSql 实现 +
    ///       <see cref="IAuditLogQueryService"/> 查询服务（TryAddScoped）+ <see cref="AuditLoggingOptions"/> Options 绑定</item>
    /// <item>ConfigureFilters——不调用（消费方 opt-in，通过 FilterBuilder.AddAuditLog 启用）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
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
        /// <para>TryAddScoped：消费方可自定义 <see cref="IAuditLogStore"/> / <see cref="IAuditLogQueryService"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// <para>Options 绑定：扩展注册 <see cref="AuditLoggingOptions"/> 默认值（通过 <c>AddOptions</c>）；
        /// 消费方在自身 <c>ConfigureServices</c> 中调用 <c>services.Configure&lt;AuditLoggingOptions&gt;(config.GetSection("TKWF:AuditLogging"))</c>
        /// 绑定 appsettings.json 配置（<see cref="ConfigureServices(IServiceCollection)"/> 基类签名仅接收 <c>IServiceCollection</c>，
        /// 不含 <c>IConfiguration</c>，因此绑定由消费方执行）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0：Options 注册（默认值），消费方可通过 services.Configure 从 appsettings.json 覆盖
            services.AddOptions<AuditLoggingOptions>();

            // V0.1.0：审计日志写入存储
            services.TryAddScoped<IAuditLogStore, FreeSqlAuditLogStore>();

            // V0.2.0：审计日志查询服务（TryAddScoped：消费方可自定义查询实现覆盖默认）
            services.TryAddScoped<IAuditLogQueryService, AuditLogQueryService>();
        }
    }
}
