using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册锁定/重置存储（AddConstructibleService）+ 主框架缺口实现
    ///     （接线型 TryAddScoped）+ 登录历史服务（门面 AddConstructibleService）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// <para>注意：<see cref="IAccountPasswordManager"/> 由消费方实现并注册（扩展不提供默认实现）。</para>
    /// </summary>
    [TKWFExtension("Account")]
    [TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.SecurityLog.ISecurityLogQueryService), MinVersion = "0.1.0")]
    public class AccountExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Account";

        /// <summary>扩展描述。</summary>
        public override string Description => "账户管理扩展——账户锁定策略与密码重置流程默认实现";

        /// <summary>
        /// 注册账户安全策略服务。
        /// <para>V4.10.53（领域自治根治，正确路线）：</para>
        /// <list type="bullet">
        /// <item><see cref="IAccountLockoutStore"/> / <see cref="IPasswordResetStore"/> / <see cref="ILoginHistoryService"/>
        ///     （接口 : IDomainService）用 <c>AddConstructibleService</c>——接口可构造守卫工厂（CurrentAopUser 守卫）
        ///     + 实现类 throw-factory；消费方经 <c>User.Use&lt;IAccountLockoutStore&gt;()</c> /
        ///     <c>User.Use&lt;ILoginHistoryService&gt;()</c> 等解析。</item>
        /// <item><see cref="IAccountLockoutPolicy"/> / <see cref="IPasswordResetFlow"/>（主框架扩展点，非 IDomainService）
        ///     保持 TryAddScoped 普通 DI——主框架 AuthController 经 GetOptionalService 解析（接线型，
        ///     AddConstructibleService 编译约束 where TInterface : IDomainService 不满足，不可用）。</item>
        /// </list>
        /// <para>V0.5.x（批次间交互遗留修复）：<see cref="ILoginHistoryService"/> 由接线型（ctor IServiceProvider +
        /// GetRequiredService 普通解析）升<b>门面</b>——SecurityLog V0.4.0 已门面化（三契约 AddConstructibleService 守卫工厂），
        /// 普通解析在域作用域帧外触「领域架构守卫」异常；升门面后继承 <see cref="DomainServiceBase"/> 经基类
        /// <c>User.Use&lt;SecurityLog 契约&gt;()</c> AOP 路径解析（IDomainUser 永不注册 DI，D01 铁律）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V4.10.53（领域自治根治）：Store/门面继承 DomainServiceBase + AddConstructibleService——
            // 消费方统一经 User.Use<IAccountLockoutStore>() / User.Use<IPasswordResetStore>() / User.Use<ILoginHistoryService>() 解析
            // （AOP 路径先设 CurrentAopUser 再 GetRequiredService，工厂据此显式传入 DomainUser）。
            services.AddOptions<AccountOptions>(); // Policy/Flow ctor 依赖 IOptions<AccountOptions>（无 User 依赖）
            services.AddConstructibleService<IAccountLockoutStore, AccountLockoutStore>();
            services.AddConstructibleService<IPasswordResetStore, PasswordResetStore>();

            // 主框架扩展点：接线型普通 DI（AuthController 经 GetOptionalService 解析；不继承 DomainServiceBase）
            services.TryAddScoped<IAccountLockoutPolicy, FreeSqlAccountLockoutPolicy>();
            services.TryAddScoped<IPasswordResetFlow, DefaultPasswordResetFlow>();

            // V0.3.0：登录历史与异常检测——消费 SecurityLog 扩展查询 API（SecurityLog 是登录历史唯一数据源，
            // 不重复建表）。V0.5.x：升门面 AddConstructibleService——SecurityLog 契约经基类 User.Use<接口>() AOP 路径
            // 解析（SecurityLog V0.4.0 守卫工厂要求 AOP 帧；未启用抛明确异常——C1 模式，异常消息含 SecurityLog 类型名）。
            services.AddConstructibleService<ILoginHistoryService, LoginHistoryService>();
            // IAccountPasswordManager 不注册默认实现——消费方实现（适配 Identity IUserManager）
        }
    }
}