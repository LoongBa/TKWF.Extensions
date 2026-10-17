using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号消息网关门面（V0.2.0 增网关模块——方案 §十·五 ① 独立网关组件）。
/// <para>职责：路由规则/下游端点 CRUD + 网关状态 + <b>转发编排核心 <see cref="RouteAsync"/></b>
/// （幂等检查最前 → 路由规则匹配（运行库 matcher）→ 转发决策（运行库 decider）→ 异步抄送/降级链）——
/// <see cref="WeChatOperationsReplyHandler"/> 各可回复分支首行统一调用。</para>
/// <para>铁律（tkwf-extension）：接口 <c>: IDomainService</c> + 实现 <c>internal sealed : DomainServiceBase</c>
/// + <c>[DiContractIgnore]</c> + <c>AddConstructibleService</c> 注册（守卫工厂帧内供给 ctor IDomainUser）；
/// DataService 经基类 <c>User.Use&lt;具体 DataService&gt;()</c> 懒加载（DI004 零豁免）；运行库类型纯逻辑
/// 零持久化——静态方法直用（<c>WeChatMessageRouteRuleMatcher.Match</c>/<c>WeChatMessageForwardDecider.DecideAsync</c>/
/// <c>WeChatMessageIdempotencyKeyBuilder.BuildKey</c>）。</para>
/// <para>⚠️ 默认开关关零回归：<see cref="WeChatMessageGatewayOptions.GatewayEnabled"/> 默认 false——
/// <see cref="RouteAsync"/> 首行判定直接返回未接管（不查库不转发），既有 67 用例全绿保持。</para>
/// <para>✅ SyncProxy/PreprocessSync（同步透传）V0.3.0 落地（方案 §十·二 P0-1 选 A——平台库 RawXml 就绪后补）：
/// 命中经 <see cref="DownstreamForwardClient.SendRawXmlAsync"/> 出站透传（<c>__decrypted_xml</c> 明文 XML 原样 POST 下游）
/// → 下游完整响应 XML 包装 <c>WechatReplyPayload.RawXml</c> <b>原样返回微信</b>（RawXml 逃逸口防双重信封）；超时/失败/非 XML
/// → 运行库 <see cref="WeChatCircuitBreaker"/> 失败计数（开闸期直接降级不转发）→ 转发日志 Timeout/Fail → decider
/// <c>syncSucceeded=false</c> LocalFallback 本地渲染链。PreprocessSync 本期与 SyncProxy 同语义（透传+降级），
/// 前置处理（本地渲染结果作载荷前缀）差异归后续迭代。</para>
/// </summary>
public interface IMessageGatewayService : IDomainService
{
    /// <summary>路由规则列表（按 ChannelId 过滤；<paramref name="msgType"/>/<paramref name="eventType"/> 可空 = 全部）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<MessageRouteRuleEntity>> ListRulesAsync(string channelId, string? msgType = null, string? eventType = null, CancellationToken ct = default);

    /// <summary>保存路由规则（Id=0 新建 + <b>完全重复同键检测</b>——ChannelId+MsgType+Event+MatchMode+TriggerKey 全同 → 更新既有；已有更新）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<long> SaveRuleAsync(MessageRouteRuleEntity rule, CancellationToken ct = default);

    /// <summary>删除路由规则（物理删）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default);

    /// <summary>下游端点列表（按 ChannelId 过滤；DTO 投影<b>不含 AuthToken 密文</b>——Oracle P1 安全）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<DownstreamEndpointDto>> ListEndpointsAsync(string channelId, CancellationToken ct = default);

    /// <summary>保存下游端点——新（Id=0）创建 / 已有更新；<paramref name="authTokenPlain"/> 为<b>明文</b>
    /// （null = 保持库中既有密文不变）——门面经 keyed <see cref="ISymmetricKeyProvider"/> AES-GCM 加密落库（明文永不落库）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<long> SaveEndpointAsync(DownstreamEndpointEntity entity, string? authTokenPlain = null, CancellationToken ct = default);

    /// <summary>删除下游端点（<b>被路由规则引用禁止删除</b>——引用保护；物理删）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<bool> DeleteEndpointAsync(long id, CancellationToken ct = default);

    /// <summary>网关状态只读（Enabled/规则数/下游数——无敏感明细；方案 §十·六 ③）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<WeChatGatewayStatusDto> GetGatewayStatusAsync(CancellationToken ct = default);

    /// <summary>网关全局开关（进程内运行时覆盖——重启回落 <see cref="WeChatMessageGatewayOptions.GatewayEnabled"/> 配置值；
    /// 对齐 <c>WeChatApiClient</c> 纯内存缓存先例，不落库）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task SetGatewayEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// <b>转发编排核心</b>（handler 挂载调用点——方案 §十·五 ①）：幂等检查（最前短路）→ 路由规则匹配
    /// （运行库 matcher——实体投影 descriptor + <see cref="RouteRuleCriteria"/>）→ 转发决策（运行库 decider——
    /// ForwardMode 分支：LocalOnly 本地 / AsyncCopy 异步抄送 / <b>SyncProxy/PreprocessSync 真实出站透传
    /// （V0.3.0——成功 RawXml 原样接管回复；失败降级本地渲染链）</b>）→ 返回
    /// <see cref="GatewayRouteResult"/>。非运营端点（不加 [RequirePermission]——handler 帧内调用）。
    /// </summary>
    /// <param name="ctx">微信回调上下文（平台库 <see cref="WechatEventContext"/>——MsgId/CreateTime/RawXml 幂等与转发数据源）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>网关决策结果（<see cref="GatewayRouteResult"/>——Handled=true 由网关接管本次回复（幂等跳过 /
    /// SyncProxy 透传结果 RawXml 原样返回）；Handled=false handler 继续本地渲染链（LocalOnly/AsyncCopy 本地回复 /
    /// NoRule 默认兜底 / SyncProxy 透传失败降级）。</returns>
    Task<GatewayRouteResult> RouteAsync(WechatEventContext ctx, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
internal sealed class MessageGatewayService : DomainServiceBase, IMessageGatewayService
{
    /// <summary>进程内网关开关运行时覆盖（null = 未覆盖，用 <see cref="WeChatMessageGatewayOptions.GatewayEnabled"/> 配置值；
    /// 进程重启即清零——对齐 <c>WeChatApiClient</c> 纯内存缓存先例）。</summary>
    private static bool? _gatewayEnabledOverride;

    private readonly ISymmetricKeyProvider _keys;
    private readonly WeChatMessageGatewayOptions _options;
    private readonly WeChatCircuitBreaker _breaker;
    private readonly DownstreamForwardClient _client;
    private readonly ILogger? _logger;

    private MessageRouteRuleEntityDataService? _ruleDs;
    private MessageRouteRuleEntityDataService RuleDs => _ruleDs ??= User.Use<MessageRouteRuleEntityDataService>();
    private DownstreamEndpointEntityDataService? _epDs;
    private DownstreamEndpointEntityDataService EpDs => _epDs ??= User.Use<DownstreamEndpointEntityDataService>();
    private MessageForwardLogEntityDataService? _logDs;
    private MessageForwardLogEntityDataService LogDs => _logDs ??= User.Use<MessageForwardLogEntityDataService>();
    private WeChatMessageIdempotencyEntityDataService? _idemDs;
    private WeChatMessageIdempotencyEntityDataService IdemDs => _idemDs ??= User.Use<WeChatMessageIdempotencyEntityDataService>();

    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；keys 经 keyed DI（复用
    /// <see cref="WeChatOperationsKeyProviderKeys.WeChatOperations"/> 键——同扩展同信任边界，方案 §十·六 ③）；
    /// 熔断器（运行库 <see cref="WeChatCircuitBreaker"/>——进程内三态单例，每 EndpointId 独立）/ 出站客户端
    /// （typed client——<see cref="DownstreamForwardClient"/> DI 生命周期托管）/ options/logger 从 DI 解析——
    /// 非域基础设施，零 DataService 构造注入（DI004）。</summary>
    public MessageGatewayService(
        IDomainUser user,
        [FromKeyedServices(WeChatOperationsKeyProviderKeys.WeChatOperations)]
        ISymmetricKeyProvider keys,
        WeChatCircuitBreaker breaker,
        DownstreamForwardClient client,
        IOptions<WeChatMessageGatewayOptions>? gatewayOptions = null,
        ILogger? logger = null) : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _breaker = breaker ?? throw new ArgumentNullException(nameof(breaker));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = gatewayOptions?.Value ?? new WeChatMessageGatewayOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageRouteRuleEntity>> ListRulesAsync(string channelId, string? msgType = null, string? eventType = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Array.Empty<MessageRouteRuleEntity>();
        return await RuleDs.EntitySelectAsync(
            r => r.ChannelId == channelId
                && (msgType == null || r.MsgType == msgType)
                && (eventType == null || r.Event == eventType),
            0, 500,
            q => q.OrderBy(r => r.SortOrder).ThenBy(r => r.Id),
            ct);
    }

    /// <inheritdoc />
    public async Task<long> SaveRuleAsync(MessageRouteRuleEntity rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.ChannelId)) throw new ArgumentException("ChannelId 必填", nameof(rule));
        if (string.IsNullOrWhiteSpace(rule.Name)) throw new ArgumentException("Name 必填（规则显示名——DMP-Lite 需求 5.5）", nameof(rule));
        if (rule.ForwardMode is < 1 or > 4) throw new ArgumentException("ForwardMode 无效（1=LocalOnly/2=AsyncCopy/3=SyncProxy/4=PreprocessSync）", nameof(rule));
        if (rule.MatchMode is < 0 or > 2) throw new ArgumentException("MatchMode 无效（0=None/1=Exact/2=Contains）", nameof(rule));
        if (rule.MatchMode != WeChatMessageMatchModes.None && string.IsNullOrWhiteSpace(rule.TriggerKey))
            throw new ArgumentException("MatchMode≠None 须填 TriggerKey", nameof(rule));
        if (rule.ForwardMode != WeChatMessageForwardModes.LocalOnly && rule.EndpointId is null)
            throw new ArgumentException("转发类策略（AsyncCopy/SyncProxy/PreprocessSync）须填 EndpointId", nameof(rule));
        if (rule.EndpointId is { } eid && eid > 0)
        {
            // 端点引用同 channel 校验（规则引用须同 channel——方案 §十·一 ③；跨 channel 拒绝）
            var ep = await EpDs.EntityGetAsync(e => e.Id == eid, ct);
            if (ep is null) throw new InvalidOperationException($"下游端点不存在：Id={eid}");
            if (ep.ChannelId != rule.ChannelId) throw new InvalidOperationException($"下游端点跨 channel 引用拒绝：端点属 ChannelId={ep.ChannelId}，规则属 {rule.ChannelId}");
        }

        var now = DateTime.UtcNow;
        if (rule.Id == 0)
        {
            // 完全重复同键检测（方案 §十·一 ③——同键覆盖更新；⚠️ FreeSql null == null 语义——显式 IS NULL 分支）
            var duplicate = await RuleDs.EntityGetAsync(
                r => r.ChannelId == rule.ChannelId
                    && (r.MsgType == rule.MsgType || (r.MsgType == null && rule.MsgType == null))
                    && (r.Event == rule.Event || (r.Event == null && rule.Event == null))
                    && r.MatchMode == rule.MatchMode
                    && (r.TriggerKey == rule.TriggerKey || (r.TriggerKey == null && rule.TriggerKey == null)),
                ct);
            if (duplicate is not null)
            {
                // 同键覆盖更新（保留原 Id/SortOrder 语义——覆盖转发决策/启用/排序）
                ApplyRuleFields(duplicate, rule);
                duplicate.UpdateTime = now;
                await RuleDs.EntityUpdateAsync(duplicate, ct);
                return duplicate.Id;
            }

            if (rule.SortOrder == 0)
            {
                var siblings = await RuleDs.EntitySelectAsync(
                    r => r.ChannelId == rule.ChannelId, 0, int.MaxValue,
                    q => q.OrderByDescending(r => r.SortOrder), ct);
                rule.SortOrder = (siblings.FirstOrDefault()?.SortOrder ?? 0) + 1;
            }
            rule.CreateTime = now;
            rule.UpdateTime = now;
            var created = await RuleDs.EntityCreateAsync(rule, ct);
            return created.Id;
        }

        var db = await RuleDs.EntityGetAsync(r => r.Id == rule.Id, ct);
        if (db is null) throw new InvalidOperationException($"路由规则不存在：Id={rule.Id}");
        ApplyRuleFields(db, rule);
        db.UpdateTime = now;
        await RuleDs.EntityUpdateAsync(db, ct);
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default)
    {
        var affected = await RuleDs.EntityDeleteBatchAsync(new[] { ruleId }, ct);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DownstreamEndpointDto>> ListEndpointsAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Array.Empty<DownstreamEndpointDto>();
        var entities = await EpDs.EntitySelectAsync(
            e => e.ChannelId == channelId, 0, 500, q => q.OrderBy(e => e.Id), ct);
        var list = new List<DownstreamEndpointDto>(entities.Count);
        foreach (var e in entities) list.Add(ToEndpointDto(e));
        return list;
    }

    /// <inheritdoc />
    public async Task<long> SaveEndpointAsync(DownstreamEndpointEntity entity, string? authTokenPlain = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (string.IsNullOrWhiteSpace(entity.ChannelId)) throw new ArgumentException("ChannelId 必填", nameof(entity));
        if (string.IsNullOrWhiteSpace(entity.Name)) throw new ArgumentException("Name 必填", nameof(entity));
        if (string.IsNullOrWhiteSpace(entity.BaseUrl)) throw new ArgumentException("BaseUrl 必填", nameof(entity));
        if (entity.AuthType is < 0 or > 2) throw new ArgumentException("AuthType 无效（0=None/1=Bearer/2=Header）", nameof(entity));
        if (entity.AuthType != WeChatEndpointAuthTypes.None && string.IsNullOrWhiteSpace(authTokenPlain))
            throw new ArgumentException("鉴权类型非 None 须填 authToken 明文（AuthTokenCipher 密文不可直接提交——门面加密落库）", nameof(authTokenPlain));
        if (entity.TimeoutSeconds <= 0)
            entity.TimeoutSeconds = _options.DefaultSyncTimeoutSeconds;   // 默认 2 秒（微信 5 秒时限留本地兜底余量）
        if (entity.TimeoutSeconds > 3)
            throw new ArgumentException("TimeoutSeconds 上限 3 秒（防运维误配吞掉兜底窗口——方案 §十·二 ② fail-fast）", nameof(entity));

        // ChannelId+Name 冲突预查（唯一索引 UX——TOCTOU 由数据库兜底；方案 §十·一 ③）
        var existing = await EpDs.EntityGetAsync(e => e.ChannelId == entity.ChannelId && e.Name == entity.Name, ct);
        if (existing is not null && (entity.Id == 0 || existing.Id != entity.Id))
            throw new InvalidOperationException($"下游端点冲突：ChannelId+Name 已被占用（Id={existing.Id}）");

        var now = DateTime.UtcNow;
        if (entity.Id == 0)
        {
            // 明文 → AES-GCM 加密落库（null/空 → 空密文——无鉴权端点）
            entity.AuthTokenCipher = string.IsNullOrWhiteSpace(authTokenPlain) ? string.Empty : _keys.Encrypt(authTokenPlain);
            entity.CreateTime = now;
            entity.UpdateTime = now;
            var created = await EpDs.EntityCreateAsync(entity, ct);
            return created.Id;
        }

        // 更新——逐字段（明文非 null = 覆盖密文；null = 保持既有）
        var db = await EpDs.EntityGetAsync(e => e.Id == entity.Id, ct);
        if (db is null) throw new InvalidOperationException($"下游端点不存在：Id={entity.Id}");
        db.ChannelId = entity.ChannelId;
        db.Name = entity.Name;
        db.BaseUrl = entity.BaseUrl;
        db.Path = entity.Path;
        db.HttpMethod = entity.HttpMethod;
        db.TimeoutSeconds = entity.TimeoutSeconds;
        db.IsEnabled = entity.IsEnabled;
        db.AuthType = entity.AuthType;
        if (authTokenPlain is not null) db.AuthTokenCipher = _keys.Encrypt(authTokenPlain);
        db.RetryCount = entity.RetryCount;
        db.Remark = entity.Remark;
        db.UpdateTime = now;
        await EpDs.EntityUpdateAsync(db, ct);
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEndpointAsync(long id, CancellationToken ct = default)
    {
        // 被规则引用禁止删除（引用保护——方案 §十·六 ① ②）
        var referenced = await RuleDs.EntityGetAsync(r => r.EndpointId == id, ct);
        if (referenced is not null)
            throw new InvalidOperationException($"下游端点被路由规则引用（规则 Id={referenced.Id}）——禁止删除");
        var affected = await EpDs.EntityDeleteBatchAsync(new[] { id }, ct);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<WeChatGatewayStatusDto> GetGatewayStatusAsync(CancellationToken ct = default)
    {
        var ruleCount = await RuleDs.CountAsync(null, ct);
        var endpointCount = await EpDs.CountAsync(null, ct);
        return new WeChatGatewayStatusDto(Enabled: _gatewayEnabledOverride ?? _options.GatewayEnabled, ruleCount, endpointCount);
    }

    /// <inheritdoc />
    public Task SetGatewayEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        _gatewayEnabledOverride = enabled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<GatewayRouteResult> RouteAsync(WechatEventContext ctx, CancellationToken ct = default)
    {
        // ① 默认开关关零回归：未启用 → 未接管（不查库不转发——既有 handler 行为零回归，方案 §十·五 ⑤）
        if (!(_gatewayEnabledOverride ?? _options.GatewayEnabled))
            return GatewayRouteResult.NotHandled;

        try
        {
            return await RouteCoreAsync(ctx, ct);
        }
        catch (Exception ex) when (ct.IsCancellationRequested is false)
        {
            // 网关自身异常 fail-safe 降级 LocalOnly（方案 §十·五 ③——不阻断回复：handler 继续本地渲染链；
            //   记日志不抛——微信回调不因网关故障 500/重试）
            _logger?.LogWarning(ex, "微信消息网关异常——降级本地处理（不阻断回复）：ChannelId={ChannelId} MsgType={MsgType} Event={Event}",
                ctx?.ChannelId, ctx?.MsgType, ctx?.Event);
            return GatewayRouteResult.NotHandled;
        }
    }

    /// <summary>路由编排核心（幂等最前短路 → 规则匹配 → 决策 → AsyncCopy 入队/本地/降级；异常由 <see cref="RouteAsync"/> 兜底）。</summary>
    private async Task<GatewayRouteResult> RouteCoreAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 上下文守卫：缺 channel/发送者 → 直通本地（无法构建幂等键/转发报文）
        if (ctx is null || string.IsNullOrWhiteSpace(ctx.ChannelId))
            return GatewayRouteResult.NotHandled;
        var fromUserName = ctx.FromUserName ?? ctx.OpenId ?? string.Empty;
        if (string.IsNullOrEmpty(fromUserName))
            return GatewayRouteResult.NotHandled;

        // ② 幂等检查（最前短路——方案 §十·四 ③）：键构建 → 查 → 命中即重复（判决语义由"键存在"决定）
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey(ctx.ChannelId, ctx.MsgId, fromUserName, ctx.CreateTime ?? 0);
        var existing = await IdemDs.GetByKeyAsync(key, ct);
        if (existing is not null)
        {
            // P1-5：Pending 超时复用键重建（陈旧 Pending 数据丢失窗口兜底——超时后允许新请求重建 Pending+转发）
            if (existing.LastStatus == WeChatMessageIdempotencyStatus.Pending && IsPendingExpired(existing))
            {
                existing.FirstSeenAt = DateTime.UtcNow;
                existing.UpdateTime = DateTime.UtcNow;
                await IdemDs.EntityUpdateAsync(existing, ct);
            }
            else
            {
                // 命中即重复（Pending/Forwarded/Skipped 均跳过）——静默跳过留痕（转发日志 SkippedDuplicate）
                await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.SkippedDuplicate, null, 0, null, null, DateTime.UtcNow), ct);
                return new GatewayRouteResult(ForwardAction.LocalOnly, ForwardDegradeStage.None, null,
                    Handled: true, Result: null, SkippedDuplicate: true);
            }
        }
        else
        {
            // 未命中 → 创建 Pending（并发唯一索引兜底——败者重查见胜者记录 → 按"键已占用"跳过）
            try
            {
                await IdemDs.EntityCreateAsync(new WeChatMessageIdempotencyEntity
                {
                    IdempotencyKey = key,
                    ChannelId = ctx.ChannelId,
                    MsgId = ctx.MsgId,
                    FromUserName = fromUserName,
                    CreateTime = ToDateTime(ctx.CreateTime),
                    FirstSeenAt = DateTime.UtcNow,
                    LastStatus = WeChatMessageIdempotencyStatus.Pending,
                    UpdateTime = DateTime.UtcNow,
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 唯一冲突（并发双建）——败者重查；见胜者 → 同样按"键已占用"跳过（竞态下胜者未及置 Forwarded 亦不重复转发）
                var fresh = await IdemDs.GetByKeyAsync(key, ct);
                if (fresh is null) throw;   // 非冲突异常——原样抛
                await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.SkippedDuplicate, null, 0, null, null, DateTime.UtcNow), ct);
                return new GatewayRouteResult(ForwardAction.LocalOnly, ForwardDegradeStage.None, null,
                    Handled: true, Result: null, SkippedDuplicate: true);
            }
        }

        // ③ 路由规则匹配（运行库 matcher——实体投影 descriptor + RouteRuleCriteria）
        var rules = await RuleDs.GetEnabledRulesAsync(ctx.ChannelId, ct);
        var criteria = new RouteRuleCriteria(ctx.ChannelId, ctx.MsgType, ctx.Event, ctx.Content);
        var matched = WeChatMessageRouteRuleMatcher.Match(rules.Select(ToDescriptor), criteria);

        if (matched is null)
        {
            // 无匹配规则 → 幂等 LastStatus=Skipped（无规则）+ 转发日志 NoRule + 未接管（handler 走本地默认兜底）
            await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Skipped, null, ct);
            await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.NoRule, null, 0, null, null, DateTime.UtcNow), ct);
            return new GatewayRouteResult(ForwardAction.LocalOnly, ForwardDegradeStage.None, null,
                Handled: false, Result: null, SkippedDuplicate: false);
        }

        // ④ 转发决策——SyncProxy/PreprocessSync 真实出站透传（V0.3.0 P0-1 选 A 落地——RawXml 逃逸口就绪）：
        //    出站成功 → decider(syncSucceeded=true) 透传接管（下游 XML 原样返回微信）；超时/失败/非 XML →
        //    decider(false) LocalFallback 降级本地渲染链（方案 §十·二 ③）。其余策略（LocalOnly/AsyncCopy）
        //    decider 直判（syncSucceeded 不参与）。
        if (matched.ForwardMode is WeChatMessageForwardModes.SyncProxy or WeChatMessageForwardModes.PreprocessSync)
            return await RouteSyncProxyAsync(ctx, matched, key, fromUserName, ct);

        var decision = await WeChatMessageForwardDecider.DecideAsync(matched, syncSucceeded: false, ct);

        switch (decision.Action)
        {
            case ForwardAction.AsyncCopy:
                // 本地渲染为主响应 + 消息副本异步入队（副分支——方案 §十·五 ②；幂等 Forwarded 语义=已入队待投递）
                long logId;
                try
                {
                    logId = await User.Use<IMessageForwardDispatcher>().EnqueueAsync(
                        ctx.ChannelId, matched.EndpointId ?? 0, ctx.RawXml ?? string.Empty,
                        ctx.MsgId, fromUserName, ctx.MsgType, matched.Id, matched.ForwardMode, key, ct);
                }
                catch (Exception ex) when (ct.IsCancellationRequested is false)
                {
                    // 入队失败（消费方缺 AddBackgroundJobs 接线等）——fail-safe 降级：本地回复不受影响不 500，
                    //   幂等键仍占用（防微信重推重复转发）；转发日志 Fail 行留痕（运维可对账）
                    _logger?.LogWarning(ex, "微信异步抄送入队失败（本地回复不受影响——fail-safe 降级）：ChannelId={ChannelId} Key={Key}",
                        ctx.ChannelId, key);
                    logId = (await LogDs.EntityCreateAsync(
                        CreateForwardLog(ctx, WeChatMessageForwardLogStatus.Fail, matched.Id, matched.ForwardMode,
                            matched.EndpointId, "异步抄送入队失败（IBackgroundJobManager 未接线？）", DateTime.UtcNow), ct)).Id;
                }
                await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, logId, ct);
                return new GatewayRouteResult(ForwardAction.AsyncCopy, ForwardDegradeStage.None, matched,
                    Handled: false, Result: null, SkippedDuplicate: false);

            case ForwardAction.LocalOnly:
                // 本地处理不转发（幂等 Forwarded——已处理语义，防重复回调重复渲染）
                await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, null, ct);
                return new GatewayRouteResult(ForwardAction.LocalOnly, ForwardDegradeStage.None, matched,
                    Handled: false, Result: null, SkippedDuplicate: false);

            case ForwardAction.LocalFallback:
            default:
                // 未知转发模式 fail-safe 降级 → 本地渲染链（decider 已步进 LocalRender；handler 本地兜底——规则→素材→文本/template/Default）
                await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, null, ct);
                return new GatewayRouteResult(ForwardAction.LocalFallback, decision.DegradeStage, matched,
                    Handled: false, Result: null, SkippedDuplicate: false);
        }
    }

    /// <summary>
    /// SyncProxy/PreprocessSync 出站透传执行（V0.3.0——方案 §十·二 P0-1 选 A 落地）。
    /// <para>编排链：原始报文守卫（RawXml 缺失 → Fail 降级）→ 下游端点解析（同 channel 启用校验）→
    /// 熔断判定（<see cref="WeChatCircuitBreaker"/> 开闸期直接降级不转发）→ AuthToken 解密瞬时 →
    /// <see cref="DownstreamForwardClient.SendRawXmlAsync"/> 出站透传（<c>__decrypted_xml</c> 明文 XML 原样 POST 下游）→
    /// 成功（2xx + 合法微信被动回复 XML）→ <c>WechatReplyPayload(RawXml: 下游XML)</c> 原样接管回复（防双重信封）+
    /// 转发日志 Success（响应截断 500）+ 幂等 Forwarded；失败/超时/非 XML → 熔断失败计数 + 转发日志 Timeout/Fail
    /// （ErrorMsg 脱敏不含凭证）+ 幂等 Forwarded + LocalFallback 降级本地渲染链。</para>
    /// <para>⚠️ PreprocessSync 本期与 SyncProxy 同语义（透传+降级）；前置处理（本地渲染结果作透传载荷前缀）差异归后续迭代。</para>
    /// </summary>
    private async Task<GatewayRouteResult> RouteSyncProxyAsync(
        WechatEventContext ctx, MessageRouteRuleDescriptor matched, string key, string fromUserName, CancellationToken ct)
    {
        var endpointId = matched.EndpointId ?? 0;
        var now = DateTime.UtcNow;

        // ① 原始报文守卫：SyncProxy 透传数据源 = __decrypted_xml 明文 XML（ctx.RawXml）——缺失/空无法透传 → Fail 降级
        var rawXml = ctx.RawXml;
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            _logger?.LogWarning("微信同步透传报文缺失（RawXml null——未解密/未富化）→ 降级本地渲染：ChannelId={ChannelId} Key={Key}",
                ctx.ChannelId, key);
            var log = await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.Fail, matched.Id,
                matched.ForwardMode, endpointId, "同步透传原始报文缺失（RawXml null——未解密/未富化）", now), ct);
            await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, log.Id, ct);
            return new GatewayRouteResult(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, matched,
                Handled: false, Result: null, SkippedDuplicate: false);
        }

        // ② 下游端点解析（同 channel 启用校验——规则引用须同 channel；禁用/缺失 → Fail 降级，对齐异步抄送语义）
        //    （ctx.ChannelId 已在 RouteCoreAsync 顶部守卫非空——此处显式 ! 消解可空流分析）
        var ep = await EpDs.GetEnabledEndpointByIdAsync(endpointId, ctx.ChannelId!, ct);
        if (ep is null)
        {
            _logger?.LogWarning("微信同步透传端点不可用：EndpointId={EndpointId} ChannelId={ChannelId} → 降级本地渲染",
                endpointId, ctx.ChannelId);
            var log = await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.Fail, matched.Id,
                matched.ForwardMode, endpointId, $"下游端点不存在/已禁用（EndpointId={endpointId}）", now), ct);
            await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, log.Id, ct);
            return new GatewayRouteResult(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, matched,
                Handled: false, Result: null, SkippedDuplicate: false);
        }

        // ③ 熔断判定（运行库三态——每 EndpointId 独立；开闸期直接 LocalFallback 不转发，防下游持续故障雪崩）
        var breakerKey = endpointId.ToString();
        if (!_breaker.TryCall(breakerKey, DateTime.UtcNow))
        {
            _logger?.LogWarning("微信同步透传熔断开闸（CircuitBreaker Open——不转发）：EndpointId={EndpointId} ChannelId={ChannelId} → 降级本地渲染",
                endpointId, ctx.ChannelId);
            var log = await LogDs.EntityCreateAsync(CreateForwardLog(ctx, WeChatMessageForwardLogStatus.Fail, matched.Id,
                matched.ForwardMode, endpointId, "熔断开闸（CircuitBreaker Open）——下游连续失败保护，直接降级本地渲染", now), ct);
            await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, log.Id, ct);
            return new GatewayRouteResult(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, matched,
                Handled: false, Result: null, SkippedDuplicate: false);
        }

        // ④ AuthToken AES-GCM 解密（明文仅出站瞬时——不落日志不落转发日志，方案 §十·六 ③）
        var token = string.IsNullOrWhiteSpace(ep.AuthTokenCipher) ? null : _keys.Decrypt(ep.AuthTokenCipher);

        // ⑤ 出站透传（typed client——端点 TimeoutSeconds 硬超时 linked CTS；缺省回落网关默认 2s）
        var result = await _client.SendRawXmlAsync(
            ep.BaseUrl, ep.Path, ep.HttpMethod, ep.AuthType, token, rawXml,
            ep.TimeoutSeconds > 0 ? ep.TimeoutSeconds : _options.DefaultSyncTimeoutSeconds, ct);

        // ⑥ 透传成功：2xx + 下游响应为合法微信被动回复 XML → 原样返回微信（RawXml 逃逸口防双重信封）+ 熔断成功回关
        if (!result.FailedOrTimedOut && result.StatusCode is >= 200 and < 300 && IsValidReplyXml(result.ResponseXml))
        {
            _breaker.RecordSuccess(breakerKey);
            var log = CreateForwardLog(ctx, WeChatMessageForwardLogStatus.Success, matched.Id, matched.ForwardMode, endpointId, null, now);
            log.StatusCode = result.StatusCode;
            log.ResponseBody = DownstreamForwardClient.Truncate(result.ResponseXml, DownstreamForwardClient.ResponseBodyMaxLength);
            log.DurationMs = result.DurationMs;
            log.ForwardedAt = now;
            var created = await LogDs.EntityCreateAsync(log, ct);
            await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, created.Id, ct);
            _logger?.LogInformation("微信同步透传成功接管回复：EndpointId={EndpointId} Key={Key} DurationMs={DurationMs}",
                endpointId, key, result.DurationMs);
            // 动作与命中规则转发模式一致（SyncProxy=3 / PreprocessSync=4——透传语义本期同，动作区分对齐 ForwardAction 值域）
            var action = matched.ForwardMode == WeChatMessageForwardModes.PreprocessSync
                ? ForwardAction.PreprocessSync
                : ForwardAction.SyncProxy;
            return new GatewayRouteResult(action, ForwardDegradeStage.None, matched,
                Handled: true, Result: new WechatReplyPayload(RawXml: result.ResponseXml), SkippedDuplicate: false);
        }

        // ⑦ 降级：超时/失败/非 XML → 熔断失败计数（连续 ≥ 阈值开闸）+ 转发日志 Timeout/Fail + LocalFallback 本地渲染链
        _breaker.RecordFailure(breakerKey);
        var status = result.TimedOut ? WeChatMessageForwardLogStatus.Timeout : WeChatMessageForwardLogStatus.Fail;
        var errorMsg = result.TimedOut
            ? $"下游转发超时（TimeoutSeconds={ep.TimeoutSeconds}，DurationMs={result.DurationMs}）"
            : result.FailedOrTimedOut
                ? $"下游转发失败（StatusCode={result.StatusCode}，DurationMs={result.DurationMs}）"
                : result.StatusCode is >= 200 and < 300
                    ? $"下游响应非合法微信被动回复 XML（StatusCode={result.StatusCode}）"
                    : $"下游转发 HTTP 失败（StatusCode={result.StatusCode}）";
        var failLog = CreateForwardLog(ctx, status, matched.Id, matched.ForwardMode, endpointId, errorMsg, now);
        failLog.StatusCode = result.StatusCode;
        failLog.DurationMs = result.DurationMs;
        failLog.ResponseBody = result.FailedOrTimedOut
            ? null
            : DownstreamForwardClient.Truncate(result.ResponseXml, DownstreamForwardClient.ResponseBodyMaxLength);
        failLog.ForwardedAt = now;
        var createdFail = await LogDs.EntityCreateAsync(failLog, ct);
        await FinalizeIdempotencyAsync(key, WeChatMessageIdempotencyStatus.Forwarded, createdFail.Id, ct);
        _logger?.LogWarning("微信同步透传失败 → 降级本地渲染：EndpointId={EndpointId} Key={Key} Status={Status} Error={Error}",
            endpointId, key, status, errorMsg);
        return new GatewayRouteResult(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, matched,
            Handled: false, Result: null, SkippedDuplicate: false);
    }

    // ─────────────────────────── 内部 ───────────────────────────

    /// <summary>Pending 超时判定（PendingTimeoutMinutes=0/负数 = 不超时复用，恒视为有效占用）。</summary>
    private bool IsPendingExpired(WeChatMessageIdempotencyEntity e)
        => _options.PendingTimeoutMinutes > 0
            && DateTime.UtcNow - e.FirstSeenAt > TimeSpan.FromMinutes(_options.PendingTimeoutMinutes);

    /// <summary>幂等键终态迁移（Forwarded/Skipped——更新 LastStatus + ForwardLogId 关联）。</summary>
    private async Task FinalizeIdempotencyAsync(string key, int lastStatus, long? logId, CancellationToken ct)
    {
        var entity = await IdemDs.GetByKeyAsync(key, ct);
        if (entity is null) return;
        entity.LastStatus = lastStatus;
        entity.ForwardLogId = logId;
        entity.UpdateTime = DateTime.UtcNow;
        await IdemDs.EntityUpdateAsync(entity, ct);
    }

    /// <summary>转发日志实体构造（全链留痕——方案 §十·四 ⑤ 每次回调一条日志；ErrorMsg 脱敏不含明文凭证）。</summary>
    private static MessageForwardLogEntity CreateForwardLog(
        WechatEventContext ctx, int status, long? matchRuleId, int forwardMode, long? endpointId, string? errorMsg, DateTime now)
        => new()
        {
            ChannelId = ctx.ChannelId,
            MsgId = ctx.MsgId,
            FromUserName = ctx.FromUserName ?? ctx.OpenId ?? string.Empty,
            MessageType = ctx.MsgType ?? string.Empty,
            MatchRuleId = matchRuleId,
            ForwardMode = forwardMode,
            DownstreamEndpointId = endpointId,
            Status = status,
            ErrorMsg = errorMsg,
            CreateTime = now,
            UpdateTime = now,
        };

    /// <summary>微信 Unix 秒时间戳 → DateTime（UTC——微信时间戳语义保留，对齐幂等表 CreateTime 原样留存语义）。</summary>
    private static DateTime ToDateTime(long? unixSeconds)
        => unixSeconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : DateTime.UtcNow;

    /// <summary>下游响应合法性判定（微信被动回复 XML 信封——TrimStart 后 <c>&lt;xml</c> 前缀；空/非 XML → 非合法，
    /// 网关按 Fail 降级本地渲染链——SyncProxy 透传只认微信被动回复 XML 形态）。</summary>
    private static bool IsValidReplyXml(string? xml)
        => !string.IsNullOrWhiteSpace(xml)
            && xml.AsSpan().TrimStart().StartsWith("<xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>规则实体 → 运行库 descriptor 投影（运行库不引用扩展实体——方案 §十·〇 ①）。</summary>
    private static MessageRouteRuleDescriptor ToDescriptor(MessageRouteRuleEntity e)
        => new(
            Id: e.Id,
            ChannelId: e.ChannelId,
            MsgType: e.MsgType,
            Event: e.Event,
            MatchMode: e.MatchMode,
            TriggerKey: e.TriggerKey,
            ForwardMode: e.ForwardMode,
            EndpointId: e.EndpointId,
            SortOrder: e.SortOrder,
            IsEnabled: e.IsEnabled);

    /// <summary>规则字段应用（新建/更新共用——保留 Id/CreateTime/SortOrder 语义）。</summary>
    private static void ApplyRuleFields(MessageRouteRuleEntity db, MessageRouteRuleEntity src)
    {
        db.ChannelId = src.ChannelId;
        db.Name = src.Name;
        db.MsgType = src.MsgType;
        db.Event = src.Event;
        db.MatchMode = src.MatchMode;
        db.TriggerKey = src.TriggerKey;
        db.ForwardMode = src.ForwardMode;
        db.EndpointId = src.EndpointId;
        db.IsEnabled = src.IsEnabled;
        db.SortOrder = src.SortOrder;
    }

    /// <summary>端点 DTO 投影——不含 AuthTokenCipher 密文（Oracle P1 安全：密文不回显）。</summary>
    private static DownstreamEndpointDto ToEndpointDto(DownstreamEndpointEntity e)
        => new(
            Id: e.Id,
            ChannelId: e.ChannelId,
            Name: e.Name,
            BaseUrl: e.BaseUrl,
            Path: e.Path,
            HttpMethod: e.HttpMethod,
            TimeoutSeconds: e.TimeoutSeconds,
            IsEnabled: e.IsEnabled,
            AuthType: e.AuthType,
            HasCredentials: !string.IsNullOrWhiteSpace(e.AuthTokenCipher),
            RetryCount: e.RetryCount,
            Remark: e.Remark,
            CreateTime: e.CreateTime,
            UpdateTime: e.UpdateTime);
}

/// <summary>
/// 网关路由决策结果（<see cref="IMessageGatewayService.RouteAsync"/> 返回——handler 挂载调用点消费；
/// 方案 §十·五 ①）。<see cref="Handled"/> = true 时本次回复已由网关接管（幂等跳过——Result=null 静默跳过）；
/// false 时 handler 继续本地渲染链。
/// </summary>
/// <param name="Action">决策动作（<see cref="ForwardAction"/>——LocalOnly/AsyncCopy/SyncProxy 透传接管/LocalFallback 降级）。</param>
/// <param name="DegradeStage">降级链阶段（<see cref="ForwardDegradeStage"/>——LocalFallback 时指示回退到哪一级；否则 None）。</param>
/// <param name="MatchedRule">命中规则描述（运行库 <see cref="MessageRouteRuleDescriptor"/>；无命中/幂等跳过为 null）。</param>
/// <param name="Handled">是否已由网关接管本次回复（true=幂等跳过短路——handler 直接 return Result；false=继续本地链）。</param>
/// <param name="Result">网关接管时返回的回复载荷（幂等跳过 null——静默跳过不回复）。</param>
/// <param name="SkippedDuplicate">是否幂等跳过（重复回调留痕——审计对账）。</param>
public sealed record GatewayRouteResult(
    ForwardAction Action,
    ForwardDegradeStage DegradeStage,
    MessageRouteRuleDescriptor? MatchedRule,
    bool Handled,
    WechatReplyPayload? Result,
    bool SkippedDuplicate)
{
    /// <summary>未接管（默认开关关 / 上下文不可路由——handler 原样本地处理；零回归路径）。</summary>
    public static GatewayRouteResult NotHandled { get; } =
        new(ForwardAction.LocalOnly, ForwardDegradeStage.None, null, Handled: false, Result: null, SkippedDuplicate: false);
}

/// <summary>
/// 下游端点投影 DTO（<b>不含 AuthToken 密文</b>——Oracle P1 DTO 投影安全：AuthToken 明文与密文均不回显；
/// <see cref="HasCredentials"/> 仅指示是否已配置鉴权令牌；方案 §十·六 ③）。</summary>
/// <param name="Id">主键。</param>
/// <param name="ChannelId">公众号隔离 id。</param>
/// <param name="Name">端点名称（channel 内唯一）。</param>
/// <param name="BaseUrl">基础 URL（出站目标主机）。</param>
/// <param name="Path">路径（可空）。</param>
/// <param name="HttpMethod">HTTP 方法（默认 POST）。</param>
/// <param name="TimeoutSeconds">超时秒数（1..3）。</param>
/// <param name="IsEnabled">启用。</param>
/// <param name="AuthType">鉴权类型（0=None/1=Bearer/2=Header）。</param>
/// <param name="HasCredentials">是否已配置鉴权令牌（AuthTokenCipher 非空）。</param>
/// <param name="RetryCount">失败重试次数（可空）。</param>
/// <param name="Remark">备注（可空）。</param>
/// <param name="CreateTime">创建时间（UTC）。</param>
/// <param name="UpdateTime">更新时间（UTC）。</param>
public sealed record DownstreamEndpointDto(
    long Id,
    string ChannelId,
    string Name,
    string BaseUrl,
    string? Path,
    string HttpMethod,
    int TimeoutSeconds,
    bool IsEnabled,
    int AuthType,
    bool HasCredentials,
    int? RetryCount,
    string? Remark,
    DateTime CreateTime,
    DateTime UpdateTime)
{
    /// <summary>
    /// 超时毫秒（派生值 = <see cref="TimeoutSeconds"/> × 1000——<b>非存储列不落库</b>）。
    /// <para>前端表单/展示用毫秒（DMP-Lite 5.5"超时毫秒"表单字段——消费方前端应展示/提交
    /// <see cref="TimeoutMs"/>，服务端按秒契约存储）；领域契约仍为秒（<see cref="TimeoutSeconds"/> 1..3，
    /// 微信 5 秒硬时限留本地兜底窗口——上限 3 秒防运维误配吞掉兜底余量）。</para>
    /// </summary>
    public int TimeoutMs => TimeoutSeconds * 1000;
}

/// <summary>网关状态 DTO（只读——Enabled/规则数/下游数；不含规则/端点明细，方案 §十·六 ③）。</summary>
/// <param name="Enabled">网关当前有效开关（运行时覆盖 ?? 配置值）。</param>
/// <param name="RuleCount">路由规则总数（全 channel）。</param>
/// <param name="EndpointCount">下游端点总数（全 channel）。</param>
public sealed record WeChatGatewayStatusDto(bool Enabled, long RuleCount, long EndpointCount);
