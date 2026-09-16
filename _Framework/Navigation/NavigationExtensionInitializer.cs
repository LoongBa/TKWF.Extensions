using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W4)：导航扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——DI 构建前：从 <c>ProjectMetaContextBase.Instance.Contributors["Menu"]</c>
    /// （A+ 贡献者机制阶段 2，V4.10.30：统一描述符 ContributorDescriptor，接口判定收集）收集贡献者
    /// （同步调用 <c>ConfigureMenu</c>，Oracle H1），填充 <c>MenuDefinitionRepository</c>，
    /// 注册 IMenuDefinitionRepository + IMenuManager</item>
    /// <item><see cref="ConfigureFilters"/>——空（菜单不注册 AOP 过滤器）</item>
    /// <item><see cref="InitializeAsync"/>——空（菜单定义已在 ConfigureServices 收集）</item>
    /// </list>
    /// <para>命名空间 <c>TKWF.Ext.Navigation</c>（D17 §5.2 + 包名约定）。</para>
    /// </summary>
    [TKWFExtension("Navigation")]
    public class NavigationExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
        public override string Name => "Navigation";

        /// <summary>扩展描述。</summary>
        public override string Description => "导航扩展——菜单数据模型与贡献机制";

        /// <summary>
        /// 注册菜单服务 + 从 ProjectMetaContext 桥收集菜单贡献者定义。
        /// <para>时序保证：V4.9.71 修复后 <see cref="DomainHostInitializerBase{TUserInfo}.InitializeDiContainer"/>
        /// 在 RegisterInfrastructureInternal（赋 Instance）之后调用本钩子，<c>ProjectMetaContextBase.Instance</c> 已就绪。
        /// 贡献者同步收集（Oracle H1——ConfigureServices 同步 void 无法 await 异步贡献者）。</para>
        /// <para>V4.10.30 (A+ 阶段 2)：读新桥 <c>Contributors[ContributorTargetKinds.Menu]</c>（统一 ContributorDescriptor，
        /// 接口判定收集）——替代旧桥 <c>MenuContributors</c>（已标 Obsolete）。ContainsKey+索引器（P1-1：
        /// IReadOnlyDictionary 无 TryGetValue）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0（Oracle 条件 1 BLOCKER）：Options 默认值注册——确保 IOptions<NavigationOptions>
            // 在所有宿主（含非 SG1/测试）可解析（MenuManager 构造注入；对齐 Settings/FeatureManagement AddOptions 先例）
            services.AddOptions<NavigationOptions>();

            // 1. 收集菜单贡献者定义（编译期清单 → 运行时实例化 → 同步 ConfigureMenu）
            var ctx = ProjectMetaContextBase.Instance as ProjectMetaContextBase;
            var contributors = ctx != null && ctx.Contributors.ContainsKey(ContributorTargetKinds.Menu)
                ? ctx.Contributors[ContributorTargetKinds.Menu]
                : Array.Empty<ContributorDescriptor>();
            var repository = new MenuDefinitionRepository();
            if (contributors.Count > 0)
            {
                var context = new MenuConfigurationContext();
                foreach (var descriptor in contributors)
                {
                    if (descriptor.ContributorType == null) continue;
                    var contributor = (IMenuContributor?)Activator.CreateInstance(descriptor.ContributorType);
                    if (contributor == null)
                        throw new InvalidOperationException($"菜单贡献者无法实例化: {descriptor.FullName}（需要无参构造器）");
                    contributor.ConfigureMenu(context);
                }
                repository.AddRange(context.MenuItems);
            }

            // 2. 默认实现注册（TryAdd*：消费方可自定义覆盖，且本填充实例不覆盖消费方实现）
            services.TryAddSingleton<IMenuDefinitionRepository>(repository);
            services.TryAddSingleton<IMenuManager, MenuManager<TUserInfo>>();
        }

        /// <summary>菜单不注册 AOP 过滤器（渲染/过滤在 IMenuManager，非 AOP 热路径）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder) { }

        /// <summary>菜单定义已在 ConfigureServices 收集，无需初始化。</summary>
        public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
    }
}