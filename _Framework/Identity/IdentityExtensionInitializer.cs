using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 身份管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IUserStore"/> + <see cref="IRoleStore"/> +
    ///     <see cref="IUserManager"/>（AddConstructibleService）+ 接线型两契约（TryAddScoped / AddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——System 作用域幂等创建 Admin 系统角色种子</item>
    /// </list>
    /// </summary>
    [TKWFExtension("Identity")]
    public class IdentityExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Identity";

        /// <summary>扩展描述。</summary>
        public override string Description => "身份管理扩展——用户、角色、用户角色分配与凭据验证";

        /// <summary>
        /// 注册用户/角色存储与管理服务。
        /// <para>V4.10.53（领域自治根治，正确路线）三态注册：</para>
        /// <list type="bullet">
        /// <item><see cref="IUserStore"/> / <see cref="IRoleStore"/> / <see cref="IUserManager"/>（接口 : IDomainService）
        ///     用 <c>AddConstructibleService</c>——接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；
        ///     消费方经 <c>User.Use&lt;IUserStore&gt;()</c> / <c>User.Use&lt;IRoleStore&gt;()</c> / <c>User.Use&lt;IUserManager&gt;()</c> 解析。</item>
        /// <item><see cref="IAccountPasswordManager"/>（Account.Abstractions 契约，非 IDomainService——不可修改）
        ///     保持 TryAddScoped 接线型普通 DI——Account.DefaultPasswordResetFlow 经 GetService 解析。</item>
        /// <item><see cref="IRoleProvider{TUserInfo}"/>（Permissions.Abstractions 契约，非 IDomainService——不可修改）
        ///     保持 AddScoped（非 TryAdd——覆盖 Permissions 默认 DefaultRoleProvider，双白名单时生效）。</item>
        /// </list>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 默认值注册（对齐 Settings/Account 先例——UserManager ctor 依赖 IOptions<IdentityOptions>，
            // AddConstructibleService 守卫工厂经 ActivatorUtilities 解析剩余参数需 IOptions 可解析；无可配置节时
            // 默认值兜底，消费方可 services.Configure<IdentityOptions>(config.GetSection("TKWF:Identity")) 覆盖）。
            services.AddOptions<IdentityOptions>();

            // V4.10.53（领域自治根治）：门面注册改 AddConstructibleService——旧 TryAddScoped 构造注入
            // IDomainUser（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）；消费方统一经 User.Use<T>() 解析。
            services.AddConstructibleService<IUserStore, UserStore>();
            services.AddConstructibleService<IRoleStore, RoleStore>();
            services.AddConstructibleService<IUserManager, UserManager>();

            // V0.2.0 VEntity：UserRoleViewDataService 手写——v4.10.8 (ADR61) 起经 DomainReadOnlyDataServiceBase 基类判定自动注册
            //（不再手动 TryAddScoped）

            // V0.3.0：Account 密码重置落地适配器（Account 不注册默认实现，Identity 注册即生效；
            // 消费方覆盖须 AddScoped——扩展钩子先于消费方 OnRegisterDomainServices，TryAdd 被跳过）
            services.TryAddScoped<IAccountPasswordManager, IdentityPasswordManager>();

            // V0.3.0：Permissions 角色实时查库（AddScoped 非 TryAdd——覆盖 Permissions 默认 DefaultRoleProvider，
            // 仅当双白名单启用时生效；Scoped 缓存消除 PermissionChecker 双调用/批量放大）
            services.AddScoped<IRoleProvider<TUserInfo>, IdentityRoleProvider<TUserInfo>>();
        }

        /// <summary>
        /// 幂等创建 Admin 系统角色种子——仅示初始化钩子用法，不创建默认用户。
        /// <para>Admin 角色已存在则跳过（幂等）；创建失败静默（RoleStore 异常静默），不阻塞扩展启动。
        /// 真实持久化未接线（未注册 <see cref="IEntityDAC{TEntity}"/>——消费方 FreeSql 接线）时跳过种子
        /// （参照 Permissions 无 IEntityDAC 时跳过种子）。</para>
        /// <para>V4.10.53（领域自治根治）：IRoleStore 注册形态改 AddConstructibleService——裸
        /// <c>sp.GetService&lt;IRoleStore&gt;()</c> 触 CurrentAopUser 守卫必抛；改经 System 作用域 +
        /// <c>sysScope.System.Use&lt;IRoleStore&gt;()</c>（方案 A'，对齐 Tagging/Permissions 同款——Use&lt;T&gt;
        /// 内设 CurrentAopUser=SystemUser）。V4.10.25 (ADR78)：sp 经参数传入（替代 IServiceProviderAware setter 注入）。</para>
        /// </summary>
        public override async Task InitializeAsync(IServiceProvider sp)
        {
            // 真实持久化未接线（未注册 IEntityDAC<RoleEntity>）→ 跳过种子（NoOp 语义下无意义；守卫路径不经宿主）
            using var scope = sp.CreateScope();
            if (scope.ServiceProvider.GetService<IEntityDAC<RoleEntity>>() is null) return;

            // V4.10.53（领域自治根治，修复既有已知缺陷——对齐 Authentication V0.3.1 / Tagging / Permissions 方案 A'）：
            // IRoleStore 经 AddConstructibleService 注册——经 System 作用域 + sysScope.System.Use<IRoleStore>() 解析
            var host = sp.GetRequiredService<DomainHost<TUserInfo>>();
            await using var sysScope = await host.BeginSystemScopeAsync(sp);
            var roleStore = sysScope.System.Use<IRoleStore>();
            await SeedAdminRoleAsync(roleStore);
        }

        /// <summary>
        /// 种子核心（供 InitializeAsync 经 System 作用域解析 IRoleStore 后调用；测试直测本方法，免宿主）。
        /// 幂等创建 Admin 系统角色——已存在则跳过，不覆盖消费方既有角色。
        /// </summary>
        internal static async Task SeedAdminRoleAsync(IRoleStore roleStore)
        {
            var admin = await roleStore.GetByNameAsync("Admin");
            if (admin != null) return; // 已存在，幂等跳过

            await roleStore.CreateAsync(new RoleEntity
            {
                Name = "Admin",
                DisplayName = "管理员",
                IsSystemRole = true
            });
        }
    }
}
