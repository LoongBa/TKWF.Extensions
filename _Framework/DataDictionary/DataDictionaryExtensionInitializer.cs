using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典扩展初始化器（V0.2.0）——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IDictionaryStore"/> + <see cref="IDictionaryManager"/>（TryAddScoped）
    /// + <see cref="IMemoryCache"/>（TryAddSingleton）+ <see cref="DataDictionaryOptions"/> 绑定</item>
    /// <item>ConfigureFilters——不调用（V0.2.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.2.0 无种子）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("DataDictionary")]
    public class DataDictionaryExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "DataDictionary";

        /// <summary>扩展描述。</summary>
        public override string Description => "数据字典扩展——字典定义与字典项集中管理、按编码查询、内存缓存、树形分组（V0.2.0）";

        /// <summary>
        /// 注册数据字典存储与管理服务（V0.2.0 含缓存 + Options）。
        /// <para>TryAddScoped：消费方可自定义 <see cref="IDictionaryStore"/> / <see cref="IDictionaryManager"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// <para>Options（V0.2.0）：<c>AddOptions&lt;DataDictionaryOptions&gt;()</c> 注册配置选项（含默认值）；
        /// 基类 <see cref="ConfigureServices(IServiceCollection)"/> 签名仅接收 <c>IServiceCollection</c>（不含 <c>IConfiguration</c>），
        /// 因此 appsettings.json 绑定由消费方在自身 <c>ConfigureServices</c> 中执行：
        /// <c>services.Configure&lt;DataDictionaryOptions&gt;(config.GetSection("TKWF:DataDictionary"))</c>。</para>
        /// <para>IMemoryCache（V0.2.0）：<c>AddMemoryCache()</c> 内部为 TryAddSingleton 语义——
        /// 消费方已注册自定义缓存（如分布式缓存）时不覆盖。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // V0.2.0：Options 注册（默认值），消费方可通过 services.Configure 从 appsettings.json 覆盖
            services.AddOptions<DataDictionaryOptions>();

            // V0.2.0：内存缓存（D8：TryAddSingleton 语义，消费方已注册则不覆盖）
            services.AddMemoryCache();

            // V0.1.0 存储与管理注册
            services.TryAddScoped<IDictionaryStore, FreeSqlDictionaryStore>();
            services.TryAddScoped<IDictionaryManager, DictionaryManager>();
        }
    }
}