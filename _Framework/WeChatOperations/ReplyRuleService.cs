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
/// 回复规则门面（全消息类型回复配置——通用规则表，设计文档 §3.3.2/§3.3.3）。
/// <para>SceneType（int）：1=Subscribe 欢迎语 / 2=Keyword 关键词 / 3=Click 菜单 key 映射 / 4=Image / 5=Voice /
/// 6=Video / 7=ShortVideo / 8=Location / 9=Link / 10=Scan 扫码 / 11=Default 兜底。</para>
/// <para>⚠️ Click 映射独立于菜单结构（SceneType=Click 规则修改<b>不触发</b> menu/create——Oracle P1 语义保留）。</para>
/// <para>消费方：<c>User.Use&lt;IReplyRuleService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IReplyRuleService : IDomainService
{
    /// <summary>规则列表（按 ChannelId 过滤；<paramref name="sceneType"/> 可空 = 全部场景；IsEnabled 降序 + SortOrder 升序）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<ReplyRuleEntity>> ListRulesAsync(string channelId, int? sceneType = null, CancellationToken ct = default);

    /// <summary>保存规则（Id=0 新建 / 已有更新）——新建默认 SortOrder=同场景末尾 max+1。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<long> SaveRuleAsync(ReplyRuleEntity rule, CancellationToken ct = default);

    /// <summary>删除规则（物理删）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default);

    /// <summary>关注欢迎语规则（SceneType=Subscribe——单条；风格最受欢迎的 SortOrder 最小）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<ReplyRuleEntity?> GetWelcomeAsync(string channelId, CancellationToken ct = default);

    /// <summary>默认兜底规则（SceneType=Default——未命中关键词/场景时用）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<ReplyRuleEntity?> GetDefaultAsync(string channelId, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
internal sealed class ReplyRuleService : DomainServiceBase, IReplyRuleService
{
    private ReplyRuleEntityDataService? _ds;
    private ReplyRuleEntityDataService Ds => _ds ??= User.Use<ReplyRuleEntityDataService>();

    public ReplyRuleService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplyRuleEntity>> ListRulesAsync(string channelId, int? sceneType = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Array.Empty<ReplyRuleEntity>();
        return await Ds.EntitySelectAsync(
            r => r.ChannelId == channelId && (sceneType == null || r.SceneType == sceneType.Value),
            0, 200,
            q => q.OrderByDescending(r => r.IsEnabled).ThenBy(r => r.SortOrder).ThenBy(r => r.Id),
            ct);
    }

    /// <inheritdoc />
    public async Task<long> SaveRuleAsync(ReplyRuleEntity rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.ChannelId)) throw new ArgumentException("ChannelId 必填", nameof(rule));
        if (rule.SceneType is < 1 or > 11) throw new ArgumentException("SceneType 无效（1..11）", nameof(rule));
        if (rule.SceneType == 2 && string.IsNullOrWhiteSpace(rule.TriggerKey)) throw new ArgumentException("Keyword 场景须填 TriggerKey", nameof(rule));
        if (rule.SceneType == 3 && string.IsNullOrWhiteSpace(rule.TriggerKey)) throw new ArgumentException("Click 场景须填 TriggerKey（菜单 key）", nameof(rule));

        var now = DateTime.UtcNow;
        if (rule.Id == 0)
        {
            if (rule.SortOrder == 0)
            {
                var siblings = await Ds.EntitySelectAsync(r => r.ChannelId == rule.ChannelId && r.SceneType == rule.SceneType,
                    0, int.MaxValue, q => q.OrderByDescending(r => r.SortOrder), ct);
                rule.SortOrder = (siblings.FirstOrDefault()?.SortOrder ?? 0) + 1;
            }
            rule.CreateTime = now;
            rule.UpdateTime = now;
            var created = await Ds.EntityCreateAsync(rule, ct);
            return created.Id;
        }

        var db = await Ds.EntityGetAsync(r => r.Id == rule.Id, ct);
        if (db is null) throw new InvalidOperationException($"回复规则不存在：Id={rule.Id}");
        db.ChannelId = rule.ChannelId;
        db.SceneType = rule.SceneType;
        db.TriggerKey = rule.TriggerKey;
        db.MatchMode = rule.MatchMode;
        db.MaterialId = rule.MaterialId;
        db.ReplyText = rule.ReplyText;
        db.IsEnabled = rule.IsEnabled;
        db.SortOrder = rule.SortOrder;
        db.UpdateTime = now;
        await Ds.EntityUpdateAsync(db, ct);
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default)
    {
        var affected = await Ds.EntityDeleteBatchAsync(new[] { ruleId }, ct);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<ReplyRuleEntity?> GetWelcomeAsync(string channelId, CancellationToken ct = default)
        => await Ds.EntityGetAsync(r => r.ChannelId == channelId && r.SceneType == 1 && r.IsEnabled, ct);

    /// <inheritdoc />
    public async Task<ReplyRuleEntity?> GetDefaultAsync(string channelId, CancellationToken ct = default)
        => await Ds.EntityGetAsync(r => r.ChannelId == channelId && r.SceneType == 11 && r.IsEnabled, ct);
}

/// <summary>回复规则场景类型常量（对齐实体 SceneType 值域——1..11，见 <see cref="ReplyRuleEntity"/>）。</summary>
public static class WeChatReplySceneTypes
{
    public const int Subscribe = 1;
    public const int Keyword = 2;
    public const int Click = 3;
    public const int Image = 4;
    public const int Voice = 5;
    public const int Video = 6;
    public const int ShortVideo = 7;
    public const int Location = 8;
    public const int Link = 9;
    public const int Scan = 10;
    public const int Default = 11;
}