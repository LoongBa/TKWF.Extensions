using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 二进制存储扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IBlobStorageService"/> + <see cref="IBlobRecordStore"/>（TryAddScoped）</item>
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
        /// <para>TryAddScoped：消费方可自定义 <see cref="IBlobStorageService"/> / <see cref="IBlobRecordStore"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.TryAddScoped<IBlobStorageService, LocalStorageService>();
            services.TryAddScoped<IBlobRecordStore, FreeSqlBlobRecordStore>();
        }
    }
}
