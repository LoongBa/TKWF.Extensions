using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 素材库门面（全局共享——无 ChannelId，规则按 MaterialId 引用一个素材多规则复用；设计文档 §3.3.1）。
/// <para>Kind：1=Text（纯文本回复） / 2=News（图文——Title/Description/Url/PicUrl）。</para>
/// <para>删除保护（Oracle P1）：素材被回复规则引用时<b>禁止删除</b>（先查引用——防规则悬空）。</para>
/// <para>消费方：<c>User.Use&lt;IMaterialService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IMaterialService : IDomainService
{
    /// <summary>素材列表（<paramref name="kind"/> 可空 = 全部；1 Text / 2 News）。
    /// <para>REST：<c>GET /api/wechat-ops/materials?kind=</c>。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/materials", RestMethod = "GET")]
    Task<IReadOnlyList<MaterialEntity>> ListAsync(int? kind = null, CancellationToken ct = default);

    /// <summary>保存素材（Id=0 新建 / 已有更新）。
    /// <para>REST：<c>POST /api/wechat-ops/materials</c>（单复杂参数 MaterialEntity → [FromBody]；无敏感字段直通）。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/materials", RestMethod = "POST")]
    Task<long> SaveAsync(MaterialEntity material, CancellationToken ct = default);

    /// <summary>删除素材（被规则引用时抛 <see cref="InvalidOperationException"/>——先查引用）。
    /// <para><c>ExposeRest=false</c>：删除为高操作权限能力（引用保护——防误删），对齐既有 15 端点映射不新增暴露面。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(ExposeRest = false)]
    Task<bool> DeleteAsync(long materialId, CancellationToken ct = default);

    /// <summary>素材引用清单（跨回复规则聚合——欢迎语/关键词/Click 映射/全消息类型/Default；返回引用位置 DTO
    /// 含 SceneType 语义 + 规则 Id——DMP-Lite 5.2 引用反查"引用 N 处"+清单）。
    /// <para><c>ExposeRest=false</c>：引用反查为素材管理内部能力，不生成 REST 端点——对齐既有 15 端点映射。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<IReadOnlyList<MaterialReferenceDto>> GetReferencesAsync(long materialId, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
[GenerateController]
public sealed class MaterialService : DomainServiceBase, IMaterialService
{
    private MaterialEntityDataService? _ds;
    private MaterialEntityDataService Ds => _ds ??= User.Use<MaterialEntityDataService>();
    private ReplyRuleEntityDataService? _ruleDs;
    private ReplyRuleEntityDataService RuleDs => _ruleDs ??= User.Use<ReplyRuleEntityDataService>();

    public MaterialService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialEntity>> ListAsync(int? kind = null, CancellationToken ct = default)
        => await Ds.EntitySelectAsync(
            m => kind == null || m.Kind == kind.Value,
            0, 500,
            q => q.OrderByDescending(m => m.CreateTime),
            ct);

    /// <inheritdoc />
    public async Task<long> SaveAsync(MaterialEntity material, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (string.IsNullOrWhiteSpace(material.Name)) throw new ArgumentException("素材名称必填", nameof(material));
        if (material.Kind is not (1 or 2)) throw new ArgumentException("Kind 无效（1=Text / 2=News）", nameof(material));
        if (material.Kind == 1 && string.IsNullOrWhiteSpace(material.Content)) throw new ArgumentException("文本素材须填 Content", nameof(material));
        if (material.Kind == 2 && (string.IsNullOrWhiteSpace(material.Title) || string.IsNullOrWhiteSpace(material.Url))) throw new ArgumentException("图文素材须填 Title/Url", nameof(material));

        var now = DateTime.UtcNow;
        if (material.Id == 0)
        {
            material.CreateTime = now;
            material.UpdateTime = now;
            var created = await Ds.EntityCreateAsync(material, ct);
            return created.Id;
        }

        var db = await Ds.EntityGetAsync(m => m.Id == material.Id, ct);
        if (db is null) throw new InvalidOperationException($"素材不存在：Id={material.Id}");
        db.Name = material.Name;
        db.Content = material.Content;
        db.Title = material.Title;
        db.Description = material.Description;
        db.Url = material.Url;
        db.PicUrl = material.PicUrl;
        db.Kind = material.Kind;
        db.IsEnabled = material.IsEnabled;
        db.UpdateTime = now;
        await Ds.EntityUpdateAsync(db, ct);
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long materialId, CancellationToken ct = default)
    {
        // 删除保护（Oracle P1）：先查规则引用——被引用禁止删除（防规则悬空）
        var referenced = await RuleDs.EntitySelectAsync(r => r.MaterialId == materialId, 0, 1, null, ct);
        if (referenced.Count > 0)
            throw new InvalidOperationException($"素材被回复规则引用（规则 Id={referenced[0].Id}）——禁止删除；请先移除规则引用或禁用素材");

        var affected = await Ds.EntityDeleteBatchAsync(new[] { materialId }, ct);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialReferenceDto>> GetReferencesAsync(long materialId, CancellationToken ct = default)
    {
        var rules = await RuleDs.EntitySelectAsync(
            r => r.MaterialId == materialId, 0, 1000,
            q => q.OrderBy(r => r.SceneType), ct);
        return rules.Select(r => new MaterialReferenceDto
        {
            MaterialId = materialId,
            ReplyRuleId = r.Id,
            SceneType = r.SceneType,
            TriggerKey = r.TriggerKey,
            IsEnabled = r.IsEnabled,
        }).ToList();
    }
}