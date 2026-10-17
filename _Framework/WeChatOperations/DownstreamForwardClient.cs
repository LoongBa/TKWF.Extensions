using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 下游转发出站客户端（typed client——<c>AddHttpClient&lt;DownstreamForwardClient&gt;()</c> DI 生命周期托管，
/// 对齐 <see cref="TKWF.Federation.WeChat.WeChatApiClient"/> 先例防 HttpClient 悬挂 socket；方案 §十·二 ⑤）。
/// <para>职责：把微信回调<b>原始报文</b>（<c>rawXml</c> 明文 XML——方案 §十·二 ④ 原样透传不重新组装）POST
/// 到 <see cref="DownstreamEndpointEntity"/> 配置的出站目标（BaseUrl + Path 拼接），携带鉴权头，按端点
/// <c>TimeoutSeconds</c> 硬超时（linked CTS——<c>HttpClient.Timeout</c> 为全局属性不能按端点设）。</para>
/// <para>鉴权（<see cref="WeChatEndpointAuthTypes"/>）：0=None 无鉴权 / 1=Bearer（<c>Authorization: Bearer {token}</c>）/
/// 2=Header 自定义头（固定头名 <see cref="CustomAuthHeader"/>——实体无头名字段，约定常量承载，写入使用指南）。</para>
/// <para>双出站形态：① <see cref="ForwardAsync"/>（AsyncCopy 异步抄送——投递在 <see cref="WeChatMessageForwardJob"/> 内调用，
/// 响应体截断 500 留痕）；② <see cref="SendRawXmlAsync"/>（SyncProxy/PreprocessSync 同步透传——下游响应<b>完整 XML 原样返回</b>，
/// 供 <see cref="MessageGatewayService"/> 包装 <c>WechatReplyPayload.RawXml</c> 逃逸口回复微信，防双重信封）。</para>
/// </summary>
public sealed class DownstreamForwardClient
{
    /// <summary>自定义头鉴权（AuthType=2）固定头名——实体无头名字段，约定常量承载（方案 §十·一 ② 字段 9）。</summary>
    public const string CustomAuthHeader = "X-WeChat-Forward-Auth";

    /// <summary>出站响应体截断长度（对齐 <see cref="MessageForwardLogEntity.ResponseBody"/> 列 MaxLength 500）。</summary>
    public const int ResponseBodyMaxLength = 500;

    private readonly HttpClient _http;
    private readonly ILogger<DownstreamForwardClient> _logger;

    /// <summary>构造——HttpClient 经 typed client 注入（AddHttpClient——DI 生命周期托管防 socket 悬挂）。</summary>
    public DownstreamForwardClient(HttpClient http, ILogger<DownstreamForwardClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownstreamForwardClient>.Instance;
    }

    /// <summary>
    /// 转发一次原始报文（<c>rawXml</c> 明文 XML 原样透传——不重新组装不验签，下游为本系统主动转发接收方，
    /// 信任边界声明方案 §十·二 ⑥）。
    /// </summary>
    /// <param name="baseUrl">出站目标基址（含 scheme/host/端口）。</param>
    /// <param name="path">路径（可空——缺省直接命中 BaseUrl）。</param>
    /// <param name="httpMethod">HTTP 方法（默认 POST——微信被动回复/转发语义）。</param>
    /// <param name="authType">鉴权类型（<see cref="WeChatEndpointAuthTypes"/>：0/1/2）。</param>
    /// <param name="authToken">鉴权令牌明文（AES-GCM 解密后瞬时——仅出站，不落日志）。</param>
    /// <param name="rawXml">微信回调原始报文（明文 XML）。</param>
    /// <param name="timeoutSeconds">硬超时秒数（≤3——微信 5 秒时限留本地兜底余量）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>转发结果（<see cref="DownstreamForwardResult"/>——超时/状态码/截断响应体/耗时）。</returns>
    public async Task<DownstreamForwardResult> ForwardAsync(
        string baseUrl, string? path, string httpMethod, int authType, string? authToken,
        string rawXml, int timeoutSeconds, CancellationToken ct = default)
    {
        var core = await ForwardCoreAsync(baseUrl, path, httpMethod, authType, authToken, rawXml, timeoutSeconds, ct);
        return new DownstreamForwardResult(core.FailedOrTimedOut, core.StatusCode, Truncate(core.Body, ResponseBodyMaxLength), core.DurationMs);
    }

    /// <summary>
    /// 同步透传一次原始报文（SyncProxy/PreprocessSync——方案 §十·二 P0-1 选 A）：<c>rawXml</c> 明文 XML 原样 POST
    /// 下游，返回下游<b>完整响应 XML</b>（<see cref="DownstreamSyncResult.ResponseXml"/>——不截断，供
    /// <see cref="MessageGatewayService"/> 原样回复微信）。超时/出站失败经 <see cref="DownstreamSyncResult.TimedOut"/>
    /// 区分（Timeout/Fail 转发日志分支）。
    /// </summary>
    /// <param name="baseUrl">出站目标基址（含 scheme/host/端口）。</param>
    /// <param name="path">路径（可空——缺省直接命中 BaseUrl）。</param>
    /// <param name="httpMethod">HTTP 方法（默认 POST——微信被动回复/转发语义）。</param>
    /// <param name="authType">鉴权类型（<see cref="WeChatEndpointAuthTypes"/>：0/1/2）。</param>
    /// <param name="authToken">鉴权令牌明文（AES-GCM 解密后瞬时——仅出站，不落日志）。</param>
    /// <param name="rawXml">微信回调原始报文（明文 XML）。</param>
    /// <param name="timeoutSeconds">硬超时秒数（≤3——微信 5 秒时限留本地兜底余量）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>同步透传结果（<see cref="DownstreamSyncResult"/>——失败/超时区分 + 完整响应 XML/耗时）。</returns>
    public async Task<DownstreamSyncResult> SendRawXmlAsync(
        string baseUrl, string? path, string httpMethod, int authType, string? authToken,
        string rawXml, int timeoutSeconds, CancellationToken ct = default)
    {
        var core = await ForwardCoreAsync(baseUrl, path, httpMethod, authType, authToken, rawXml, timeoutSeconds, ct);
        return new DownstreamSyncResult(core.FailedOrTimedOut, core.TimedOut, core.StatusCode, core.Body, core.DurationMs);
    }

    /// <summary>出站核心（<see cref="ForwardAsync"/>/<see cref="SendRawXmlAsync"/> 共用——响应体不截断返回，由调用方按形态截断/透传）。</summary>
    private async Task<DownstreamForwardCore> ForwardCoreAsync(
        string baseUrl, string? path, string httpMethod, int authType, string? authToken,
        string rawXml, int timeoutSeconds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);

        var target = BuildTargetUri(baseUrl, path);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 硬超时（linked CTS——HttpClient.Timeout 为全局属性不能按端点设；方案 §十·二 ②）
        if (timeoutSeconds > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var req = new HttpRequestMessage(new HttpMethod(ResolveMethod(httpMethod)), target)
        {
            // ⚠️ mediaType 须为裸媒体类型（charset 由 Encoding 参数承载——"text/xml; charset=utf-8" 带参数
            //    StringContent 会抛 FormatException，实测暴露：AsyncCopy 作业未直测 HTTP 未被触发，SyncProxy 出站首暴露）
            Content = new StringContent(rawXml, Encoding.UTF8, "text/xml"),
        };
        ApplyAuth(req, authType, authToken);

        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            sw.Stop();
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            _logger.LogInformation("下游转发完成：Target={Target} StatusCode={StatusCode} DurationMs={DurationMs}",
                target, (int)resp.StatusCode, sw.ElapsedMilliseconds);
            return new DownstreamForwardCore(false, false, (int)resp.StatusCode, body, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 端点超时（CancelAfter 触发——非调用方取消）——降级本地兜底；方案 §十·二 ②
            sw.Stop();
            _logger.LogWarning("下游转发超时：Target={Target} TimeoutSeconds={TimeoutSeconds}", target, timeoutSeconds);
            return new DownstreamForwardCore(true, true, 0, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // 出站异常（不可达/连接失败等）——异常消息不含明文凭证（脱敏；方案 §十·六 ③）
            sw.Stop();
            _logger.LogWarning(ex, "下游转发异常：Target={Target}", target);
            return new DownstreamForwardCore(true, false, 0, null, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>BaseUrl + Path 拼接完整目标（Path 空 → 直接 BaseUrl；拼接防双斜杠）。</summary>
    private static Uri BuildTargetUri(string baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new Uri(baseUrl);
        return new Uri(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));
    }

    /// <summary>HTTP 方法解析（默认 POST——微信转发语义；未知方法 fail-safe POST）。</summary>
    private static string ResolveMethod(string httpMethod)
        => string.IsNullOrWhiteSpace(httpMethod) ? "POST" : httpMethod.ToUpperInvariant();

    /// <summary>鉴权头应用（WeChatEndpointAuthTypes：0=None / 1=Bearer / 2=自定义头）。</summary>
    private static void ApplyAuth(HttpRequestMessage req, int authType, string? authToken)
    {
        if (string.IsNullOrEmpty(authToken))
            return;
        switch (authType)
        {
            case WeChatEndpointAuthTypes.Bearer:
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                break;
            case WeChatEndpointAuthTypes.Header:
                req.Headers.TryAddWithoutValidation(CustomAuthHeader, authToken);
                break;
            default:
                break;   // None 无鉴权
        }
    }

    /// <summary>响应体截断（列 MaxLength 500——方案 §十·六 ③ 日志脱敏/截断）。</summary>
    internal static string? Truncate(string? body, int max)
        => body is null ? null : (body.Length <= max ? body : body[..max]);
}

/// <summary>
/// 下游转发结果（<see cref="DownstreamForwardClient.ForwardAsync"/> 返回——Job 回写
/// <see cref="MessageForwardLogEntity"/> 数据源；方案 §十·二 ⑤）。
/// </summary>
/// <param name="FailedOrTimedOut">出站失败或超时（true——投递未成功，Job 记 Fail/Timeout）。</param>
/// <param name="StatusCode">出站 HTTP 状态码（0 = 未出站/超时）。</param>
/// <param name="Body">出站响应体（截断 500——可空）。</param>
/// <param name="DurationMs">转发耗时（毫秒）。</param>
public sealed record DownstreamForwardResult(bool FailedOrTimedOut, int StatusCode, string? Body, long DurationMs);

/// <summary>
/// 下游同步透传结果（<see cref="DownstreamForwardClient.SendRawXmlAsync"/> 返回——SyncProxy/PreprocessSync
/// 出站；<see cref="ResponseXml"/> 为下游<b>完整响应 XML</b>（不截断——供 RawXml 逃逸口原样回复微信）。
/// </summary>
/// <param name="FailedOrTimedOut">出站失败或超时（true——投递未成功，网关记 Fail/Timeout 并降级本地渲染链）。</param>
/// <param name="TimedOut">是否端点超时（true=CancelAfter 触发——转发日志 Status=Timeout 分支；false=出站异常/非 XML）。</param>
/// <param name="StatusCode">出站 HTTP 状态码（0 = 未出站/超时）。</param>
/// <param name="ResponseXml">下游完整响应 XML（可空——失败/超时/无响应体）。</param>
/// <param name="DurationMs">转发耗时（毫秒）。</param>
public sealed record DownstreamSyncResult(bool FailedOrTimedOut, bool TimedOut, int StatusCode, string? ResponseXml, long DurationMs);

/// <summary>出站核心结果（<see cref="DownstreamForwardClient"/> 内部——双公开形态共用；响应体不截断由调用方按需处理）。</summary>
internal sealed record DownstreamForwardCore(bool FailedOrTimedOut, bool TimedOut, int StatusCode, string? Body, long DurationMs);
