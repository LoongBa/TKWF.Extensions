using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典扩展初始化器（V0.2.0，V0.3.0 领域自治根治）——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IDictionaryStore"/> + <see cref="IDictionaryManager"/>（AddConstructibleService）
    /// + <see cref="IMemoryCache"/>（AddMemoryCache，内部 TryAddSingleton）+ <see cref="DataDictionaryOptions"/> 绑定</item>
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
        /// 注册数据字典存储与管理服务（V0.2.0 含缓存 + Options；V0.3.0 领域自治根治）。
        /// <para>V0.3.0（ADR90）：注册形态由 <c>TryAddScoped</c> 改为 <c>AddConstructibleService</c>——
        /// 接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory。旧形态构造注入
        /// <see cref="IDomainUser"/>（永不注册 DI——D01）→ 运行时解析必失败；消费方统一经
        /// <c>User.Use&lt;IDictionaryStore&gt;()</c> / <c>User.Use&lt;IDictionaryManager&gt;()</c> 解析
        /// （AOP 路径先设 CurrentAopUser 再 GetRequiredService，工厂据此显式传入 DomainUser）。</para>
        /// <para>Options（V0.2.0）：<c>AddOptions&lt;DataDictionaryOptions&gt;()</c> 注册配置选项（含默认值）；
        /// appsettings.json 绑定由消费方在自身 <c>ConfigureServices</c> 中执行：
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

            // V0.3.0（领域自治根治，ADR90）：AddConstructibleService——接口可构造守卫工厂（CurrentAopUser 守卫）+
            // 实现类 throw-factory；消费方统一经 User.Use<IDictionaryStore>() / User.Use<IDictionaryManager>() 解析
            services.AddConstructibleService<IDictionaryStore, DictionaryStore>();
            services.AddConstructibleService<IDictionaryManager, DictionaryManager>();
        }
    }
}