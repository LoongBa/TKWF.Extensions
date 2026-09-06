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
    /// <item><see cref="ConfigureServices"/>——注册 ImportService/ExportService + MiniExcel Provider +
    ///       <see cref="IDataImportTaskService"/> + <see cref="DataPortOptions"/> Options 绑定（TKWF:DataPort）</item>
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
        /// <para>TryAddScoped/TryAddSingleton：消费方可自定义实现，扩展默认实现不覆盖消费方。</para>
        /// <para>Options 绑定：扩展注册 <see cref="DataPortOptions"/> 并绑定 TKWF:DataPort 配置节——消费方可在
        /// appsettings.json 中配置默认批次大小/失败策略/默认 Provider。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 绑定：TKWF:DataPort 配置节 + 默认值
            services.AddOptions<DataPortOptions>()
                .BindConfiguration("TKWF:DataPort");

            // 核心服务（TryAddScoped：消费方可自定义覆盖）
            services.TryAddScoped<ImportService>();
            services.TryAddScoped<IImportService>(sp => sp.GetRequiredService<ImportService>());
            services.TryAddScoped<ExportService>();
            services.TryAddScoped<IExportService>(sp => sp.GetRequiredService<ExportService>());

            // MiniExcel Provider（TryAddSingleton：无状态）
            services.TryAddSingleton<MiniExcelImportProvider>();
            services.TryAddSingleton<IImportProvider>(sp => sp.GetRequiredService<MiniExcelImportProvider>());
            services.TryAddSingleton<MiniExcelExportProvider>();
            services.TryAddSingleton<IExportProvider>(sp => sp.GetRequiredService<MiniExcelExportProvider>());

            // 导入任务服务（TryAddScoped：消费方可自定义覆盖）
            services.TryAddScoped<IDataImportTaskService, DataImportTaskService>();
        }
    }
}