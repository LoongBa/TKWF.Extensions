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
        /// <para>V4.10.53（领域自治根治，ADR90）：</para>
        /// <list type="bullet">
        /// <item><see cref="IEmailRecordStore"/>（接口 : IDomainService）用 <c>AddConstructibleService</c>——接口可构造
        ///     守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方经 <c>User.Use&lt;IEmailRecordStore&gt;()</c> 解析
        ///     （AOP 路径——旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）。</item>
        /// <item><see cref="IEmailSender"/>（Emailing.Abstractions 契约非 IDomainService）保持 <c>TryAddScoped</c>
        ///     普通 DI（接线型——SmtpEmailSender ctor(IServiceProvider)，内部 C1 延迟解析 Store）。</item>
        /// </list>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddConstructibleService<IEmailRecordStore, EmailRecordStore>();
            services.TryAddScoped<IEmailSender, SmtpEmailSender>();
        }
    }
}
