using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="ISettingStore"/> + <see cref="ISettingManager"/>（TryAddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
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
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<ISettingStore, FreeSqlSettingStore>();
            services.TryAddScoped<ISettingManager, SettingManager>();
        }
    }
}
