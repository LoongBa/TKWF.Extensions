using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板管理门面——版本生命周期（Draft→Active→Archived）+ 渲染入口 + 发布版本自动递增。
    /// <para>Scoped 生命周期（按请求）。</para>
    /// </summary>
    public interface ITemplateManager
    {
        /// <summary>按模板键查询模板。</summary>
        Task<PrintTemplateEntity?> GetTemplateAsync(string key, CancellationToken ct = default);

        /// <summary>按模板键 + 版本号查询版本（精确版本，审计用）。</summary>
        Task<PrintTemplateVersionEntity?> GetVersionAsync(string key, string version, CancellationToken ct = default);

        /// <summary>按模板键查询当前 Active 版本（最新激活）。</summary>
        Task<PrintTemplateVersionEntity?> GetActiveVersionAsync(string key, CancellationToken ct = default);

        /// <summary>按模板键列出所有版本（按版本号倒序）。</summary>
        Task<IReadOnlyList<PrintTemplateVersionEntity>> ListVersionsAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// 渲染：version=null → 最新 Active；version 非空 → 精确固定（审计用）。
        /// <para>模板缺失或版本未找到 → 抛异常。</para>
        /// </summary>
        Task<string> RenderAsync(string key, IReadOnlyDictionary<string, object?> model, string? version = null, CancellationToken ct = default);

        /// <summary>
        /// 发布新版本：Key 不存在时自动建 PrintTemplate 行（Name=Key，C2）；自动 minor 递增（首版 1.0.0）；新 Active → 旧 Active 自动 Archived。
        /// <para>发布失败显式抛异常（C1）。</para>
        /// </summary>
        Task<PrintTemplateVersionEntity> PublishAsync(string key, string content, string? description = null, CancellationToken ct = default);

        /// <summary>
        /// 存/更新草稿版本（每模板一个 Draft，upsert）；Draft 不占 Active 槽位；Publish 不提升 Draft（C3）。
        /// </summary>
        Task<PrintTemplateVersionEntity> DraftAsync(string key, string content, string? description = null, CancellationToken ct = default);

        /// <summary>归档版本（Archived——可追溯渲染，不可再发布）。</summary>
        Task ArchiveAsync(string key, string version, CancellationToken ct = default);
    }
}
