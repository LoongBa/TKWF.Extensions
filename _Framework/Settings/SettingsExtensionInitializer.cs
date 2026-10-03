using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="ISettingManager"/>（AddConstructibleService）+
    ///       <see cref="SettingsOptions"/> Options 注册 + <see cref="IMemoryCache"/> 注册（TryAddSingleton）</item>
    /// <item>ConfigureFilters——不调用（V0.2.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.2.0 无种子）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("Settings")]
    public class SettingsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Settings";

        /// <summary>扩展描述。</summary>
        public override string Description => "设置管理扩展——分层键值对存储与读取";

        /// <summary>
        /// 注册设置管理服务。
        /// <para>Options 绑定（V0.2.0）：<see cref="SettingsOptions"/> 标注 <see cref="OptionsAttribute"/>（<c>"TKWF:Settings"</c> 节）——
        /// SG1 在消费方生成 <c>GeneratedOptionsBindings</c>，宿主启动期经 <c>RegisterOptionsBindings</c> 自动
        /// <c>services.Configure&lt;SettingsOptions&gt;(configuration.GetSection("TKWF:Settings"))</c>（与 Navigation/Permissions 同模式）。
        /// 此处 <c>AddOptions</c> 仅注册默认值兜底（无 IConfiguration 的非 Web 宿主仍可解析 IOptions）。</para>
        /// <para>缓存：注册 <see cref="IMemoryCache"/>（TryAddSingleton），消费方可覆盖为分布式缓存等实现。</para>
        /// <para>V0.3.0（领域自治根治）：注册形态由 <c>TryAddScoped&lt;ISettingManager, SettingManager&gt;</c> 改为
        /// <c>AddConstructibleService&lt;ISettingManager, SettingManager&gt;</c>——接口构造工厂（CurrentAopUser 守卫）+
        /// 实现类 throw-factory。旧形态构造注入 <see cref="IDomainUser"/> 而 IDomainUser 永不注册 DI（D01）→
        /// 运行时解析必失败（v0.3.3 故障根因）；消费方统一经 <c>User.Use&lt;ISettingManager&gt;()</c> 解析
        /// （AOP 路径先设 CurrentAopUser 再 GetRequiredService，工厂据此显式传入 DomainUser）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0：Options 默认值注册（SG1 [Options] 特性已在消费方自动绑定 TKWF:Settings 节；此处兜底默认值）
            services.AddOptions<SettingsOptions>();

            // V0.2.0：内存缓存（TryAddSingleton：IMemoryCache 是 Singleton 生命周期，消费方可覆盖）
            services.TryAddSingleton<IMemoryCache, MemoryCache>();

            // V0.3.0（领域自治根治）：AddConstructibleService——接口构造工厂（CurrentAopUser 守卫）+ 实现类 throw-factory
            services.AddConstructibleService<ISettingManager, SettingManager>();
        }
    }
}
