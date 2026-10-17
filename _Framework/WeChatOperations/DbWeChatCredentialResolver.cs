using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// DB 优先微信凭证解析器（两源并存接入点 ①——设计文档 §3.3.4：DB 命中 → AES-GCM 解密返回；
/// 未命中 → 委托缺省 Options 实现（<see cref="WeChatOptions.Channels"/> 按 AppId 精确匹配——现状语义降级））。
/// <para>⚠️ 契约 <see cref="IWeChatCredentialResolver"/> 为平台库契约<b>非 IDomainService</b>（DB 命中解密返回 +
/// 未命中回退 Options——try-fail-到-options 降级矩阵，Oracle P2-4 凭证自持语义保持）——<b>接线型</b>
/// （tkwf-extension §4.8 #7：ctor 收 IServiceProvider + Options + keyed 密钥，普通 DI TryAddScoped 注册；
/// DataService 经 <c>sp.GetRequiredService&lt;WeChatChannelConfigEntityDataService&gt;()</c> C1 延迟解析——
/// ⚠️ C1 帧内限制：本 resolver 出站调用在用户帧内（门面/事件 handler）执行，DataService 解析/读取合法）。</para>
/// <para>注册形态：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IWeChatCredentialResolver,
/// DbWeChatCredentialResolver&gt;())</c>（⚡ 注意：并非 TryAddEnumerableConstructible——契约非 IDomainService，
/// 普通 DI 集合；平台库 <c>AddWeChatFederationChannels()</c> 已注册缺省 Options 实现（TryAddSingleton）——
/// <b>本实现必须注册在 Options 实现之后</b>（集合遍历取最后注册，DB 优先语义），Initializer 注册顺序保证）。</para>
/// <para>解密失败（密文损坏/密钥不匹配）→ 异常消息<b>不含明文</b>（Oracle P1 异常消息安全五补充）。
/// 凭证空（未配置）→ null（调用方 WeChatApiClient 抛"凭证未配置"——语义与 Options 缺省一致）。</para>
/// </summary>
public sealed class DbWeChatCredentialResolver : IWeChatCredentialResolver
{
    private readonly IServiceProvider _sp;
    private readonly IOptions<WeChatOptions> _options;
    private readonly ISymmetricKeyProvider _keys;

    private WeChatChannelConfigEntityDataService? _ds;
    private WeChatChannelConfigEntityDataService Ds => _ds ??= _sp.GetRequiredService<WeChatChannelConfigEntityDataService>();

    public DbWeChatCredentialResolver(
        IServiceProvider sp,
        IOptions<WeChatOptions> options,
        [FromKeyedServices(WeChatOperationsKeyProviderKeys.WeChatOperations)] ISymmetricKeyProvider keys)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <inheritdoc />
    public async Task<WeChatChannelConfig?> ResolveAsync(string appId, CancellationToken ct = default)
    {
        // ① DB 命中（按 AppId 精确匹配——出站凭证解析键）→ AES-GCM 解密返回
        var entity = await Ds.EntityGetAsync(e => e.AppId == appId, ct);
        if (entity is not null)
        {
            var config = new WeChatChannelConfig
            {
                ChannelId = entity.ChannelId,
                AppId = entity.AppId,
                AppSecret = Decrypt(entity.AppSecretCipher),
                Token = Decrypt(entity.TokenCipher),
                EncodingAESKey = Decrypt(entity.EncodingAESKeyCipher),
            };
            // 明文解密仅出站瞬时——空密文（未配置）→ null（调用方抛凭证未配置）
            if (string.IsNullOrWhiteSpace(config.AppSecret))
                return null;
            return config;
        }

        // ② 未命中 → 降级 Options（缺省实现语义——Channels 按 AppId 精确匹配；现状行为迁移）
        var channels = _options.Value.Channels;
        if (channels is { Count: > 0 })
        {
            foreach (var channel in channels)
            {
                if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                    return channel;
            }
        }
        return null;
    }

    /// <summary>AES-GCM 单段密文解密——空密文返回空串；解密失败异常消息不含明文（Oracle P1 安全 5）。</summary>
    private string Decrypt(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return string.Empty;
        try
        {
            return _keys.Decrypt(cipher);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("微信凭证解析失败（密文解密失败——密钥不匹配或密文损坏）", ex);
        }
    }
}