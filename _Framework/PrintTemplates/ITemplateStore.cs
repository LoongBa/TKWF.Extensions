using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板存储抽象——模板/版本 CRUD + 按 Key/Key@Version/Active 查询。
    /// <para>异常传播：所有存储层异常向上传递（审计关键，不静默）。</para>
    /// </summary>
    public interface ITemplateStore
    {
        /// <summary>按模板键查询模板。</summary>
        Task<PrintTemplateEntity?> GetByKeyAsync(string key, CancellationToken ct = default);

        /// <summary>新增或更新模板（Idempotent Upsert）。</summary>
        Task UpsertTemplateAsync(PrintTemplateEntity template, CancellationToken ct = default);

        /// <summary>按模板 ID + 版本号查询版本。</summary>
        Task<PrintTemplateVersionEntity?> GetVersionAsync(long templateId, string version, CancellationToken ct = default);

        /// <summary>按模板 ID 查询当前 Active 版本。</summary>
        Task<PrintTemplateVersionEntity?> GetActiveVersionAsync(long templateId, CancellationToken ct = default);

        /// <summary>按模板 ID 列出所有版本（按版本号倒序）。</summary>
        Task<IReadOnlyList<PrintTemplateVersionEntity>> ListVersionsAsync(long templateId, CancellationToken ct = default);

        /// <summary>新增或更新版本（Idempotent Upsert，TemplateId+Version 唯一约束）。</summary>
        Task UpsertVersionAsync(PrintTemplateVersionEntity version, CancellationToken ct = default);
    }
}
