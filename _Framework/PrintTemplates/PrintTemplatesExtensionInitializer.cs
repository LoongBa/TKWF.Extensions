using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板扩展初始化器（V0.2.0，V0.3.0 领域自治根治）——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="ITemplateStore"/> 默认实现（<see cref="TemplateStore"/>，
    ///      经两个 DataService 委托持久化）+ <see cref="ITemplateManager"/> 门面（AddConstructibleService）+ 
    ///      <see cref="ITemplateRenderer"/> Scriban 沙箱渲染（接线型 TryAddSingleton，纯渲染基础设施无 user 依赖）+
    ///      <see cref="PrintTemplatesOptions"/> Options 绑定（TKWF:PrintTemplates）</item>
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
        /// <para>V0.3.0（领域自治根治，ADR90）：门面 <see cref="ITemplateStore"/> / <see cref="ITemplateManager"/>
        /// 注册形态由 <c>TryAddScoped</c> 改为 <c>AddConstructibleService</c>——接口可构造守卫工厂（CurrentAopUser 守卫）+
        /// 实现类 throw-factory。旧形态构造注入 <see cref="IDomainUser"/>（永不注册 DI——D01）→ 运行时解析必失败；
        /// 消费方统一经 <c>User.Use&lt;ITemplateStore&gt;()</c> / <c>User.Use&lt;ITemplateManager&gt;()</c> 解析。</para>
        /// <para><see cref="ITemplateRenderer"/>（ScribanTemplateRenderer）为<b>接线型</b>基础设施（无状态 + 解析缓存线程安全，
        /// 无 user 依赖）——保持 <c>TryAddSingleton</c> 普通 DI 注册（消费方可覆盖，且不属 AddConstructibleService 目标）。</para>
        /// <para>Options 绑定：扩展注册 <see cref="PrintTemplatesOptions"/> 并绑定 TKWF:PrintTemplates 配置节（C4）；
        /// 消费方可在 appsettings.json 中配置 Scriban 执行限制。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 绑定（C4）：TKWF:PrintTemplates 配置节 + 默认值
            services.AddOptions<PrintTemplatesOptions>()
                .BindConfiguration("TKWF:PrintTemplates");

            // 模板存储（V0.3.0 领域自治根治：AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory）——
            // 经 SG1/xCodeGen 生成的 DataService 委托持久化（数据访问红线：不直接注入 IFreeSql / IEntityDAC）
            services.AddConstructibleService<ITemplateStore, TemplateStore>();

            // 模板管理门面（V0.3.0 领域自治根治：AddConstructibleService）
            services.AddConstructibleService<ITemplateManager, TemplateManager>();

            // Scriban 沙箱渲染器（接线型保留：TryAddSingleton——无状态 + 解析缓存线程安全，无 user 依赖）
            services.TryAddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        }
    }
}
