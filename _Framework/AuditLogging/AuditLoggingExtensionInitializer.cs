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
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IAuditLogStore"/> 默认 FreeSql 实现（TryAddScoped）</item>
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
        public override string Description => "审计日志扩展——方法级调用事件数据库存储";

        /// <summary>
        /// 注册审计日志存储服务。
        /// <para>TryAddScoped：消费方可自定义 <see cref="IAuditLogStore"/> 实现（如写文件、发送到 SIEM），
        /// 扩展默认 FreeSql 实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IAuditLogStore, FreeSqlAuditLogStore>();
        }
    }
}
