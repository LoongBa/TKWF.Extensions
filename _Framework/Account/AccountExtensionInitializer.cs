using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册锁定/重置存储 + 主框架缺口实现（TryAddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// <para>注意：<see cref="IAccountPasswordManager"/> 由消费方实现并注册（扩展不提供默认实现）。</para>
    /// </summary>
    [TKWFExtension("Account")]
    public class AccountExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Account";

        /// <summary>扩展描述。</summary>
        public override string Description => "账户管理扩展——账户锁定策略与密码重置流程默认实现";

        /// <summary>
        /// 注册账户安全策略服务。
        /// <para>TryAddScoped：消费方可自定义存储/策略实现，扩展默认实现不覆盖消费方。
        /// <see cref="IAccountLockoutPolicy"/> 与 <see cref="IPasswordResetFlow"/> 为主框架扩展点，
        /// 注册后框架 AuthController 自动调用。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IAccountLockoutStore, FreeSqlAccountLockoutStore>();
            services.TryAddScoped<IPasswordResetStore, FreeSqlPasswordResetStore>();
            services.TryAddScoped<IAccountLockoutPolicy, FreeSqlAccountLockoutPolicy>();
            services.TryAddScoped<IPasswordResetFlow, DefaultPasswordResetFlow>();
            // IAccountPasswordManager 不注册默认实现——消费方实现（适配 Identity IUserManager）
        }
    }
}