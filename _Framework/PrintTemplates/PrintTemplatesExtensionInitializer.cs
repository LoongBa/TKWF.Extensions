using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="ITemplateStore"/> 默认 FreeSql 实现 +
    ///       <see cref="ITemplateManager"/> 门面 + <see cref="ITemplateRenderer"/> Scriban 沙箱渲染 +
    ///       <see cref="PrintTemplatesOptions"/> Options 绑定（TKWF:PrintTemplates）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("PrintTemplates")]
    public class PrintTemplatesExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "PrintTemplates";

        /// <summary>扩展描述。</summary>
        public override string Description => "打印模板引擎与版本化（Scriban 沙箱渲染 + Draft/Active/Archived 生命周期）";

        /// <summary>
        /// 注册模板存储 + 门面 + 渲染器 + Options 配置。
        /// <para>TryAddScoped/TryAddSingleton：消费方可自定义实现，扩展默认实现不覆盖消费方。</para>
        /// <para>Options 绑定：扩展注册 <see cref="PrintTemplatesOptions"/> 并绑定 TKWF:PrintTemplates 配置节（C4）；
        /// 消费方可在 appsettings.json 中配置 Scriban 执行限制。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 绑定（C4）：TKWF:PrintTemplates 配置节 + 默认值
            services.AddOptions<PrintTemplatesOptions>()
                .BindConfiguration("TKWF:PrintTemplates");

            // 模板存储（TryAddScoped：消费方可自定义 ITemplateStore 覆盖默认）
            services.TryAddScoped<ITemplateStore, FreeSqlTemplateStore>();

            // 模板管理门面（TryAddScoped：消费方可自定义 ITemplateManager 覆盖默认）
            services.TryAddScoped<ITemplateManager, TemplateManager>();

            // Scriban 沙箱渲染器（TryAddSingleton：无状态 + 解析缓存线程安全）
            services.TryAddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        }
    }
}
