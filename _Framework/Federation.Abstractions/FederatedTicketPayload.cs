using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦免登票据载荷（D03 §6.1 定稿——Federation 域专用，AB 签票 → CD 兑付的唯一耦合点）。
/// <para><b>与框架 Core <see cref="TKW.Framework.Domain.FederatedIdentity"/> 不互替（D03 A7/P2）</b>：
/// FederatedIdentity 是框架通用「已可信外部身份」POCO（<c>DomainUser.LoginAsFederatedAsync</c> 路径、
/// 子应用委托面 <c>SsoSubAppBridge</c>）；本载荷是 Federation 域票据契约（provider/channelType/channelAlias/
/// platformUid/assurance 语义），仅经 <c>ISealedTicketService&lt;T&gt;</c> 泛型中间人密封（T3 默认 / T1 预留）。</para>
/// <para><b>字段白名单制</b>（A7 定稿）：明确不含 mid/tenant（AB 不认识租户——业务链接里的 mid 由业务模板决定）、
/// targetRoute（URL 由业务模板构造）、profile（展示档案不入票——O1 定稿，C 侧按需调 AuthCenter 档案接口）。</para>
/// <para><b>payload 字节预算（D03 评审 R9）</b>：11 字段 JSON 序列化 ≈ 300–500B &lt; SealedTicket 上限 4068B
/// （TrustCenter <c>MaxPayloadBytes = 4096-12-16</c>）；扩展字段须复查预算（见 <see cref="FederatedTicketPayloadBudget"/>）。</para>
/// <para><b>验证约束</b>（D03 §6.1 取值矩阵）：<see cref="Provider"/> 开放注册表字符串（wechat/alipay/douyin...——
/// 与 <see cref="ChannelConfig.PlatformType"/> 同构）；<see cref="ChannelType"/> 为<b>通道形态</b>（mp_subscription/
/// mp_service/mini_program/work_wechat——各 provider 定义合法值，见 <see cref="ChannelTypeRegistry"/>），
/// <b>不是</b>协议通道类型（wechat_oauth/wechat_event——不入票，经 <see cref="Extensions"/>["protocol_type"] 双写承载）。
/// ⚠️ 命名碰撞警示（Oracle6 条件 4）：payload.ChannelType 取「通道形态」；协议类型经 Extensions 承载。</para>
/// <para>不可变性：sealed record + <see cref="StringComparer.Ordinal"/> 扩展字典（Oracle6 条件 E——构造后不可改，
/// 兑付侧直接 Redeem 还原，无 TOCTOU）。</para>
/// </summary>
public sealed record FederatedTicketPayload(
    /// <summary>开放注册表 provider（wechat / alipay / douyin / ...——开放注册表不枚举死，新 provider 零 DDL）。</summary>
    string Provider,
    /// <summary>通道形态（mp_subscription / mp_service / mini_program / work_wechat...——D03 §6.1 channelType 语义；合法值见 <see cref="ChannelTypeRegistry"/>；<b>非协议通道类型</b>）。</summary>
    string ChannelType,
    /// <summary>不变键（正式 = 微信唯一 ID gh_xxx；业务绑定只用 (Provider, ChannelId) 不变键——A4）。</summary>
    string ChannelId,
    /// <summary>不可变对外名（slug 语义——A14 定稿；可空 = 未配置；DisplayName 可变不入票）。</summary>
    string? ChannelAlias,
    /// <summary>外部用户标识（openid——外部身份 L1 键之一）。</summary>
    string ExternalUserId,
    /// <summary>跨通道归并键（可空——同 provider 有 UnionId 时跨 channel 归并同人，A1）。</summary>
    string? UnionId,
    /// <summary>L2 平台身份（AB 阶段已分配——A11 事件预置；可空 = 未分配，C 侧映射/建号兜底）。</summary>
    string? PlatformUid,
    /// <summary>档案可信等级（event | base | userinfo——开放字符串，出票方声明；D03 §5.2 AssuranceLevel）。</summary>
    string Assurance,
    /// <summary>签发 Unix 秒（DateTimeOffset.UtcNow.ToUnixTimeSeconds()）。</summary>
    long Iat,
    /// <summary>过期 Unix 秒（签票方组装时 = Iat + TTL）。</summary>
    long Exp,
    /// <summary>单次标识（签发方生成，≥16 字节随机——防重放判据）。</summary>
    string Jti)
{
    /// <summary>
    /// 平台附加属性扩展字典（开放承载——不破坏白名单制）。已知键：
    /// <c>"protocol_type"</c> —— 协议通道类型（wechat_oauth/wechat_event...，签票侧双写；兑付侧
    /// <see cref="FederatedTicketPayloadExtensions.ToFederatedIdentity"/> 优先取此值，缺失回退形态映射表）；
    /// <c>"is_test"</c> —— 测试地址票标记（D03 R5——preview/test-url 端点防滥用）。
    /// </summary>
    public IReadOnlyDictionary<string, string?> Extensions { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}

/// <summary>
/// payload 字节预算常量（D03 评审 R9——当前 11 字段 ≈ 300–500B，SealedTicket 上限 4068B 余量充足；
/// 扩展字段须复查预算，禁止无记录扩字段）。
/// </summary>
public static class FederatedTicketPayloadBudget
{
    /// <summary>防御线（单元测试锁定：序列化后的 payload 字节数须 &lt; 本值——当前实测约 500B 内）。</summary>
    public const int MaxPayloadBytes = 1024;

    /// <summary>SealedTicket 后端硬上限（TrustCenter MaxPayloadBytes——契约层只读权威值，供预算注释引用）。</summary>
    public const int SealedTicketHardLimit = 4096 - 12 - 16;
}

/// <summary>
/// payload → 框架通用身份的适配转换（Oracle6 条件 E/P2——单点转换，测试锁定字段映射）。
/// <para>消费方：<c>ExternalTicketLoginService</c>（兑付 payload → Orchestrator 输入）。
/// Orchestrator 签名保持 <see cref="TKW.Framework.Domain.FederatedIdentity"/>（P2 未含 Orchestrator——AuthCenter ⟂
/// Federation 依赖方向；payload 为 Federation 域载荷，编排吃框架通用身份）。</para>
/// </summary>
public static class FederatedTicketPayloadExtensions
{
    /// <summary>协议通道类型承载键（Extensions 字典键）。</summary>
    public const string ProtocolTypeKey = "protocol_type";

    /// <summary>测试票标记键（Extensions 字典键——D03 R5）。</summary>
    public const string IsTestKey = "is_test";

    /// <summary>
    /// 通道形态 → 默认协议类型回退映射（仅兼容旧票/未双写场景——协议类型值缺省时按形态推断）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ProtocolTypeFallback =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mp_service"] = "wechat_oauth",
            ["mp_subscription"] = "wechat_event",
            ["mini_program"] = "wechat_oauth",
            ["work_wechat"] = "wechat_event",
        };

    /// <summary>
    /// payload → FederatedIdentity 适配转换（字段映射：UId=PlatformUid??"" / ChannelId / ExternalUserId /
    /// ChannelType=Extensions["protocol_type"] ?? 形态回退——D03 §5.1a 决策；AuthLevel=2 联邦快捷恒定——
    /// assurance 细粒度（base/userinfo）不入 AuthLevel 槽，留 token claim 层）。
    /// </summary>
    public static TKW.Framework.Domain.FederatedIdentity ToFederatedIdentity(this FederatedTicketPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var protocolType = payload.Extensions?.TryGetValue(ProtocolTypeKey, out var pt) == true && !string.IsNullOrEmpty(pt)
            ? pt
            : (ProtocolTypeFallback.TryGetValue(payload.ChannelType, out var fallback) ? fallback : null);
        return new TKW.Framework.Domain.FederatedIdentity(
            UId: payload.PlatformUid ?? string.Empty,
            ChannelId: payload.ChannelId,
            ExternalUserId: payload.ExternalUserId,
            ChannelType: protocolType,
            AuthLevel: 2,
            Extensions: payload.Extensions);
    }
}