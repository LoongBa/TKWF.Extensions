using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板存储实现——经 <see cref="PrintTemplateEntityDataService"/> + <see cref="PrintTemplateVersionEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：
    /// 扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常传播：所有存储层异常向上传递（模板是审计关键资产，不静默）——
    /// 区别于 Settings/BlobStoring/Emailing 的异常静默模式。</para>
    /// </summary>
    internal sealed class TemplateStore : ITemplateStore
    {
        private readonly PrintTemplateEntityDataService _templateDataService;
        private readonly PrintTemplateVersionEntityDataService _versionDataService;

        public TemplateStore(
            PrintTemplateEntityDataService templateDataService,
            PrintTemplateVersionEntityDataService versionDataService)
        {
            _templateDataService = templateDataService ?? throw new ArgumentNullException(nameof(templateDataService));
            _versionDataService = versionDataService ?? throw new ArgumentNullException(nameof(versionDataService));
        }

        /// <inheritdoc />
        public async Task<PrintTemplateEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
            => await _templateDataService.GetByKeyAsync(key, ct);

        /// <inheritdoc />
        public async Task UpsertTemplateAsync(PrintTemplateEntity template, CancellationToken ct = default)
            => await _templateDataService.UpsertByKeyAsync(template, ct);

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetVersionAsync(long templateId, string version, CancellationToken ct = default)
            => await _versionDataService.GetByTemplateAndVersionAsync(templateId, version, ct);

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetActiveVersionAsync(long templateId, CancellationToken ct = default)
            => await _versionDataService.GetActiveByTemplateAsync(templateId, ct);

        /// <inheritdoc />
        public async Task<IReadOnlyList<PrintTemplateVersionEntity>> ListVersionsAsync(long templateId, CancellationToken ct = default)
            => await _versionDataService.ListVersionsByTemplateAsync(templateId, ct);

        /// <inheritdoc />
        public async Task UpsertVersionAsync(PrintTemplateVersionEntity version, CancellationToken ct = default)
            => await _versionDataService.UpsertVersionAsync(version, ct);
    }
}