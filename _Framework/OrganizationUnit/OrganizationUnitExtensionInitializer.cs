using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元扩展初始化器——经 <c>[TKWFExtension("OrganizationUnit")]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 SG1 DataService + <see cref="IOrganizationUnitStore"/>
    ///       （TryAddScoped，内部接线型）+ <see cref="IOrganizationUnitManager"/>（AddConstructibleService 门面注册）</item>
    /// <item><see cref="ConfigureFilters"/>——空实现（V0.1.0 无过滤器）</item>
    /// <item><see cref="InitializeAsync"/>——空实现（V0.1.0 无种子数据）</item>
    /// </list>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）二态注册：</para>
    /// <list type="bullet">
    /// <item><b>门面（AddConstructibleService）</b>——<see cref="IOrganizationUnitManager"/>（接口 : IDomainService）：
    ///     接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；实现继承
    ///     <see cref="TKW.Framework.Domain.DomainServiceBase"/>（经基类 <c>User</c> 取上下文——<b>IDomainUser 永不注册 DI</b>，
    ///     旧工厂 lambda 构造注入 IDomainUser 生产解析必失败）+ <c>[DiContractIgnore]</c> 豁免 DI001；
    ///     消费方统一经 <c>User.Use&lt;IOrganizationUnitManager&gt;()</c> 解析。</item>
    /// <item><b>内部接线型（TryAddScoped 普通 DI）</b>——<see cref="IOrganizationUnitStore"/>：接口 internal
    ///     （Manager 内部组合依赖，不可改可见性），实现 ctor(<see cref="System.IServiceProvider"/>) + C1 延迟解析
    ///     DataService——普通 DI 可构造（Manager 守卫工厂经 ActivatorUtilities 解析 Store 时无 IDomainUser 依赖）。</item>
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

            // 2. 存储（内部接线型 TryAddScoped 普通 DI——委托 DataService，数据访问红线合规，不注入 IFreeSql/IEntityDAC）：
            //    V4.10.53（ADR90）：实现 ctor 改 (IServiceProvider)——C1 延迟解析 DataService（GetRequiredService），
            //    修复旧 ctor(IDomainUser) 在 Manager 工厂解析 Store 时 IDomainUser 永不注册（D01）致生产解析失败。
            services.TryAddScoped<IOrganizationUnitStore, OrganizationUnitStore>();

            // 3. 管理门面（V4.10.53 领域自治根治）——AddConstructibleService：接口可构造守卫工厂（CurrentAopUser 守卫）
            //    + 实现类 throw-factory；消费方经 User.Use<IOrganizationUnitManager>() 解析（AOP 路径）。
            //    旧 TryAddScoped 工厂 lambda（构造注入 IDomainUser + internal ctor）生产解析必失败
            //    （IDomainUser 永不注册 DI——D01，v0.3.3 同根缺陷）；改后 ctor public + IDomainUser 首位经基类 User。
            //    其余参数（Store + ITransactionManager + ILogger）由守卫工厂 ActivatorUtilities 从 DI 解析：
            //    Store 接线型普通注册可解析。
            services.AddConstructibleService<IOrganizationUnitManager, OrganizationUnitManager>();
        }

        /// <summary>过滤器不注册（V0.1.0 无过滤器）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现
        }

        /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
        public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
    }
}
