using System;
using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号消息网关配置（<c>TKWF:WeChatOperations:Gateway</c> 独立子节——方案 §十·六 ② 配置分层）。
/// <para>配置分层（AGENTS §8）：领域安全/转发决策（<see cref="GatewayEnabled"/> fail-closed 默认关、
/// 幂等保留期/超时、异步重试/退避、同步超时上限）归本节——Web 层 <see cref="WeChatOperationsEndpointOptions"/>
/// （<c>:Web</c>）不存在关闭领域行为的对应开关（防表现层绕过）；配置节挂 <c>TKWF:WeChatOperations</c> 根下保内聚
/// （对齐 Federation <c>:ChannelRegistry</c>/<c>:Web</c> 拆节先例）。</para>
/// <para><see cref="GatewayEnabled"/> <b>默认 false（fail-closed）</b>——未开全局开关/未配规则时既有
/// <see cref="WeChatOperationsReplyHandler"/> 行为零回归（RouteAsync 首行判定直接返回未接管，不查库不转发）。</para>
/// </summary>
[Options("TKWF:WeChatOperations:Gateway")]
public class WeChatMessageGatewayOptions
{
    /// <summary>网关全局开关（默认 false——fail-closed 默认关：未启用时 <see cref="MessageGatewayService.RouteAsync"/>
    /// 直接返回未接管，既有 handler 零回归；开启后路由规则/幂等/转发生效）。</summary>
    public bool GatewayEnabled { get; set; } = false;

    /// <summary>幂等保留天数（默认 7——覆盖微信全部重试窗口 + 对账余量；<c>0/负数</c> = 不清理；
    /// 仅清理 Forwarded/Skipped 终态记录，Pending 由 <see cref="PendingTimeoutMinutes"/> 独立超时兜底——方案 P1-5）。</summary>
    public int IdempotencyRetentionDays { get; set; } = 7;

    /// <summary>幂等 Pending 超时分钟（默认 5——极端场景下 Pending 超时后允许新请求复用键重建 Pending+转发，
    /// 防陈旧 Pending 数据丢失窗口；方案 P1-5）。</summary>
    public int PendingTimeoutMinutes { get; set; } = 5;

    /// <summary>异步抄送投递重试次数（默认 3——BackgroundJob 队列 MaxRetryCount；下游必须幂等——
    /// <c>IdempotencyKey</c> 透传供其自行去重，方案 §十·三 ③ at-least-once 语义）。</summary>
    public int AsyncForwardRetryCount { get; set; } = 3;

    /// <summary>异步抄送失败退避秒数（默认 2——指数退避 <c>2^RetryCount</c> 秒基础值）。</summary>
    public int BackoffSeconds { get; set; } = 2;

    /// <summary>同步透传默认超时秒数（默认 2——微信 5 秒时限留 1 秒本地兜底余量；<b>上限强制 ≤3</b>，
    /// 配置超限拒收 fail-fast（方案 §十·二 ②——防运维误配吞掉兜底窗口）。V0.3.0 SyncProxy 落地——
    /// 端点 TimeoutSeconds 缺省回落本节（端点 0/负 → 本节值）。</summary>
    public int DefaultSyncTimeoutSeconds { get; set; } = 2;
}

/// <summary>
/// 消息转发日志状态常量（<see cref="MessageForwardLogEntity.Status"/> 值域——1..6，对齐
/// <see cref="WeChatReplySceneTypes"/> 静态 int 常量风格；方案 §十·一 ④ 字段 12）。
/// </summary>
public static class WeChatMessageForwardLogStatus
{
    /// <summary>已入队（异步抄送 BackgroundJob 待投递）。</summary>
    public const int Queued = 1;

    /// <summary>成功（投递成功回写）。</summary>
    public const int Success = 2;

    /// <summary>失败（重试超限/出站异常）。</summary>
    public const int Fail = 3;

    /// <summary>超时（同步透传端点超时——V0.3.0 SyncProxy 落地：CancelAfter 触发 Timeout 分支）。</summary>
    public const int Timeout = 4;

    /// <summary>重复跳过（幂等命中——微信重推回调静默跳过留痕，审计对账可见）。</summary>
    public const int SkippedDuplicate = 5;

    /// <summary>无匹配规则（首见消息无路由规则命中）。</summary>
    public const int NoRule = 6;
}

/// <summary>
/// 消息幂等状态常量（<see cref="WeChatMessageIdempotencyEntity.LastStatus"/> 值域——1..3；
/// 方案 §十·四 ②：1=Pending 处理中 / 2=Forwarded 已转发 / 3=Skipped 已跳过（重复/无规则））。
/// </summary>
public static class WeChatMessageIdempotencyStatus
{
    /// <summary>处理中（建键占位——防并发双转发）。</summary>
    public const int Pending = 1;

    /// <summary>已转发/已处理（P1-2：事务提交即迁——语义=已入队待投递，投递状态归 <see cref="MessageForwardLogEntity"/>）。</summary>
    public const int Forwarded = 2;

    /// <summary>已跳过（重复回调/无规则——键存在即判决，不依赖状态精确值）。</summary>
    public const int Skipped = 3;
}
