using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.BlobStoring;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件管理扩展初始化器——经 <c>[TKWFExtension("FileManagement")]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——Options 绑定（TKWF:FileManagement 节）+ SG1 DataService +
    ///       <see cref="IFileFolderStore"/> + <see cref="IManagedFileStore"/> + <see cref="IFileManager"/>（均 TryAddScoped，消费方自定义优先）</item>
    /// <item><see cref="ConfigureFilters"/>——空实现（v0.1.0 无过滤器）</item>
    /// <item><see cref="InitializeAsync"/>——空实现（v0.1.0 无种子数据）</item>
    /// </list>
    /// <para>依赖：<see cref="IBlobStorageService"/> 经 <see cref="TKWF.Ext.BlobStoring"/> 契约注入（C1/ADR50 L2）——
    /// 由消费方启用 BlobStoring 扩展或自定义实现提供；FileManagement <b>不</b>引用 BlobStoring 实现项目。</para>
    /// <para>V4.9.85 起发现不自动启用——消费方须在自身领域初始化器上标注
    /// <c>[TKWFEnabledExtension(typeof(FileManagementExtensionInitializer&lt;&gt;))]</c> 白名单声明，三钩子才执行。</para>
    /// </summary>
    [TKWFExtension("FileManagement")]
    public class FileManagementExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "FileManagement";

        /// <summary>扩展描述。</summary>
        public override string Description => "文件管理扩展——目录树（FileFolder 物化路径）+ 文件元数据（ManagedFile SHA256/去重）+ 上传/下载/删除/重命名/移动门面（安全校验链 + BlobStoring 契约委托物理存储）";

        /// <summary>
        /// 注册 Options + DataService + Store + Manager。
        /// <para>DataService 显式注册（<c>TryAddScoped</c>）——Store/Manager 构造依赖在消费方 DI 中确定性可解析
        /// （扩展 DataService 为 internal，消费方无法自行注册）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. Options 绑定（TKWF:FileManagement 节——AllowedExtensions/AllowAnyExtension/MaxFileSizeBytes/Deduplicate/DefaultPageSize）
            services.AddOptions<FileManagementOptions>().BindConfiguration("TKWF:FileManagement");

            // 2. SG1 DataService（Store/Manager 构造依赖；显式注册保证 DI 可解析）
            services.TryAddScoped<FileFolderEntityDataService>();
            services.TryAddScoped<ManagedFileEntityDataService>();

            // 3. 存储（委托 DataService——数据访问红线合规，不注入 IFreeSql/IEntityDAC）
            services.TryAddScoped<IFileFolderStore, FileFolderStore>();
            services.TryAddScoped<IManagedFileStore, ManagedFileStore>();

            // 4. 管理门面（业务规则 + ITransactionManager 事务包裹）——工厂注册：
            //    FileManager 构造函数 internal（Store 为 internal 契约），
            //    TryAddScoped 工厂语义与类型注册等价（消费方自定义实现仍优先）。
            //    IBlobStorageService 经 sp.GetRequiredService 解析（Abstractions 契约——消费方须启用 BlobStoring 或自定义实现）。
            services.TryAddScoped<IFileManager>(sp => new FileManager(
                sp.GetRequiredService<IFileFolderStore>(),
                sp.GetRequiredService<IManagedFileStore>(),
                sp.GetRequiredService<IBlobStorageService>(),
                sp.GetRequiredService<ITransactionManager>(),
                sp.GetRequiredService<IOptions<FileManagementOptions>>(),
                sp.GetRequiredService<ILogger<FileManager>>()));
        }

        /// <summary>过滤器不注册（v0.1.0 无过滤器）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现
        }

        /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
        public override Task InitializeAsync() => Task.CompletedTask;
    }
}
