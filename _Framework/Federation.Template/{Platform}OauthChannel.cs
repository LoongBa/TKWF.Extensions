using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}OAuth 通道（`ISsoChannel: {platform}_oauth`）——平台网页授权身份获取（多通道联邦 v0.3.0）。
/// <para>流程：装配层构造 authorize URL（归装配层——state 生成/会话绑定/回调校验归装配层/消费方，
/// 本通道不感知 state）→ 授权回调带 code → 本通道 <see cref="AuthenticateAsync"/> 经
/// <see cref="{Platform}ApiClient.GetExternalUserIdAsync"/> code 换 external_uid → 返回
/// <see cref="SsoChannelAuthResult"/>（ExternalUserId=external_uid），归一 <c>(channel_id, external_uid) → uid</c>
/// （编排层/装配层）。</para>
/// <para><b>多通道联邦（v0.3.0，方案 docs/Federation/多通道联邦-开发方案.md）</b>：通道 ctor 收
/// <see cref="ChannelConfig"/>（工厂经 <see cref="ISsoChannelFactory"/> 预取传入——POCO 非域服务，
/// 规避 DI004 与 DomainHost 依赖；Oracle M1/M8 意图保持——凭证在 AuthenticateAsync 使用）。
/// <paramref name="channel"/> 为 null = 集合模板实例（<see cref="SsoChannelFactory"/> 类型索引用，不直接认证）。</para>
/// <para>注册：库扩展方法 <c>Add{Platform}FederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）+
/// <c>IChannelSource</c>（<see cref="{Platform}ChannelSource"/>——强类型配置投影统一
/// <see cref="ChannelConfig"/>，经 <see cref="IChannelRegistry"/> 聚合选区）。</para>
/// </summary>
[DiContractIgnore]
public sealed class {Platform}OauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly {Platform}ApiClient _api;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入）；null = 集合模板实例。</summary>
    public {Platform}OauthChannel(
        IDomainUser user,
        {Platform}ApiClient api,
        ChannelConfig? channel = null)
        : base(user)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "{platform}_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0);

        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, $"{PlatformName}_CODE_REQUIRED", 0);

        try
        {
            // code 是平台 OAuth 授权码（与发起授权的 AppId 绑定）——按本 channel 的 AppId 换 external_uid
            var externalUserId = await _api.GetExternalUserIdAsync(_channel.AppId!, code, ct);
            return new SsoChannelAuthResult(true, externalUserId, null, 2);  // AuthLevel=2 平台便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // 平台 API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }

    private static string PlatformName => "{Namespace}";
}
