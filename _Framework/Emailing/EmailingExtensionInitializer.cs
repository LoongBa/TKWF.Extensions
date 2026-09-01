using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件发送扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IEmailSender"/> + <see cref="IEmailRecordStore"/>（TryAddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("Emailing")]
    public class EmailingExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Emailing";

        /// <summary>扩展描述。</summary>
        public override string Description => "邮件发送扩展——SMTP 邮件发送与记录持久化";

        /// <summary>
        /// 注册邮件发送与记录存储服务。
        /// <para>TryAddScoped：消费方可自定义 <see cref="IEmailSender"/> / <see cref="IEmailRecordStore"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IEmailRecordStore, FreeSqlEmailRecordStore>();
            services.TryAddScoped<IEmailSender, SmtpEmailSender>();
        }
    }
}
