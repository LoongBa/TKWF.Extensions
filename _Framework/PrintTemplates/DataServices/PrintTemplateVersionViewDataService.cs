using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.PrintTemplates;
using TKWF.Ext.PrintTemplates.DTOs;

namespace TKWF.Ext.PrintTemplates;

/// <summary>
/// 打印模板版本视图只读 DataService（VEntity V0.2.0）——跨表 JOIN 单查询下推 DB，
/// 替代"先 GetByKeyAsync 取 TemplateId 再查版本"两步查询（2 往返 → 1 往返）。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（红线）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：REST 经 ITemplateManager 门面暴露；
/// 敏感视图（含模板正文 Content）GraphQL 显式关闭（C4，ExposeGraphqlQuery = false）。</para>
/// </summary>
partial class PrintTemplateVersionViewDataService(IDomainUser user, IEntityReadOnlyDAC<PrintTemplateVersionView> dac)
    : DomainReadOnlyDataServiceBase<PrintTemplateVersionView, PrintTemplateVersionViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>单查询跨表：按 Key + Version 精确查询（替代两步：GetByKeyAsync + GetVersionAsync）。</summary>
    public async Task<PrintTemplateVersionView?> GetVersionByKeyAsync(string key, string version, CancellationToken ct = default)
    {
        var list = await SelectAsync(v => v, predicate: v => v.Key == key && v.Version == version, limit: 1, ct: ct);
        return list.FirstOrDefault();
    }

    /// <summary>单查询跨表：按 Key 查当前 Active 版本（替代两步，状态过滤下推 DB）。</summary>
    public async Task<PrintTemplateVersionView?> GetActiveVersionByKeyAsync(string key, CancellationToken ct = default)
    {
        var list = await SelectAsync(v => v, predicate: v => v.Key == key && v.Status == PrintTemplateVersionStatus.Active, limit: 1, ct: ct);
        return list.FirstOrDefault();
    }

    /// <summary>单查询跨表：按 Key 列出所有版本（替代两步，按版本号倒序——最新优先）。</summary>
    public async Task<List<PrintTemplateVersionView>> ListVersionsByKeyAsync(string key, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.Key == key, orderBy: q => q.OrderByDescending(v => v.Version), ct: ct);
}