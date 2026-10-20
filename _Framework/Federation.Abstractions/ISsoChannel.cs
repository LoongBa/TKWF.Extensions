using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>channel 认证上下文——可扩展键值模型（区别于既有 ProviderAuthenticateContext 扁平 record 缺陷）。</summary>
public sealed record SsoChannelAuthContext(IReadOnlyDictionary<string, string?> Parameters);

/// <summary>channel 认证结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid；成功时）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
/// <param name="AuthLevel">认证强度（int——1=手机号 / 2=微信便捷 / 3=教师核实，对齐 Abstractions SsoAccountDto；不依赖主包 AuthLevel 枚举）。</param>
public sealed record SsoChannelAuthResult(bool Success, string? ExternalUserId, string? FailReason, int AuthLevel);

/// <summary>授权 URL 构造输入（子应用消费方接入 2026-10-11——authorize/start 端点用，ADR-Federation-子应用委托授权URL构造契约化）。</summary>
/// <param name="RedirectUri">授权回调地址（Platform Federation 回调端点——8 平台库零 redirect 配置字段，必须入参）。</param>
/// <param name="State">外部 IdP state 参数（可空 = 引擎/通道自行生成，生成值经 <see cref="SsoChannelAuthorizeResult.State"/> 回传——OIDC 引擎自生成语义）。</param>
/// <param name="Scope">OAuth scope（可空 = 通道默认，如 snsapi_base/get_user_info/auth_base）。</param>
/// <param name="Parameters">可扩展键值参数（WeCom 双流 flow/login_type 选择器等——探索约束 3）。</param>
public sealed record SsoChannelAuthorizeContext(string RedirectUri, string? State, string? Scope, IReadOnlyDictionary<string, string?> Parameters);

/// <summary>授权 URL 构造输出（字段形态对齐引擎 <c>OAuthAuthorizeResult</c>——Url/State/PkceVerifier/PkceChallenge 零转译）。</summary>
/// <param name="Url">授权 URL。</param>
/// <param name="State">实际使用的 state（入参 State 为 null 时引擎/通道生成值回传——回调侧验签衔接）。</param>
/// <param name="PkceVerifier">PKCE verifier（仅 OIDC 系启用；回调侧须能以 <c>Parameters["code_verifier"]</c> 键衔接——探索约束 2/6）。</param>
/// <param name="PkceChallenge">PKCE S256 challenge。</param>
public sealed record SsoChannelAuthorizeResult(string Url, string? State, string? PkceVerifier, string? PkceChallenge);

/// <summary>
/// 事件平台化回复（外部身份联邦 E3 评审 F5）——<c>Content</c> = 响应体（微信 XML 被动回复 text/xml）；
/// 空 <c>Content</c> + "text/xml" = 200 空回复（微信 5 秒超时约束兜底——异步客服消息机制归 Phase 2/消费方）。
/// <para><b>V0.1.1 图文扩展（非破坏）</b>：<see cref="NewsArticles"/> 可空——平台库通道（WeChatEventChannel）
/// 检测非空时构建图文（news）信封（<c>&lt;MsgType&gt;news&lt;/MsgType&gt;</c> + <c>&lt;Articles&gt;</c>），
/// 否则沿用 <see cref="Content"/> 文本信封。handler 返回业务图文结构，信道信封归通道。</para>
/// </summary>
/// <param name="Content">响应体文本（图文场景可留空——<see cref="NewsArticles"/> 承载内容）。</param>
/// <param name="ContentType">Content-Type（text/xml / text/plain...）。</param>
/// <param name="NewsArticles">图文条目列表（可空——非图文回复）。</param>
public sealed record EventReplyResult(
    string Content,
    string ContentType,
    IReadOnlyList<EventReplyNewsArticle>? NewsArticles = null);

/// <summary>图文（news）消息条目——公众号被动回复图文结构（标题/描述/跳转链接/封面图）。</summary>
/// <param name="Title">图文标题。</param>
/// <param name="Description">图文描述（微信显示于标题下方）。</param>
/// <param name="Url">点击跳转链接（用户点击图文后访问）。</param>
/// <param name="PicUrl">封面图 URL（可空——微信默认无图样式）。</param>
public sealed record EventReplyNewsArticle(string Title, string Description, string Url, string? PicUrl = null);

/// <summary>
/// SSO channel 契约（IdP 适配器接口——供平台适配扩展 <c>TKWF.Federation.{平台}</c> 实现）。
/// <para>设计文档 §4.1 channel 抽象：channel_type 决定"身份获取流程"（OAuth 代理 / Issue 票据 / 未来其他）；
/// 与既有 <c>IAuthenticationProvider</c> 的边界（Oracle P2-3）：Provider = 认证中心内部认证方式
/// （服务 token1 签发）；本契约 = 联邦 IdP 适配（外部身份源，服务 token2 联邦流）——两套并存。</para>
/// <para>注册：<c>TryAddEnumerableConstructible</c>（集合版守卫工厂，对齐 ADR92）；平台适配扩展 Initializer
/// 追加实现，未注册通道自然跳过。channel 的 IdP 凭证（如微信 AppId/AppSecret）归平台适配扩展自持（Oracle P2-4）。</para>
/// <para><b>契约归属（I2 契约收口 2026-10-20）</b>：本契约随通道契约收口迁入 Federation.Abstractions——
/// 连接层契约与平台库同程序集命名空间（<c>TKWF.Ext.Federation</c>），信任内核（TrustCenter）回归纯信任，
/// 不再经内核转述通道。TrustCenter.Abstractions 中旧定义已删除（Anson 破坏性迁移：8 平台库 + 消费方
/// <c>using</c> 改 <c>TKWF.Ext.Federation</c>，零行为变更）。</para>
/// </summary>
public interface ISsoChannel : IDomainService
{
    /// <summary>channel 类型（wechat_oauth / wechat_event / google_oidc / ...）。</summary>
    string ChannelType { get; }

    /// <summary>channel 实例 id（微信 = 公众号 id）。</summary>
    string ChannelId { get; }

    /// <summary>身份获取流程（OAuth 代理 / Issue 票据 / 未来其他）——返回渠道身份标识。</summary>
    Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default);

    /// <summary>
    /// 构造授权 URL（子应用消费方接入 2026-10-11——Federation <c>authorize/start</c> 端点调用；ADR-Federation-子应用委托授权URL构造契约化）。
    /// <para><b>默认 null = 通道不支持</b>（端点返回 <c>AUTHORIZE_NOT_SUPPORTED</c>）——既有/未来平台库零强制；模板实例
    /// （ctor <c>ChannelConfig?</c> 为 null 的集合模板）同样返回 null（不可认证语义对齐）。</para>
    /// <para>凭证自持（Oracle P2-4）：通道 ctor <c>ChannelConfig</c> 已含 AppId/AppSecret/Extra——构造 URL 不额外索取凭证。</para>
    /// </summary>
    Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default) => Task.FromResult<SsoChannelAuthorizeResult?>(null);

    /// <summary>
    /// 处理平台事件推送并返回解析结果（I2 语义层——平台库通道验签解密后把原始报文解析为
    /// <see cref="ChannelEvent"/>；编排层经 <c>OrchestrateAsync</c> 消费：记录关注状态 / 派发 handler / 触发域事件）。
    /// <para><b>DIM 默认 <see cref="ChannelEventOutcome.NotApplicable"/> = 非事件通道零强制</b>（对齐
    /// <see cref="BuildAuthorizeUrlAsync"/> DIM 模式——既有/未来平台库零改动）；事件通道（如 <c>wechat_event</c>）实现：
    /// 验签（信任根一票否决——失败返回 <see cref="ChannelEventOutcome.Rejected"/>）+ AES 解密 → 提取
    /// ExternalUserId/MsgType/Event → <see cref="ChannelEventOutcome.Parsed"/>（微信 5 秒超时约束——回复信封归 handler 层）。</para>
    /// <para>返回 <see cref="ChannelEventOutcome.NotApplicable"/> = 非事件（编排跳过）；<see cref="ChannelEventOutcome.Rejected"/>
    /// = 信任根失败（编排短路：不记录、不回复、不回退 handler）。</para>
    /// </summary>
    Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
        => Task.FromResult(ChannelEventOutcome.NotApplicable);

    /// <summary>
    /// URL 验证握手（微信后台服务器配置验证契约——GET 请求；装配层 HandleEvent 分派）。
    /// <para><b>DIM 默认 null = 通道不支持 URL 验证</b>（镜像 <see cref="HandleEventAsync"/> DIM 模式——非事件通道/
    /// 无验证契约的平台库零强制）。事件通道（如 <c>wechat_event</c>）实现：双模式验签（安全 msg_signature /
    /// 明文 signature）+ 解密 echostr → 回显明文（text/plain）。</para>
    /// <para>返回：验签+解密成功 → 回显明文 echostr；任何失败（参数缺失/验签失败/解密失败/不支持）→ null
    /// （装配层映射 400——微信后台统一显示"token 验证失败"，不区分具体原因）。</para>
    /// </summary>
    Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}