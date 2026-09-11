using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元扩展初始化器——经 <c>[TKWFExtension("OrganizationUnit")]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 SG1 DataService + <see cref="IOrganizationUnitStore"/> +
    ///       <see cref="IOrganizationUnitManager"/>（均 TryAddScoped，消费方自定义优先）</item>
    /// <item><see cref="ConfigureFilters"/>——空实现（V0.1.0 无过滤器）</item>
    /// <item><see cref="InitializeAsync"/>——空实现（V0.1.0 无种子数据）</item>
    /// </list>
    /// <para>V4.9.85 起发现不自动启用——消费方须在自身领域初始化器上标注
    /// <c>[TKWFEnabledExtension(typeof(OrganizationUnitExtensionInitializer&lt;&gt;))]</c> 白名单声明，三钩子才执行。</para>
    /// </summary>
    [TKWFExtension("OrganizationUnit")]
    public class OrganizationUnitExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "OrganizationUnit";

        /// <summary>扩展描述。</summary>
        public override string Description => "组织单元扩展——树形部门/团队/分组定义、维护与用户归属管理（FreeSql 持久化）";

        /// <summary>
        /// 注册 Store + Manager（SG1 DataService 经 v4.10.8 ADR61 基类类型判定 + 消费方聚合自动注册）。
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. SG1 DataService——ADR61 起自动注册（可构造工厂），不再手动 TryAddScoped

            // 2. 存储（委托 DataService——数据访问红线合规，不注入 IFreeSql/IEntityDAC）
            services.TryAddScoped<IOrganizationUnitStore, OrganizationUnitStore>();

            // 3. 管理门面（业务规则 + ITransactionManager 事务包裹）——工厂注册：
            //    OrganizationUnitManager 构造函数 internal（IOrganizationUnitStore 为 internal 契约），
            //    TryAddScoped 工厂语义与类型注册等价（消费方自定义实现仍优先）。
            services.TryAddScoped<IOrganizationUnitManager>(sp => new OrganizationUnitManager(
                sp.GetRequiredService<IOrganizationUnitStore>(),
                sp.GetRequiredService<ITransactionManager>(),
                sp.GetRequiredService<ILogger<OrganizationUnitManager>>()));
        }

        /// <summary>过滤器不注册（V0.1.0 无过滤器）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现
        }

        /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
        public override Task InitializeAsync() => Task.CompletedTask;
    }
}
