using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Federation.WeChat;

using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营事件回复 handler（继承平台库 <see cref="TKWF.Federation.WeChat.WechatEventReplyHandlerBase"/>
/// 二级分发——全消息类型回复配置 + 交互日志；设计文档 §3.3.3 Oracle P0 选项 A：<b>内部组合 template 兜底</b>——
/// DB 规则未命中时直接读 <c>Channel.Extra["news_reply"]</c> 模板返回，不依赖编排器迭代顺序，消费方无需额外注册
/// <see cref="TKWF.Federation.WeChat.WechatTemplateEventReplyHandler"/>）。
/// <para>渲染三层：规则层（DB 按 (ChannelId, SceneType) 过滤）→ 素材层（DB 内容库）→ 渲染层
/// （<c>BuildTextReply</c>/<c>BuildNewsReply</c> 信封闭包——本 handler 内部组合）。</para>
/// <para>分发映射（全消息类型）：Subscribe（欢迎语）/ Text（Keyword 完全→包含→Default 兜底）/ Click（TriggerKey=EventKey）/
/// Image/Voice/Video/ShortVideo/Location/Link（对应 SceneType）/ Scan（场景规则）/ View/ViewMiniProgram（null 微信直跳）/
/// Unsubscribe（回复被协议门控吞掉）。</para>
/// <para>每次回复记 <see cref="InteractionLogEntity"/>（ChannelId/OpenId/MsgType/Content/TriggerType/ReplyContent/
/// ReplyMaterialId/耗时 <c>DurationMs</c>（V0.3.0）/匹配过程追踪 <c>MatchTraceJson</c>（V0.3.1——逐行推演"为什么命中/未命中"，
/// 供 DMP-Lite 5.6 §4 日志详情"匹配过程"灰框）——运维排查）——best-effort（try/catch 不阻塞回复）；<b>I2-10：订阅/取关
/// 统计写路径移除</b>——关注状态记录归编排层 <see cref="TKWF.Ext.Federation.IFollowStateService.RecordAsync"/>（方案 §5.1
/// 第 6 步记录在后），handler 只保留交互日志（业务审计），订阅统计读取经 <see cref="ISubscriptionStatService"/> 委托关注域。</para>
/// <para>注册（本扩展 Initializer）：<c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler,
/// WeChatOperationsReplyHandler&gt;()</c>——ADR92 集合版守卫工厂（帧内供给 IDomainUser；ctor 仅
/// IDomainUser + ILogger + IOptions，零 DataService 构造注入——DI004）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeChatOperationsReplyHandler : TKWF.Federation.WeChat.WechatEventReplyHandlerBase
{
    private readonly WeChatOperationsOptions _options;
    private readonly WeChatMessageGatewayOptions _gatewayOptions;
    private readonly ILogger? _logger;

    /// <summary>本次消息处理计时（V0.3.0 耗时字段——DMP-Lite 5.6 交互日志"耗时 ms"）：Handler 实例随请求
    /// 作用域创建（守卫工厂/NoAop 直建均为每次调用新实例）——<b>构造即入口起</b>，<see cref="RecordInteractionAsync"/>
    /// 记录交互日志时止（<c>ElapsedMilliseconds</c> 写入 <see cref="InteractionLogEntity.DurationMs"/>）。</summary>
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    private ReplyRuleEntityDataService? _ruleDs;
    private ReplyRuleEntityDataService RuleDs => _ruleDs ??= User.Use<ReplyRuleEntityDataService>();
    private MaterialEntityDataService? _matDs;
    private MaterialEntityDataService MatDs => _matDs ??= User.Use<MaterialEntityDataService>();
    private InteractionLogEntityDataService? _logDs;
    private InteractionLogEntityDataService LogDs => _logDs ??= User.Use<InteractionLogEntityDataService>();
    private IMessageGatewayService? _gateway;
    private IMessageGatewayService Gateway => _gateway ??= User.Use<IMessageGatewayService>();

    /// <summary>消息业务总开关读源（V0.4.0 DB 化——FeatureManagement 消费：<c>WeChatOperations.MessageBusiness</c>
    /// feature 布尔判定，fail-closed（未定义/解析失败 → false）；Options <see cref="WeChatOperationsOptions.MessageBusinessEnabled"/>
    /// 保留为<b>启动期默认值</b>语义（FeatureDefinition DefaultValue="true" 对齐），运行时切换经
    /// FeatureManagement 管理 API 写 DB（版本号缓存失效 + 分布式事件）。DI004 合规——门面经基类
    /// <c>User.Use&lt;IFeatureManager&gt;()</c> 懒加载（AOP 帧内解析，不构造注入）。</summary>
    private TKWF.Ext.FeatureManagement.IFeatureManager? _featureManager;
    private TKWF.Ext.FeatureManagement.IFeatureManager FeatureManager => _featureManager ??= User.Use<TKWF.Ext.FeatureManagement.IFeatureManager>();

    /// <summary>
    /// 消息业务总开关判定（V0.4.0 起 FeatureManagement DB 化 + Options 启动语义双源——十处分支统一入口）。
    /// <para>语义（DMP-Lite 5.8——关闭后本地回复停发统一维护提示；转发网关已先行不受控）：
    /// ① <see cref="WeChatOperationsOptions.MessageBusinessEnabled"/> 显式 <c>false</c>（启动期关闭）→ 关闭；
    /// ② Options <c>true</c>（默认）→ 运行时经 FeatureManagement DB 权威：<c>WeChatOperations.MessageBusiness</c>
    /// 存值覆盖（DB 写 <c>SetValueAsync</c> → 运行时可切换）；<b>未存值 → 默认开（"true"——业务语义默认开放，
    /// 与 V0.2.1 Options 默认一致；勿用 IsEnabledAsync——其对未定义/未存值恒 fail-closed false，不读 DefaultValue）</b>。
    /// fail-closed：FeatureManager 域外解析 / 读源异常 → 关闭（维护提示兜底，不静默放开）。</para>
    /// </summary>
    private async Task<bool> IsMessageBusinessEnabledAsync(CancellationToken ct)
    {
        // ① 启动期 Options 显式关闭 → 关闭（保留 V0.2.1 配置语义；测试经 ConfigureOptions 可控制）
        if (!_options.MessageBusinessEnabled) return false;
        // ② Options 默认开 → 运行时 DB 权威（GetValueAsync 显式默认 "true"——未存值默认开 = 业务默认开放语义）
        try
        {
            var raw = await FeatureManager.GetValueAsync(
                WeChatOperationsFeatureNames.MessageBusiness, User, defaultValue: "true", ct);
            return bool.TryParse(raw, out var enabled) && enabled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 总开关读源异常——fail-closed（停发本地回复；不吞掉回复路径抛异常：维护提示兜底）
            _logger?.LogWarning(ex, "消息业务总开关 DB 读源异常——fail-closed 停发本地回复（维护提示兜底）");
            return false;
        }
    }

    /// <summary>本次消息匹配过程逐行追踪（P2-2——DMP-Lite 5.6 §4 日志详情"匹配过程"灰框逐行推演）：Handler 实例
    /// 随请求作用域创建（每次调用新实例）——<b>实例生命周期 = 单次回复</b>，无需清理；进
    /// <see cref="RecordInteractionAsync"/> 序列化为 <see cref="InteractionLogEntity.MatchTraceJson"/>
    /// （best-effort——仅旁路采集，不影响回复决策）。</summary>
    private readonly List<MatchTraceStep> _trace = new();

    /// <summary>
    /// 匹配过程追踪单步（序列化进 <see cref="InteractionLogEntity.MatchTraceJson"/>——JSON 数组，PascalCase 属性名，
    /// 消费方前端逐行渲染"为什么命中/未命中"）。每步记录：规则 id + 规则名（如有）、场景类型、匹配模式、触发键、
    /// 命中与否（bool）、简短原因（如 "TriggerKey==content"/"content contains key"/"无 Default 规则"/"template fallback"）。
    /// </summary>
    /// <param name="ruleId">命中/评估的规则主键（无规则分支为 null）。</param>
    /// <param name="ruleName">规则显示名（<see cref="ReplyRuleEntity"/> 暂无 Name——恒 null，字段保留供前端渲染）。</param>
    /// <param name="sceneType">规则场景类型（<see cref="WeChatReplySceneTypes"/>；template 兜底/无规则分支为 null）。</param>
    /// <param name="matchMode">匹配模式（1=Exact 完全 / 2=Contains 包含；场景型规则 null）。</param>
    /// <param name="triggerKey">本次评估的触发键（规则 TriggerKey，或 Exact 分支的内容/Scan 场景值）。</param>
    /// <param name="hit">是否命中。</param>
    /// <param name="reason">简短原因（展示文案）。</param>
    private sealed record MatchTraceStep(
        long? RuleId,
        string? RuleName,
        int? SceneType,
        int? MatchMode,
        string? TriggerKey,
        bool Hit,
        string Reason);

    /// <summary>追加匹配过程单步（仅旁路采集——不影响回复决策语义）。</summary>
    private void Trace(int? sceneType, int? matchMode, long? ruleId, string? ruleName, string? triggerKey, bool hit, string reason)
        => _trace.Add(new MatchTraceStep(ruleId, ruleName, sceneType, matchMode, triggerKey, hit, reason));

    /// <summary>构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；logger/options 从 DI 解析——
    /// 对齐平台基类形态，非构造注入领域服务；<see cref="WeChatMessageGatewayOptions"/> 为网关默认开关
    /// （默认 false 零回归）——守卫工厂可解析（Initializer AddOptions 兜底））。</summary>
    public WeChatOperationsReplyHandler(
        IDomainUser user,
        IOptions<WeChatOperationsOptions>? options = null,
        IOptions<WeChatMessageGatewayOptions>? gatewayOptions = null,
        ILogger? logger = null)
        : base(user, logger)
    {
        _options = options?.Value ?? new WeChatOperationsOptions();
        _gatewayOptions = gatewayOptions?.Value ?? new WeChatMessageGatewayOptions();
        _logger = logger;
    }

    // ─── 事件类 ───

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnSubscribeAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路：重复回调直接跳过，不计数不转发）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);

        // Subscribe 规则 → 渲染；无规则 → TryReplyBySceneAsync 已含 template 兜底（Oracle P0 选项 A 内部组合）
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Subscribe, triggerKey: null, triggerType: 1, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnUnsubscribeAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 取关无本地回复——回复被协议门控吞掉（副作用统计归编排层 IFollowStateService.RecordAsync，handler 不写）
        return null;
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnTextAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);

        var content = ctx.Content ?? string.Empty;

        // ① Keyword 完全匹配（MatchMode=1，TriggerKey 精确 == 内容）
        var exact = await RuleDs.EntityGetAsync(r => r.ChannelId == ctx.ChannelId && r.SceneType == WeChatReplySceneTypes.Keyword
            && r.IsEnabled && r.MatchMode == 1 && r.TriggerKey == content, ct);
        if (exact is not null)
        {
            Trace(WeChatReplySceneTypes.Keyword, 1, exact.Id, ruleName: null, exact.TriggerKey, hit: true, "TriggerKey==content");
            return await TryRenderRuleAsync(ctx, exact, triggerType: 2, ct);
        }
        Trace(WeChatReplySceneTypes.Keyword, 1, null, ruleName: null, content, hit: false, "无完全匹配规则（TriggerKey≠content）");

        // ② Keyword 包含匹配（MatchMode=2——内容包含关键词；取 SortOrder 最小首个）
        var contains = await RuleDs.EntitySelectAsync(r => r.ChannelId == ctx.ChannelId && r.SceneType == WeChatReplySceneTypes.Keyword
            && r.IsEnabled && r.MatchMode == 2 && r.TriggerKey != null && r.TriggerKey != "",
            0, 100, q => q.OrderBy(r => r.SortOrder), ct);
        if (contains is { Count: > 0 })
        {
            foreach (var rule in contains)
            {
                var hit = content.Contains(rule.TriggerKey!, StringComparison.Ordinal);
                Trace(WeChatReplySceneTypes.Keyword, 2, rule.Id, ruleName: null, rule.TriggerKey, hit,
                    hit ? "content contains key" : "content 不含关键词");
                if (hit)
                    return await TryRenderRuleAsync(ctx, rule, triggerType: 2, ct);
            }
        }
        else
        {
            Trace(WeChatReplySceneTypes.Keyword, 2, null, ruleName: null, triggerKey: null, hit: false, "无包含匹配规则");
        }

        // ③ Default 兜底规则（SceneType=Default——未命中关键词）
        var def = await RuleDs.EntityGetAsync(r => r.ChannelId == ctx.ChannelId && r.SceneType == WeChatReplySceneTypes.Default && r.IsEnabled, ct);
        if (def is not null)
        {
            Trace(WeChatReplySceneTypes.Default, matchMode: null, def.Id, ruleName: null, def.TriggerKey, hit: true, "Default 规则命中");
            return await TryRenderRuleAsync(ctx, def, triggerType: 11, ct);
        }
        Trace(WeChatReplySceneTypes.Default, matchMode: null, null, ruleName: null, triggerKey: null, hit: false, "无 Default 规则");

        // ④ 无规则 → template 兜底 → 再兜底 Options.DefaultReplyText
        var tpl = await TemplateFallbackAsync(ctx, ct);
        if (tpl is not null)
        {
            Trace(WeChatReplySceneTypes.Keyword, matchMode: null, null, ruleName: null, triggerKey: null, hit: true, "template fallback");
            return tpl;
        }
        Trace(WeChatReplySceneTypes.Keyword, matchMode: null, null, ruleName: null, triggerKey: null, hit: false,
            "无规则且无 template → Options.DefaultReplyText 兜底");
        return await ReplyTextPayloadAsync(ctx, _options.DefaultReplyText, triggerType: 12, materialId: null, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnClickAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);

        // Click 映射——TriggerKey=EventKey（菜单 key）；未映射 → null（不回复）
        if (string.IsNullOrWhiteSpace(ctx.EventKey)) return null;
        var rule = await RuleDs.EntityGetAsync(r => r.ChannelId == ctx.ChannelId && r.SceneType == WeChatReplySceneTypes.Click
            && r.IsEnabled && r.TriggerKey == ctx.EventKey, ct);
        if (rule is null)
        {
            Trace(WeChatReplySceneTypes.Click, matchMode: null, null, ruleName: null, ctx.EventKey, hit: false, "未映射规则（TriggerKey≠EventKey）");
            return null;
        }
        Trace(WeChatReplySceneTypes.Click, matchMode: null, rule.Id, ruleName: null, rule.TriggerKey, hit: true, "TriggerKey==EventKey");
        return await TryRenderRuleAsync(ctx, rule, triggerType: 3, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnScanAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);

        // Scan 规则（可含 TriggerKey=场景值——扫码带参）；无规则 → null
        var rule = await RuleDs.EntitySelectAsync(r => r.ChannelId == ctx.ChannelId && r.SceneType == WeChatReplySceneTypes.Scan
            && r.IsEnabled && (r.TriggerKey == null || r.TriggerKey == "" || r.TriggerKey == ctx.SceneId),
            0, 10, q => q.OrderBy(r => r.SortOrder), ct);
        if (rule is { Count: > 0 })
        {
            Trace(WeChatReplySceneTypes.Scan, matchMode: null, rule[0].Id, ruleName: null, rule[0].TriggerKey ?? ctx.SceneId,
                hit: true, "场景规则命中（SortOrder 首个）");
            return await TryRenderRuleAsync(ctx, rule[0], triggerType: 10, ct);
        }
        Trace(WeChatReplySceneTypes.Scan, matchMode: null, null, ruleName: null, ctx.SceneId, hit: false, "无场景规则");
        return null;
    }

    // ─── 消息类（对应 SceneType——无规则 template 兜底） ───

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnImageAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Image, triggerKey: null, triggerType: 4, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnVoiceAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Voice, triggerKey: null, triggerType: 5, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnVideoAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Video, triggerKey: null, triggerType: 6, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnShortVideoAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.ShortVideo, triggerKey: null, triggerType: 7, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnLocationAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Location, triggerKey: null, triggerType: 8, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnLinkAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ①——幂等最前短路）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;

        // 消息业务总开关（DMP-Lite 5.8——关闭后本地回复停发，统一维护提示；网关已先行不受控）
        if (!await IsMessageBusinessEnabledAsync(ct)) return await MaintenanceReply(ctx, ct);
        return await TryReplyBySceneAsync(ctx, WeChatReplySceneTypes.Link, triggerKey: null, triggerType: 9, ct);
    }

    /// <inheritdoc />
    protected override async Task<WechatReplyPayload?> OnLocationEventAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 网关挂载（方案 §十·五 ① P2-2 精确化——LOCATION 上报事件补入：可转发应补；本地无回复逻辑 → null）
        var gate = await Gateway.RouteAsync(ctx, ct);
        if (gate.Handled) return gate.Result;
        return null;
    }

    /// <inheritdoc />
    protected override Task<WechatReplyPayload?> OnViewAsync(WechatEventContext ctx, CancellationToken ct)
        => Task.FromResult<WechatReplyPayload?>(null);   // 微信直接跳转，无回复

    /// <inheritdoc />
    protected override Task<WechatReplyPayload?> OnViewMiniProgramAsync(WechatEventContext ctx, CancellationToken ct)
        => Task.FromResult<WechatReplyPayload?>(null);   // 微信直接跳转，无回复

    /// <summary>
    /// 维护提示回复（消息业务总开关关闭时——本地回复停发统一返回维护提示；DMP-Lite 5.3"统一维护提示"）。
    /// <para>复用 <see cref="WeChatOperationsOptions.DefaultReplyText"/> 兜底文案——不新增配置键；
    /// 文案为空 → null 不回复（对齐 <see cref="ReplyTextPayloadAsync"/> 空文案语义）。</para>
    /// </summary>
    private Task<WechatReplyPayload?> MaintenanceReply(WechatEventContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.DefaultReplyText))
            return Task.FromResult<WechatReplyPayload?>(null);
        return Task.FromResult<WechatReplyPayload?>(new WechatReplyPayload(Content: _options.DefaultReplyText));
    }

    // ─── 内部（渲染三层：规则 → 素材 → 信封闭包；交互日志 best-effort） ───

    /// <summary>按场景读规则（场景型规则 TriggerKey 空）→ 渲染（素材/文本）→ 交互日志记录。
    /// 匹配过程追踪：场景规则命中的 SortOrder 首个 / 无该场景规则 → template 兜底（hit/miss 各记一步）。</summary>
    private async Task<WechatReplyPayload?> TryReplyBySceneAsync(
        WechatEventContext ctx, int sceneType, string? triggerKey, int triggerType, CancellationToken ct)
    {
        var rules = await RuleDs.EntitySelectAsync(
            r => r.ChannelId == ctx.ChannelId && r.SceneType == sceneType && r.IsEnabled
                 && (triggerKey == null || r.TriggerKey == triggerKey),
            0, 10, q => q.OrderBy(r => r.SortOrder), ct);
        if (rules is { Count: > 0 })
        {
            Trace(sceneType, matchMode: null, rules[0].Id, ruleName: null, rules[0].TriggerKey ?? triggerKey,
                hit: true, "场景规则命中（SortOrder 首个）");
            return await TryRenderRuleAsync(ctx, rules[0], triggerType, ct);
        }

        // 无规则 → template 兜底（hit/miss 追踪——仅旁路采集，不影响回复决策）
        Trace(sceneType, matchMode: null, null, ruleName: null, triggerKey, hit: false, "无该场景规则");
        var tpl = await TemplateFallbackAsync(ctx, ct);
        if (tpl is not null)
        {
            Trace(sceneType, matchMode: null, null, ruleName: null, triggerKey, hit: true, "template fallback");
            return tpl;
        }
        Trace(sceneType, matchMode: null, null, ruleName: null, triggerKey, hit: false, "无场景规则且无 template");
        return null;
    }

    /// <summary>规则渲染：<c>ReplyText</c>（直接文本——优先于素材）→ <c>MaterialId</c>（素材——Kind=1 文本 / Kind=2 图文）→
    /// 记录交互日志（回复内容 + 素材引用）。</summary>
    private async Task<WechatReplyPayload?> TryRenderRuleAsync(WechatEventContext ctx, ReplyRuleEntity rule, int triggerType, CancellationToken ct)
    {
        // ① ReplyText 优先（非空时忽略 MaterialId——设计 §3.3.1）
        if (!string.IsNullOrWhiteSpace(rule.ReplyText))
            return await ReplyTextPayloadAsync(ctx, rule.ReplyText!, triggerType, materialId: null, ct);

        // ② 素材引用（MaterialId → MaterialEntity；禁用素材跳过 → null）
        if (rule.MaterialId is { } materialId)
        {
            var material = await MatDs.EntityGetAsync(m => m.Id == materialId, ct);
            if (material is { IsEnabled: true })
            {
                if (material.Kind == 1 && !string.IsNullOrWhiteSpace(material.Content))        // Text 素材
                    return await ReplyTextPayloadAsync(ctx, material.Content!, triggerType, materialId, ct);
                if (material.Kind == 2 && !string.IsNullOrWhiteSpace(material.Title))          // News 素材（图文单篇）
                {
                    var payload = new WechatReplyPayload(News:
                    [
                        new EventReplyNewsArticle(
                            material.Title!, material.Description ?? "", material.Url ?? "", material.PicUrl),
                    ]);
                    await RecordInteractionAsync(ctx, triggerType, payload, materialId, ct);
                    return payload;
                }
            }
        }
        return null;
    }

    /// <summary>文本回复载荷（包 text XML 信封——经平台基类 <see cref="BuildTextReply"/>）+ 交互日志。</summary>
    private async Task<WechatReplyPayload?> ReplyTextPayloadAsync(WechatEventContext ctx, string content, int triggerType, long? materialId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var payload = new WechatReplyPayload(Content: content);
        await RecordInteractionAsync(ctx, triggerType, payload, materialId, ct);
        return payload;
    }

    /// <summary>
    /// Template 兜底（Oracle P0 选项 A——内部组合，不依赖编排器迭代顺序）：读 <c>Channel.Extra["news_reply"]</c>
    /// JSON（多图文优先 <c>articles</c> → 单图文兼容回退，对齐平台库 <see cref="TKWF.Federation.WeChat.NewsReplyTemplateV2"/>）
    /// → 占位符替换 → 图文信封闭包；模板缺失/损坏/禁回复 → null（不回复——对齐平台模板 handler Oracle P0-2 语义）。
    /// <para>I2-8：模板含 <c>{ticket}</c> 时经 <see cref="TKWF.Federation.WeChat.WechatEventReplyHandlerBase.ResolveTicketAsync"/>
    /// 签免登票据（模板 TtlSeconds 优先）——票 URL 替换进 link/description（未启用 → 空串替换，回复结构不变）。</para>
    /// </summary>
    private async Task<WechatReplyPayload?> TemplateFallbackAsync(WechatEventContext ctx, CancellationToken ct)
    {
        var template = ReadNewsTemplateFromChannel(ctx);
        if (template is null or { Enabled: not true }) return null;

        // I2-8：签一次免登票据（模板 TtlSeconds 优先 → FederationLoginEntryOptions.TicketTtlSeconds 缺省——ResolveTicketAsync 内）
        var ticketUrl = await ResolveTicketAsync(ctx, ct, template.TtlSeconds);

        var articles = template.Articles is { Count: > 0 }
            ? template.Articles.Take(8)   // 微信图文上限 8 篇
                .Select(a => new EventReplyNewsArticle(
                    ReplacePlaceholders(a.Title ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(a.Description ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(a.Link ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    a.PicUrl))
                .ToList()
            : new List<EventReplyNewsArticle> {
                new(
                    ReplacePlaceholders(template.Title ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(template.Description ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(template.Link ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    template.PicUrl) };

        var payload = new WechatReplyPayload(News: articles);
        return payload;
    }

    /// <summary>读 Channel.Extra["news_reply"] 模板（多图文 V2——自解析；缺失/损坏 → null，对齐 Oracle P0-2）。</summary>
    private static TKWF.Federation.WeChat.NewsReplyTemplateV2? ReadNewsTemplateFromChannel(WechatEventContext ctx)
    {
        var extra = ctx.Channel?.Extra;
        if (extra is null || !extra.TryGetValue(TKWF.Federation.WeChat.WechatEventReplyHandlerBase.NewsReplyTemplateKey, out var raw)
            || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TKWF.Federation.WeChat.NewsReplyTemplateV2>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>记录交互日志（best-effort——记录失败不阻塞回复；⚠️ 帧内执行，无帧外解析守卫问题；
    /// <see cref="InteractionLogEntity.DurationMs"/> = 本次消息处理耗时（Stopwatch 入口起→记录时止）；
    /// <see cref="InteractionLogEntity.MatchTraceJson"/> = 本次匹配过程逐行追踪（<see cref="_trace"/>
    /// → <c>System.Text.Json</c> 序列化 JSON 数组；无追踪步 → null）。</summary>
    private async Task RecordInteractionAsync(WechatEventContext ctx, int triggerType, WechatReplyPayload payload, long? materialId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.ChannelId) || payload is null) return;
        try
        {
            await LogDs.EntityCreateAsync(new InteractionLogEntity
            {
                ChannelId = ctx.ChannelId,
                OpenId = ctx.OpenId ?? ctx.FromUserName ?? string.Empty,
                MsgTime = DateTime.UtcNow,
                MsgType = ctx.MsgType ?? string.Empty,
                Content = ctx.Content,
                TriggerType = triggerType,
                ReplyContent = payload.Content,   // 文本直接承载；图文 null（可经素材 id 追溯）
                ReplyMaterialId = materialId,
                DurationMs = _sw.ElapsedMilliseconds,   // V0.3.0 耗时字段——Stopwatch 入口起→记录时止
                MatchTraceJson = _trace.Count > 0 ? JsonSerializer.Serialize(_trace) : null,   // V0.3.1 匹配过程追踪（仅旁路采集）
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "交互日志记录失败（best-effort——不阻塞回复）：ChannelId={ChannelId} TriggerType={TriggerType}",
                ctx.ChannelId, triggerType);
        }
    }
}