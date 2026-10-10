using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝开放平台 OAuth 通道（`ISsoChannel: alipay_oauth`）——支付宝身份获取（出站-only 形态，N4 T3 + 多通道联邦 v0.3.0）。
/// <para>流程：装配层构造支付宝 authorize URL（<c>openauth.alipay.com/oauth2/publicAppAuthorize.htm</c>——app_id +
/// scope（auth_base/auth_user）+ redirect_uri + state；**authorize 构造归装配层、通道不感知 state——Oracle P1-1**，
/// 三授权形态（跳转/扫码/H5 JSAPI）在端点处全部收敛到回调 code）→ 授权回调带 auth_code →
/// 本通道 <see cref="AuthenticateAsync"/> 经 <see cref="AlipayApiClient"/> auth_code→user_id（RSA2 双向签名换取链，
/// 用户级 token 不缓存——对齐 QQ P1-5）→ 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=user_id，
/// **应用维度唯一稳定映射键——open_id 灰度过渡兼容，N4 §二**），归一 <c>(channel_id, user_id) → uid</c>
/// （编排层/装配层）。AuthLevel=2。</para>
/// <para><b>多通道联邦（v0.3.0 选区机制，Oracle M1/M2/M8）</b>：ctor 收 <see cref="ChannelConfig"/>
/// （<see cref="ISsoChannelFactory"/> 预取传入——POCO 非域服务规避 DI004 与 DomainHost 依赖）；凭证
/// （AppId 公共列 + Extra 承载 PrivateKeyPath/AlipayPublicKeyPath/EnableMobile——RSA 私钥路径进 Extra 非
/// AppSecret 列，非对称/对称分离，方案 F5 零表结构变更）。<paramref name="channel"/> 为 null = 集合模板实例
/// （工厂类型索引源，不直接认证）。</para>
/// <para>注册：库扩展方法 <c>AddAlipayFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）+
/// <see cref="AlipayChannelSource"/>（IChannelSource——静态配置投影统一 ChannelConfig）。</para>
/// </summary>
[DiContractIgnore]
public sealed class AlipayOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly AlipayApiClient _api;
    private readonly AlipaySignService _signService;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入）；null = 集合模板实例。</summary>
    public AlipayOauthChannel(
        IDomainUser user,
        AlipayApiClient api,
        AlipaySignService signService,
        ChannelConfig? channel = null)
        : base(user)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _signService = signService ?? throw new ArgumentNullException(nameof(signService));
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "alipay_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0);

        // 回调参数：支付宝仅保证 auth_code/app_id/scope 有效（回跳参数本身无签名——N4 §3.2 安全重心在签名换取链 + state）。
        // auth_code 语义映射为 context "code" 键（对齐 QQ "code" 先例；装配层把支付宝回调 auth_code 注入该键）
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "ALIPAY_CODE_REQUIRED", 0);

        try
        {
            // redirect_uri 校验（N4 P1-2 防开放重定向）：⚠️ 支付宝 code→token 换取不经 redirect_uri（顶层参数无此字段——
            // librarian 协议事实）——本通道仅校验装配层在授权时决定的 redirect_uri 随回调注入 context（与支付宝
            // 控制台回调配置一致性比对归装配层/消费方，对齐 QQ P1-4 防开放重定向语义）。
            if (!context.Parameters.TryGetValue("redirect_uri", out var redirectUri)
                || string.IsNullOrWhiteSpace(redirectUri))
                return new SsoChannelAuthResult(false, null, "ALIPAY_REDIRECT_URI_REQUIRED", 0);

            // 支付宝换取链（RSA2 双向签名，一次性，用户级 token 不缓存 P1-5）：auth_code→access_token
            // ExternalUserId = user_id（应用维度唯一稳定映射键——open_id 灰度过渡兼容，N4 §二）
            var token = await _api.GetAccessTokenAsync(_channel.AppId!, code, ct);
            return new SsoChannelAuthResult(true, token.UserId, null, 2);  // AuthLevel=2 平台便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or HttpRequestException
            or JsonException
            or FormatException
            or CryptographicException
            or IOException) // IOException：私钥/公钥 PEM 文件缺失（生产 fail-fast 缺钥拒——通道层转失败结果不崩端点）
        {
            // 支付宝 API 业务错误 / 网络错误 / 非 JSON 响应 / 验签失败 / 密钥缺失——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }
}
