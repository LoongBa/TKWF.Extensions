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
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="ISettingStore"/> + <see cref="ISettingManager"/>（TryAddScoped）+
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
        /// 注册设置存储与管理服务。
        /// <para>TryAddScoped：消费方可自定义 <see cref="ISettingStore"/> / <see cref="ISettingManager"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// <para>Options 绑定（V0.2.0）：<see cref="SettingsOptions"/> 标注 <see cref="OptionsAttribute"/>（<c>"TKWF:Settings"</c> 节）——
        /// SG1 在消费方生成 <c>GeneratedOptionsBindings</c>，宿主启动期经 <c>RegisterOptionsBindings</c> 自动
        /// <c>services.Configure&lt;SettingsOptions&gt;(configuration.GetSection("TKWF:Settings"))</c>（与 Navigation/Permissions 同模式）。
        /// 此处 <c>AddOptions</c> 仅注册默认值兜底（无 IConfiguration 的非 Web 宿主仍可解析 IOptions）。</para>
        /// <para>缓存：注册 <see cref="IMemoryCache"/>（TryAddSingleton），消费方可覆盖为分布式缓存等实现。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0：Options 默认值注册（SG1 [Options] 特性已在消费方自动绑定 TKWF:Settings 节；此处兜底默认值）
            services.AddOptions<SettingsOptions>();

            // V0.2.0：内存缓存（TryAddSingleton：IMemoryCache 是 Singleton 生命周期，消费方可覆盖）
            services.TryAddSingleton<IMemoryCache, MemoryCache>();

            services.TryAddScoped<ISettingStore, FreeSqlSettingStore>();
            services.TryAddScoped<ISettingManager, SettingManager>();
        }
    }
}
