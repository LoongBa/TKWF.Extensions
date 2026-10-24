using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 诊断探针 handler（方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.3——
/// 无条件注册 <c>TryAddEnumerableConstructible</c> + <see cref="IOptions{TOptions}"/> 惰性判断
/// <see cref="WechatEventReplyOptions.ProbeEventReplyHandlerEnabled"/>（Oracle P1-3）。
/// <para>开关<b>关</b>（默认 false，P1-1 fail-safe）：全分支返回 null（不回复不干预，业务 handler 正常流转）；
/// 开关<b>开</b>：可回复分支返回 <c>PROBE:branch=...</c> 可区分文本（每分支文案不同——验证分支命中）。
/// <b>门控分支</b>（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）：基类协议门控在回复组装层吞掉
/// （返回 null），日志由基类 <see cref="WechatEventReplyHandlerBase.LogProbeBranch"/> 输出——探针无需特殊处理。</para>
/// <para><b>互斥声明</b>（Oracle P2-5）：探针与业务 handler 互斥使用——接入业务 handler 后保持开关关
/// （默认），开发期开 true 验证全分支。注册：<c>AddWeChatFederationChannels()</c> 内无条件注册
/// （方案 §3.5——仅 Probe 因开关门控无条件注册；Template 归消费方显式注册）。</para>
/// </summary>
[DiagnosticEventReplyHandler]
[DiContractIgnore]
public sealed class WechatProbeEventReplyHandler : WechatEventReplyHandlerBase
{
    private readonly IOptions<WechatEventReplyOptions> _options;

    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；<see cref="IOptions{TOptions}"/> 从 DI 解析——
    /// Oracle P1-3 惰性判断开关，不为关闭状态做无谓构造）。
    /// </summary>
    public WechatProbeEventReplyHandler(
        IDomainUser user,
        IOptions<WechatEventReplyOptions> options,
        ILogger? logger = null)
        : base(user, logger)
    {
        _options = options ?? throw new System.ArgumentNullException(nameof(options));
    }

    /// <summary>探针开关（惰性读取——Oracle P1-3；关 → 全分支 null 不回复不干预）。</summary>
    private bool Enabled => _options.Value.ProbeEventReplyHandlerEnabled;

    private Task<WechatReplyPayload?> Probe(string text)
        => Task.FromResult<WechatReplyPayload?>(Enabled ? new WechatReplyPayload(Content: text) : null);

    /// <summary>关注事件 → <c>PROBE:branch=subscribe</c>（EventKey 带参二维码场景值）。</summary>
    protected override Task<WechatReplyPayload?> OnSubscribeAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=subscribe key={ctx.EventKey}");

    /// <summary>取消关注事件（Reply 被基类协议门控吞掉——副作用可做；开关开时未回复，日志由基类 LogProbeBranch 输出）。</summary>
    protected override Task<WechatReplyPayload?> OnUnsubscribeAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=unsubscribe");

    /// <summary>扫码事件 → <c>PROBE:branch=SCAN scene={SceneId}</c>（场景值去 <c>qrscene_</c> 前缀）。</summary>
    protected override Task<WechatReplyPayload?> OnScanAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=SCAN scene={ctx.SceneId}");

    /// <summary>上报地理位置事件 → <c>PROBE:branch=LOCATION lat=... lng=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnLocationEventAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=LOCATION lat={ctx.Latitude} lng={ctx.Longitude} precision={ctx.Precision}");

    /// <summary>点击菜单拉取消息事件 → <c>PROBE:branch=CLICK key={EventKey}</c>。</summary>
    protected override Task<WechatReplyPayload?> OnClickAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=CLICK key={ctx.EventKey}");

    /// <summary>点击菜单跳转链接事件 → <c>PROBE:branch=VIEW url={EventKey}</c>。</summary>
    protected override Task<WechatReplyPayload?> OnViewAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=VIEW url={ctx.EventKey}");

    /// <summary>点击菜单跳转小程序事件 → <c>PROBE:branch=VIEW_MINIPROGRAM key={EventKey}</c>。</summary>
    protected override Task<WechatReplyPayload?> OnViewMiniProgramAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=VIEW_MINIPROGRAM key={ctx.EventKey}");

    /// <summary>群发结果事件（Reply 被基类协议门控吞掉——日志由基类 LogProbeBranch 输出）。</summary>
    protected override Task<WechatReplyPayload?> OnMassSendJobFinishAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=MASSSENDJOBFINISH status={ctx.Status} sent={ctx.SentCount}");

    /// <summary>文本消息 → <c>PROBE:branch=text content={Content}</c>。</summary>
    protected override Task<WechatReplyPayload?> OnTextAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=text content={ctx.Content}");

    /// <summary>图片消息 → <c>PROBE:branch=image media_id=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnImageAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=image media_id={ctx.MediaId}");

    /// <summary>语音消息 → <c>PROBE:branch=voice media_id=... format=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnVoiceAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=voice media_id={ctx.MediaId} format={ctx.Format}");

    /// <summary>视频消息 → <c>PROBE:branch=video media_id=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnVideoAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=video media_id={ctx.MediaId} thumb_media_id={ctx.ThumbMediaId}");

    /// <summary>小视频消息 → <c>PROBE:branch=shortvideo media_id=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnShortVideoAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=shortvideo media_id={ctx.MediaId} thumb_media_id={ctx.ThumbMediaId}");

    /// <summary>地理位置消息 → <c>PROBE:branch=location lat=... lng=... scale=... label=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnLocationAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=location lat={ctx.Latitude} lng={ctx.Longitude} scale={ctx.Scale} label={ctx.Label}");

    /// <summary>链接消息 → <c>PROBE:branch=link title=... url=...</c>。</summary>
    protected override Task<WechatReplyPayload?> OnLinkAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=link title={ctx.Title} url={ctx.Url}");

    /// <summary>兜底分支 → <c>PROBE:branch=unknown msg_type=... event=...</c>（未覆盖类型诊断）。</summary>
    protected override Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct)
        => Probe($"PROBE:branch=unknown msg_type={ctx.MsgType} event={ctx.Event}");
}