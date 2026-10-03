using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.DataPort;
using TKW.Framework.Utility.DataPort.Providers.MiniExcel;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// DataPort 扩展初始化器——经 <c>[TKWFExtension]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 ImportService/ExportService + MiniExcel Provider（接线型普通 DI）+
    ///       <see cref="IDataImportTaskService"/>（V4.10.53 AddConstructibleService 门面）+ <see cref="DataPortOptions"/> Options 绑定（TKWF:DataPort）</item>
    /// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
    /// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("DataPort")]
    public class DataPortExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "DataPort";

        /// <summary>扩展描述。</summary>
        public override string Description => "数据导入导出（三层架构——核心运行库 + MiniExcel Provider + 扩展模块带持久化）";

        /// <summary>
        /// 注册 DataPort 服务。
        /// <para>二态注册（V4.10.53 领域自治根治，ADR90，正确路线）：</para>
        /// <list type="bullet">
        /// <item><b>接线型（TryAddScoped/TryAddSingleton 普通 DI）</b>——核心运行库（<see cref="ImportService"/>/<see cref="ExportService"/>）
        ///     与 MiniExcel Provider（<see cref="MiniExcelImportProvider"/>/<see cref="MiniExcelExportProvider"/>）为框架类型、
        ///     零用户上下文依赖（ctor 仅 IImportProvider/IExportProvider）——不继承基类，注册保持（消费方可自定义覆盖）。</item>
        /// <item><b>门面（AddConstructibleService）</b>——<see cref="IDataImportTaskService"/>（接口 : IDomainService）：
        ///     接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；实现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>
        ///     （经基类 <c>User</c> 取上下文——<b>IDomainUser 永不注册 DI</b>，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）。
        ///     消费方统一经 <c>User.Use&lt;IDataImportTaskService&gt;()</c> 解析。</item>
        /// </list>
        /// <para>Options 绑定：扩展注册 <see cref="DataPortOptions"/> 并绑定 TKWF:DataPort 配置节——消费方可在
        /// appsettings.json 中配置默认批次大小/失败策略/默认 Provider。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 绑定：TKWF:DataPort 配置节 + 默认值
            services.AddOptions<DataPortOptions>()
                .BindConfiguration("TKWF:DataPort");

            // 核心服务（接线型 TryAddScoped：框架类型 ImportService/ExportService——零用户上下文，消费方可自定义覆盖）
            services.TryAddScoped<ImportService>();
            services.TryAddScoped<IImportService>(sp => sp.GetRequiredService<ImportService>());
            services.TryAddScoped<ExportService>();
            services.TryAddScoped<IExportService>(sp => sp.GetRequiredService<ExportService>());

            // MiniExcel Provider（接线型 TryAddSingleton：无状态）
            services.TryAddSingleton<MiniExcelImportProvider>();
            services.TryAddSingleton<IImportProvider>(sp => sp.GetRequiredService<MiniExcelImportProvider>());
            services.TryAddSingleton<MiniExcelExportProvider>();
            services.TryAddSingleton<IExportProvider>(sp => sp.GetRequiredService<MiniExcelExportProvider>());

            // 任务门面（V4.10.53 领域自治根治，ADR90）：AddConstructibleService——接口可构造守卫工厂
            // （CurrentAopUser 守卫）+ 实现类 throw-factory；消费方经 User.Use<IDataImportTaskService>() 解析（AOP 路径）。
            // 旧 TryAddScoped<IDataImportTaskService, DataImportTaskService> 构造注入 IDomainUser（永不注册 DI）生产解析必失败。
            // 实现继承 DomainServiceBase（经基类 User 取上下文）+ [DiContractIgnore] 豁免 DI001（运行时手写注册）。
            // 数据访问红线（2026-09-07）：实现委托 SG1 DataService，禁裸 IFreeSql。
            services.AddConstructibleService<IDataImportTaskService, DataImportTaskService>();
        }
    }
}