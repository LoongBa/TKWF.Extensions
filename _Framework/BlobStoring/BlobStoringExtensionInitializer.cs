using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 二进制存储扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IBlobStorageService"/>（TryAddScoped 接线型）+
    ///      <see cref="IBlobRecordStore"/>（AddConstructibleService 领域服务门面）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("BlobStoring")]
    public class BlobStoringExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "BlobStoring";

        /// <summary>扩展描述。</summary>
        public override string Description => "二进制存储扩展——本地文件系统 Blob 存储与记录持久化";

        /// <summary>
        /// 注册 Blob 存储与记录存储服务。
        /// <para>Options 绑定（C2 评审修复）：<see cref="BlobStoringOptions"/> 经
        /// <c>AddOptions&lt;BlobStoringOptions&gt;().BindConfiguration("TKWF:BlobStoring")</c>
        /// 从配置节绑定（RootPath/IsEnabled），不再依赖宿主隐式默认值。</para>
        /// <para>二态注册（V4.10.53 ADR90 领域自治根治）：
        /// <list type="bullet">
        /// <item><see cref="IBlobRecordStore"/>（领域服务门面，接口标 <c>: IDomainService</c> 勿改）——
        ///     <c>AddConstructibleService&lt;IBlobRecordStore, BlobRecordStore&gt;()</c>：接口可构造守卫工厂
        ///     （CurrentAopUser 守卫——域作用域外解析即抛）+ 实现类 throw-factory（禁直接 DI 解析）；
        ///     消费方统一经 <c>User.Use&lt;IBlobRecordStore&gt;()</c> 解析（AOP 路径）。旧 TryAddScoped 构造注入
        ///     IDomainUser 而 IDomainUser 永不注册 DI → 生产解析必失败（v0.3.3 同根缺陷）。</item>
        /// <item><see cref="IBlobStorageService"/>（<c>BlobStoring.Abstractions</c> 契约非 IDomainService——
        ///     勿改契约；LocalStorageService 纯文件系统实现、无 IDomainUser/DataService 依赖）——
        ///     <strong>接线型基础设施</strong>：保持 <c>TryAddScoped</c> 普通 DI（无 User 依赖，不走 AddConstructibleService）；
        ///     消费方可自定义实现优先，扩展默认实现不覆盖消费方。</item>
        /// </list></para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 绑定（C2 评审修复）：TKWF:BlobStoring 配置节 → BlobStoringOptions
            services.AddOptions<BlobStoringOptions>().BindConfiguration("TKWF:BlobStoring");

            // 接线型基础设施（Abstractions 契约非 IDomainService、无用户上下文依赖）：TryAddScoped 普通 DI
            services.TryAddScoped<IBlobStorageService, LocalStorageService>();
            // 领域服务门面（IBlobRecordStore : IDomainService）：AddConstructibleService——接口守卫工厂 + 实现 throw-factory
            services.AddConstructibleService<IBlobRecordStore, BlobRecordStore>();
        }
    }
}
