using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 身份管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IUserStore"/> + <see cref="IRoleStore"/> + <see cref="IUserManager"/>（TryAddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——幂等创建 Admin 系统角色种子</item>
    /// </list>
    /// </summary>
    [TKWFExtension("Identity")]
    public class IdentityExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>, IServiceProviderAware
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Identity";

        /// <summary>扩展描述。</summary>
        public override string Description => "身份管理扩展——用户、角色、用户角色分配与凭据验证";

        /// <summary>注入的 IServiceProvider（InitializeExtensionsAsync 阶段设置，IServiceProviderAware）。</summary>
        public IServiceProvider? ServiceProvider { get; set; }

        /// <summary>
        /// 注册用户/角色存储与管理服务。
        /// <para>TryAddScoped：消费方可自定义 <see cref="IUserStore"/> / <see cref="IRoleStore"/> / <see cref="IUserManager"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IUserStore, FreeSqlUserStore>();
            services.TryAddScoped<IRoleStore, FreeSqlRoleStore>();
            services.TryAddScoped<IUserManager, UserManager>();
        }

        /// <summary>
        /// 幂等创建 Admin 系统角色种子——仅示初始化钩子用法，不创建默认用户。
        /// <para>Admin 角色已存在则跳过（幂等）；创建失败静默，不阻塞扩展启动。</para>
        /// </summary>
        public override async Task InitializeAsync()
        {
            var roleStore = ServiceProvider?.GetService<IRoleStore>();
            if (roleStore == null) return;

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