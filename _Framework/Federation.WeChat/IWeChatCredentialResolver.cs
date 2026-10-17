using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信凭证解析契约（方案 docs/WeChatOperations/微信公众号运营扩展-开发方案.md §3.2.2——
/// 凭证来源演进链 Options → DB 加密 → 未来 KMS/Secret Manager，可插拔契约是必然抽象方向；Oracle P2-4
/// 凭证自持语义保持——凭证从 <see cref="WeChatOptions.Channels"/> 或扩展注册的 DB/KMS 实现解析，不经
/// Authentication <c>IPlatformCredentialService</c>）。
/// <para>契约落平台库（凭证来源是平台库演进方向，扩展只负责注册实现）。扩展注册 DB 实现时：
/// <c>TryAddEnumerableConstructible&lt;IWeChatCredentialResolver, DbWeChatCredentialResolver&gt;</c>
/// ——DB 命中 → 解密返回；未命中 → 委托缺省 Options 实现（DB 权威 + 降级两源并存）。</para>
/// <para>返回 <c>null</c> = 未命中（调用方 <see cref="WeChatApiClient"/> 抛出凭证未配置异常——语义与
/// 既有 <c>ResolveCredential</c> 一致）。</para>
/// </summary>
public interface IWeChatCredentialResolver
{
    /// <summary>按 <paramref name="appId"/> 精确解析微信 channel 凭证（未命中 → null）。</summary>
    Task<WeChatChannelConfig?> ResolveAsync(string appId, CancellationToken ct = default);
}

/// <summary>
/// 缺省实现（读 <see cref="WeChatOptions.Channels"/> 按 AppId 精确匹配——现状 <c>ResolveCredential</c>
/// 语义迁移；Oracle P2-4 凭证自持）。<b>DI 注册缺省实现</b>（<c>AddWeChatFederationChannels()</c> 内
/// <c>TryAddSingleton</c>——Oracle P1：ctor 收非空 <see cref="IWeChatCredentialResolver"/>，不做 ctor 内 fallback）。</summary>
public sealed class OptionsWeChatCredentialResolver(IOptions<WeChatOptions> options) : IWeChatCredentialResolver
{
    /// <inheritdoc />
    public Task<WeChatChannelConfig?> ResolveAsync(string appId, CancellationToken ct = default)
    {
        var channels = options.Value.Channels;
        if (channels is { Count: > 0 })
        {
            foreach (var channel in channels)
            {
                if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                    return Task.FromResult<WeChatChannelConfig?>(channel);
            }
        }

        return Task.FromResult<WeChatChannelConfig?>(null);
    }
}
