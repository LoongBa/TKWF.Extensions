using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// FreeSql 模板存储实现——模板/版本 CRUD + 按 Key/Key@Version/Active 查询。
    /// <para>异常传播：所有存储层异常向上传递（审计关键，不静默）。</para>
    /// </summary>
    internal sealed class FreeSqlTemplateStore : ITemplateStore
    {
        private readonly IFreeSql _freeSql;

        public FreeSqlTemplateStore(IFreeSql freeSql)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
        }

        /// <inheritdoc />
        public async Task<PrintTemplateEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
        {
            return await _freeSql
                .Select<PrintTemplateEntity>()
                .Where(t => t.Key == key)
                .FirstAsync(ct);
        }

        /// <inheritdoc />
        public async Task UpsertTemplateAsync(PrintTemplateEntity template, CancellationToken ct = default)
        {
            await _freeSql
                .InsertOrUpdate<PrintTemplateEntity>()
                .SetSource(template)
                .ExecuteAffrowsAsync(ct);
        }

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetVersionAsync(long templateId, string version, CancellationToken ct = default)
        {
            return await _freeSql
                .Select<PrintTemplateVersionEntity>()
                .Where(v => v.TemplateId == templateId && v.Version == version)
                .FirstAsync(ct);
        }

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetActiveVersionAsync(long templateId, CancellationToken ct = default)
        {
            return await _freeSql
                .Select<PrintTemplateVersionEntity>()
                .Where(v => v.TemplateId == templateId && v.Status == PrintTemplateVersionStatus.Active)
                .FirstAsync(ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<PrintTemplateVersionEntity>> ListVersionsAsync(long templateId, CancellationToken ct = default)
        {
            return await _freeSql
                .Select<PrintTemplateVersionEntity>()
                .Where(v => v.TemplateId == templateId)
                .OrderByDescending(v => v.Version)
                .ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task UpsertVersionAsync(PrintTemplateVersionEntity version, CancellationToken ct = default)
        {
            await _freeSql
                .InsertOrUpdate<PrintTemplateVersionEntity>()
                .SetSource(version)
                .ExecuteAffrowsAsync(ct);
        }
    }
}
