using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IDictionaryStore"/> + <see cref="IDictionaryManager"/>（TryAddScoped）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("DataDictionary")]
    public class DataDictionaryExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "DataDictionary";

        /// <summary>扩展描述。</summary>
        public override string Description => "数据字典扩展——字典定义与字典项集中管理与按编码查询";

        /// <summary>
        /// 注册数据字典存储与管理服务。
        /// <para>TryAddScoped：消费方可自定义 <see cref="IDictionaryStore"/> / <see cref="IDictionaryManager"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IDictionaryStore, FreeSqlDictionaryStore>();
            services.TryAddScoped<IDictionaryManager, DictionaryManager>();
        }
    }
}