using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 异步抄送投递作业（<c>[BackgroundJob]</c> 标记——BackgroundJob 队列执行，方案 §十·三）。
/// <para>⚠️ 执行帧：BackgroundJobWorker <b>SystemActor</b> 帧内（<c>BeginSystemScopeAsync</c> 已设
/// <see cref="TKW.Framework.Domain.StandaloneDomainUserAccessor{TUserInfo}"/> + BindScope）——作业<b>非泛型</b>
/// 经 <see cref="IWeChatSystemUserProvider"/> 桥取系统用户后 <c>User.Use&lt;DataService&gt;()</c> NoAop 直建
/// （对齐 <c>TKWF.Ext.BackgroundJobs.JobExecutionRecorder&lt;TUserInfo&gt;</c> 先例，V4.10.55 ADR92/T3）。</para>
/// <para>职责：解析下游端点（同 channel 启用校验 + AuthToken AES-GCM 解密瞬时）→
/// <see cref="DownstreamForwardClient"/> HTTP POST 原始报文（幂等键不参与出站——下游自行幂等由
/// <see cref="WeChatMessageForwardJobArgs.IdempotencyKey"/> 透传）→ 回写
/// <see cref="MessageForwardLogEntity"/>（Status=Success/Fail + StatusCode/ResponseBody 截断/DurationMs/ForwardedAt）。</para>
/// <para>⚠️ args 类型：BackgroundJob 队列经 <c>System.Text.Json</c>（JsonSerializerDefaults.Web + Type.GetType AQN）
/// 往返——须 <b>public 顶层类型 + 公开无参构造</b>（匿名类型不可解析会静默降级 null）；故用
/// <see cref="WeChatMessageForwardJobArgs"/> record（显式无参构造，方案 §十·三 ④ 字段对齐）。</para>
/// <para>⚠️ 注册：消费方 <c>AddBackgroundJobs&lt;TUserInfo&gt;()</c> 的 SG 类型清单归消费方编译——扩展作业
/// 不被消费方自动登记，须本扩展 Initializer 显式 <c>AddTransient</c> 注册（Worker 经
/// <c>sp.GetRequiredService(jobType)</c> 解析，未注册 → 作业落 Failed）。</para>
/// </summary>
[BackgroundJob(DisplayName = "微信消息异步抄送投递")]
public sealed class WeChatMessageForwardJob : IBackgroundJob
{
    private readonly IServiceProvider _sp;
    private readonly DownstreamForwardClient _client;
    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<WeChatMessageForwardJob>? _logger;

    private IWeChatSystemUserProvider? _systemUserProvider;
    private IWeChatSystemUserProvider SystemUserProvider
        => _systemUserProvider ??= _sp.GetRequiredService<IWeChatSystemUserProvider>();

    /// <summary>
    /// 构造（DI 全满足——Worker System 帧 scope 解析：IServiceProvider/客户端/keyed 密钥/logger 均注册于消费方 DI；
    /// 系统用户桥经 <see cref="IServiceProvider"/> 惰性解析（内部类型不可入公开 ctor 签名）；零 DataService
    /// 构造注入——DataService 经系统用户 NoAop 直建，DI004 合规）。
    /// </summary>
    public WeChatMessageForwardJob(
        IServiceProvider sp,
        DownstreamForwardClient client,
        [FromKeyedServices(WeChatOperationsKeyProviderKeys.WeChatOperations)]
        ISymmetricKeyProvider keys,
        ILogger<WeChatMessageForwardJob>? logger = null)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(object args, CancellationToken ct = default)
    {
        // args 往返降级防御（Unpack 失败 → null——静默丢弃仅记日志，方案 P2-3 阶段一无死信）
        if (args is not WeChatMessageForwardJobArgs a)
        {
            _logger?.LogWarning("微信异步抄送作业参数缺失/不可解析——静默丢弃（args={ArgsType}）", args?.GetType().FullName);
            return;
        }

        // ① SystemActor 帧内取系统用户 → DataService NoAop 直建（对齐 JobExecutionRecorder 先例）
        IDomainUser user;
        MessageForwardLogEntityDataService logDs;
        try
        {
            user = SystemUserProvider.GetSystemUser();
            logDs = user.Use<MessageForwardLogEntityDataService>();
        }
        catch (Exception ex) when (ct.IsCancellationRequested is false)
        {
            // System 帧未绑定（非 Worker 直调场景）——作业无法落库，记日志告警（不重试——帧问题重试无效）
            _logger?.LogWarning(ex, "微信异步抄送作业 SystemActor 帧缺失——无法访问数据服务（ForwardLogId={ForwardLogId}）", a.ForwardLogId);
            return;
        }

        // ② 解析下游端点（同 channel 启用校验——规则引用须同 channel；禁用/缺失 → Fail 回写）
        var epDs = user.Use<DownstreamEndpointEntityDataService>();
        var ep = await epDs.GetEnabledEndpointByIdAsync(a.EndpointId, a.ChannelId, ct);
        if (ep is null)
        {
            _logger?.LogWarning("微信异步抄送端点不可用：EndpointId={EndpointId} ChannelId={ChannelId}——回写 Fail",
                a.EndpointId, a.ChannelId);
            await UpdateLogAsync(logDs, a.ForwardLogId, status: WeChatMessageForwardLogStatus.Fail,
                statusCode: null, responseBody: null, durationMs: null,
                errorMsg: $"下游端点不存在/已禁用（EndpointId={a.EndpointId}）", ct);
            return;
        }

        // ③ AuthToken AES-GCM 解密（明文仅出站瞬时——不落日志不落 JobRecord，方案 §十·六 ③）
        var token = string.IsNullOrWhiteSpace(ep.AuthTokenCipher) ? null : _keys.Decrypt(ep.AuthTokenCipher);

        // ④ HTTP POST 原始报文（DownstreamForwardClient——端点 TimeoutSeconds 硬超时）
        var result = await _client.ForwardAsync(
            ep.BaseUrl, ep.Path, ep.HttpMethod, ep.AuthType, token, a.RawXml,
            ep.TimeoutSeconds > 0 ? ep.TimeoutSeconds : 2, ct);

        // ⑤ 回写转发日志（Success/Fail + StatusCode/ResponseBody 截断/DurationMs/ForwardedAt；RetryCount 累计）
        if (result.FailedOrTimedOut)
        {
            _logger?.LogWarning("微信异步抄送投递失败：ForwardLogId={ForwardLogId} EndpointId={EndpointId} DurationMs={DurationMs}",
                a.ForwardLogId, a.EndpointId, result.DurationMs);
            await UpdateLogAsync(logDs, a.ForwardLogId, WeChatMessageForwardLogStatus.Fail,
                result.StatusCode, result.Body, result.DurationMs,
                $"下游转发失败/超时（DurationMs={result.DurationMs}）", ct);
        }
        else
        {
            await UpdateLogAsync(logDs, a.ForwardLogId, WeChatMessageForwardLogStatus.Success,
                result.StatusCode, result.Body, result.DurationMs, errorMsg: null, ct);
        }
    }

    /// <summary>转发日志状态回写（Success/Fail——Queued 行更新；ErrorMsg 脱敏 1000 不含明文凭证）。</summary>
    private async Task UpdateLogAsync(
        MessageForwardLogEntityDataService logDs, long logId, int status,
        int? statusCode, string? responseBody, long? durationMs, string? errorMsg, CancellationToken ct)
    {
        try
        {
            var log = await logDs.EntityGetAsync(l => l.Id == logId, ct);
            if (log is null) return;
            log.Status = status;
            log.StatusCode = statusCode;
            log.ResponseBody = responseBody is null ? null : Truncate(responseBody, DownstreamForwardClient.ResponseBodyMaxLength);
            log.ErrorMsg = errorMsg is null ? null : Truncate(errorMsg, 1000);
            log.DurationMs = durationMs;
            log.ForwardedAt = DateTime.UtcNow;
            if (status == WeChatMessageForwardLogStatus.Fail)
                log.RetryCount++;
            log.UpdateTime = DateTime.UtcNow;
            await logDs.EntityUpdateAsync(log, ct);
        }
        catch (Exception ex) when (ct.IsCancellationRequested is false)
        {
            // 回写失败 best-effort（不抛——作业已尽力投递；异常消息不含明文凭证）
            _logger?.LogWarning(ex, "微信异步抄送日志回写失败（best-effort）：ForwardLogId={ForwardLogId}", logId);
        }
    }

    /// <summary>截断（列长度上限——防超长列写入失败）。</summary>
    private static string? Truncate(string? value, int max)
        => value is null ? null : (value.Length <= max ? value : value[..max]);
}

/// <summary>
/// 异步抄送作业参数（BackgroundJob 队列 args——方案 §十·三 ④ 字段对齐 + ForwardLogId 关联）。
/// <para>⚠️ <b>public 顶层类型 + 显式公开无参构造</b>（System.Text.Json + Type.GetType AQN 往返必需——
/// 匿名/嵌套类型静默降级 null）；JsonSerializerDefaults.Web = camelCase 属性序列化。</para>
/// </summary>
/// <param name="ChannelId">公众号 channel 实例 id。</param>
/// <param name="EndpointId">命中的下游端点配置 id。</param>
/// <param name="RawXml">消息副本原始 XML（快照——防下游再查微信）。</param>
/// <param name="MsgId">微信 MsgId（可空——事件类无 MsgId）。</param>
/// <param name="FromUserName">发送者 openid。</param>
/// <param name="IdempotencyKey">规范化幂等键（透传下游供其自行幂等——at-least-once 语义）。</param>
/// <param name="ForwardLogId">入队时落的 <see cref="MessageForwardLogEntity"/>（Status=Queued）主键——投递结果回写定位。</param>
public sealed record WeChatMessageForwardJobArgs(
    string ChannelId,
    long EndpointId,
    string RawXml,
    string? MsgId,
    string FromUserName,
    string IdempotencyKey,
    long ForwardLogId)
{
    /// <summary>公开无参构造（System.Text.Json 反序列化必需——JsonSerializerDefaults.Web）。</summary>
    public WeChatMessageForwardJobArgs() : this("", 0, "", null, "", "", 0) { }
}
