using System;
using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.RateLimiting;

/// <summary>分区方式。</summary>
public enum RateLimitPartition
{
    /// <summary>不分区（全局限流器对所有请求共享同一配额）。</summary>
    None,

    /// <summary>按客户端 IP 分区（<see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/>）。</summary>
    Ip,

    /// <summary>按当前用户分区（HttpContext.User ClaimsPrincipal 解析；匿名 fallback IP）。</summary>
    User
}

/// <summary>
/// Web 层限流配置（<c>TKWF:RateLimiting</c> 配置节）。
/// <para>SG1 <see cref="OptionsAttribute"/> 声明 + 本扩展 <c>AddOptions().BindConfiguration</c> 双通道绑定；
/// 编程式 <c>AddTkfwRateLimiting(configure)</c> 回调覆盖配置节。</para>
/// </summary>
[Options("TKWF:RateLimiting")]
public sealed class RateLimitingOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "TKWF:RateLimiting";

    /// <summary>
    /// 全局策略（所有端点兜底；<see cref="EndpointPolicies"/> 精确命中优先）。
    /// </summary>
    public RateLimitPolicyModel Global { get; set; } = new();

    /// <summary>
    /// 分区方式（Ip=RemoteIpAddress / User=当前用户 ID，匿名 fallback IP / None=全局共享）。
    /// 默认 <see cref="RateLimitPartition.Ip"/>。
    /// </summary>
    public RateLimitPartition Partition { get; set; } = RateLimitPartition.Ip;

    /// <summary>
    /// 端点级策略覆盖（路径 → 策略模型；如 <c>/api/auth/login</c> 更严格）。
    /// <para>匹配规则：<b>精确匹配</b>（Oracle P2-2）——仅整路径完全一致命中，未命中回退
    /// <see cref="Global"/> 策略；v0.1.0 不支持通配符前缀匹配（v0.2.0 评估）。</para>
    /// </summary>
    public Dictionary<string, RateLimitPolicyModel> EndpointPolicies { get; set; } = new();

    /// <summary>
    /// 拒绝状态码（默认 429——对齐 Domain 层 RateLimitException 语义）。
    /// </summary>
    public int RejectionStatusCode { get; set; } = 429;

    /// <summary>拒绝时写 <c>Retry-After</c> 响应头（默认 true）。</summary>
    public bool RetryAfter { get; set; } = true;
}