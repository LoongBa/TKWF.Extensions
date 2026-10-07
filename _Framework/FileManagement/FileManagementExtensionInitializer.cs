using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
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
    ///       <see cref="IFileFolderStore"/> + <see cref="IManagedFileStore"/> + <see cref="IManagedFileVersionStore"/>（均 TryAddScoped，
    ///       internal 接线型普通 DI——消费方自定义优先）+ <see cref="IFileManager"/>（AddConstructibleService 门面注册）</item>
    /// <item><see cref="ConfigureFilters"/>——空实现（v0.1.0 无过滤器）</item>
    /// <item><see cref="InitializeAsync"/>——空实现（v0.1.0 无种子数据）</item>
    /// </list>
    /// <para>依赖：<see cref="IBlobStorageService"/> 经 <see cref="TKWF.Ext.BlobStoring"/> 契约注入（C1/ADR50 L2）——
    /// 由消费方启用 BlobStoring 扩展或自定义实现提供；FileManagement <b>不</b>引用 BlobStoring 实现项目。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）三态注册：</para>
    /// <list type="bullet">
    /// <item><b>门面（AddConstructibleService）</b>——<see cref="IFileManager"/>（接口 : IDomainService）：接口可构造守卫工厂
    ///     （CurrentAopUser 守卫）+ 实现类 throw-factory；实现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>（经基类 <c>User</c>
    ///     取上下文——<b>IDomainUser 永不注册 DI</b>，旧工厂 lambda <c>sp.GetRequiredService&lt;IDomainUser&gt;()</c> 生产解析必失败）+ 
    ///     <c>[DiContractIgnore]</c> 豁免 DI001；消费方统一经 <c>User.Use&lt;IFileManager&gt;()</c> 解析。</item>
    /// <item><b>内部接线型（TryAddScoped 普通 DI）</b>——三 Store：接口 internal（FileManager 内部组合依赖，不可改可见性），
    ///     实现 ctor(<see cref="System.IServiceProvider"/>) + C1 延迟解析 DataService——普通 DI 可构造
    ///     （FileManager 守卫工厂经 ActivatorUtilities 解析 Store 时无 IDomainUser 依赖）。</item>
    /// </list>
    /// <para>V4.9.85 起发现不自动启用——消费方须在自身领域初始化器上标注
    /// <c>[TKWFEnabledExtension(typeof(FileManagementExtensionInitializer&lt;&gt;))]</c> 白名单声明，三钩子才执行。</para>
    /// </summary>
    [TKWFExtension("FileManagement")]
    [TKWFExtensionCapability(ServiceType = typeof(IFileManager), QuerySurface = "FullIQueryable")]
    [TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.BlobStoring.IBlobStorageService), MinVersion = "0.1.1")]
    public class FileManagementExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "FileManagement";

        /// <summary>扩展描述。</summary>
        public override string Description => "文件管理扩展——目录树（FileFolder 物化路径）+ 文件元数据（ManagedFile SHA256/去重）+ 上传/下载/删除/重命名/移动门面（安全校验链 + BlobStoring 契约委托物理存储）";

        /// <summary>
        /// 注册 Options + Store + Manager（SG1 DataService 经 v4.10.8 ADR61 基类类型判定 + 消费方聚合自动注册）。
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. Options 绑定（TKWF:FileManagement 节——AllowedExtensions/AllowAnyExtension/MaxFileSizeBytes/Deduplicate/DefaultPageSize）
            services.AddOptions<FileManagementOptions>().BindConfiguration("TKWF:FileManagement");

            // 2. SG1 DataService——ADR61 起自动注册（throw-factory），不再手动 TryAddScoped

            // 3. 存储（内部接线型 TryAddScoped 普通 DI——委托 DataService，数据访问红线合规，不注入 IFreeSql/IEntityDAC）：
            //    V4.10.53（ADR90）：实现 ctor 改 (IServiceProvider)——C1 延迟解析 DataService（GetRequiredService），
            //    修复旧 ctor(IDomainUser) 在 FileManager 工厂解析 Store 时 IDomainUser 永不注册（D01）致生产解析失败。
            services.TryAddScoped<IFileFolderStore, FileFolderStore>();
            services.TryAddScoped<IManagedFileStore, ManagedFileStore>();
            services.TryAddScoped<IManagedFileVersionStore, ManagedFileVersionStore>();   // V0.2.0：版本 Store

            // 4. 管理门面（V4.10.53 领域自治根治）——AddConstructibleService：接口可构造守卫工厂（CurrentAopUser 守卫）
            //    + 实现类 throw-factory；消费方经 User.Use<IFileManager>() 解析（AOP 路径）。
            //    旧 TryAddScoped 工厂 lambda（sp.GetRequiredService<IDomainUser> 注入 + internal ctor）生产解析必失败
            //    （IDomainUser 永不注册 DI——D01，v0.3.3 同根缺陷）；改后 ctor public + IDomainUser 首位经基类 User。
            //    其余参数（3 Store + IBlobStorageService + ITransactionManager + IOptions + ILogger）由守卫工厂
            //    ActivatorUtilities 从 DI 解析：Store 接线型普通注册可解析；IBlobStorageService 由消费方启用
            //    BlobStoring 扩展（TryAddScoped<IBlobStorageService, LocalStorageService>）或自定义实现提供
            //    （FileManagement 不注册——C1/ADR50 L2 依赖倒置，不引实现项目）。
            services.AddConstructibleService<IFileManager, FileManager>();
        }

        /// <summary>过滤器不注册（v0.1.0 无过滤器）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现
        }

        /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
        public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
    }
}
