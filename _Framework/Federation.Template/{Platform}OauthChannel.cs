using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}OAuth 通道（`ISsoChannel: {platform}_oauth`）——平台网页授权身份获取。
/// <para>流程：装配层构造 authorize URL（归装配层——state 生成/会话绑定/回调校验归装配层/消费方，
/// 本通道不感知 state）→ 授权回调带 code → 本通道 <see cref="AuthenticateAsync"/> 经
/// <see cref="{Platform}ApiClient.GetExternalUserIdAsync"/> code 换 external_uid → 返回
/// <see cref="SsoChannelAuthResult"/>（ExternalUserId=external_uid），归一 <c>(channel_id, external_uid) → uid</c>
/// （编排层/装配层）。</para>
/// <para>注册：库扩展方法 <c>Add{Platform}FederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）——
/// 集合元素 ctor 注入 IDomainUser 由帧内 CurrentAopUser 供给；channel 业务参数（ChannelId/凭证）经 Options 注入
/// 不占 ctor IDomainUser 槽。<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。
/// 本实现继承 <see cref="DomainServiceBase"/>（经基类 <c>User</c> 取上下文——tkwf-extension §4.3 铁律）。</para>
/// </summary>
[DiContractIgnore]
public sealed class {Platform}OauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly {Platform}ApiClient _api;
    private readonly {Platform}ChannelConfig _channel;

    /// <summary>构造——channel 由配置选区（Channels 首项为默认；多实例按 <see cref="ISsoChannel.ChannelId"/> 选区归后续迭代）。</summary>
    public {Platform}OauthChannel(
        IDomainUser user,
        {Platform}ApiClient api,
        IOptions<{Platform}Options> options)
        : base(user)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _channel = options?.Value.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException($"{PlatformName} OAuth channel 未配置：TKWF:Federation:{Namespace} 节 Channels 为空");
    }

    /// <inheritdoc />
    public string ChannelType => "{platform}_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel.ChannelId;

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, $"{PlatformName}_CODE_REQUIRED", 0);

        try
        {
            // code 是平台 OAuth 授权码（与发起授权的 AppId 绑定）——按本 channel 的 AppId 换 external_uid
            var externalUserId = await _api.GetExternalUserIdAsync(_channel.AppId, code, ct);
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
