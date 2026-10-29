using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 运营配置只读查询（<see cref="IOperationsConfigQuery"/> 的 WeChatOperations DB 实现——D03 1.10 方案 §10.3）。
/// <para>DB 权威：每域单行配置表（Id=1）读取，无行 = 未配置 → 返回 null（消费方 Options 静态降级兜底）。</para>
/// <para>注册：<see cref="WeChatOperationsExtensionInitializer"/> <c>AddConstructibleService</c>——
/// 契约在 FA（<c>TKWF.Ext.Federation</c> 命名空间）、实现在本扩展（di-constructible 守卫工厂）。
/// 消费方（Federation 编排链等）经 <c>User.Use&lt;IOperationsConfigQuery&gt;()</c> 帧内解析——未装配本扩展 → 守卫失败 → 消费方先行 GetService 判空降级。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WeChatOperationsConfigQuery : DomainServiceBase, IOperationsConfigQuery
{
    private ArticleReplyOpsConfigEntityDataService? _articleReplyDs;
    private ArticleReplyOpsConfigEntityDataService ArticleReplyDs
        => _articleReplyDs ??= User.Use<ArticleReplyOpsConfigEntityDataService>();

    private ReplyTextOpsConfigEntityDataService? _replyTextDs;
    private ReplyTextOpsConfigEntityDataService ReplyTextDs
        => _replyTextDs ??= User.Use<ReplyTextOpsConfigEntityDataService>();

    private DegradeOpsConfigEntityDataService? _degradeDs;
    private DegradeOpsConfigEntityDataService DegradeDs
        => _degradeDs ??= User.Use<DegradeOpsConfigEntityDataService>();

    private TicketOpsConfigEntityDataService? _ticketDs;
    private TicketOpsConfigEntityDataService TicketDs
        => _ticketDs ??= User.Use<TicketOpsConfigEntityDataService>();

    public WeChatOperationsConfigQuery(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<string?> GetArticleReplyUrlTemplateAsync(CancellationToken ct = default)
    {
        var entity = await ArticleReplyDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity?.UrlTemplate;
    }

    /// <inheritdoc />
    public async Task<ReplyTextConfigDto?> GetReplyTextConfigAsync(CancellationToken ct = default)
    {
        var entity = await ReplyTextDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null
            ? null
            : new ReplyTextConfigDto(entity.DisabledChannelReplyTemplate, entity.DefaultReplyText, entity.Version);
    }

    /// <inheritdoc />
    public async Task<bool?> GetUnknownChannelReplyEnabledAsync(CancellationToken ct = default)
    {
        var entity = await DegradeDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity?.UnknownChannelReplyEnabled;
    }

    /// <inheritdoc />
    public async Task<TicketConfigDto?> GetTicketConfigAsync(CancellationToken ct = default)
    {
        var entity = await TicketDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null
            ? null
            : new TicketConfigDto(entity.Mode, entity.TtlSeconds, entity.Version);
    }
}