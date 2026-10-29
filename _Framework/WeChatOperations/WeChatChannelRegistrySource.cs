using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信通道 DB 注册表源（D03 §4.2 供给层——Priority=100 DB 权威）。
/// <para>投影（方案 §8.3）：<c>ChannelId/Alias/AppId/IsEnabled</c> 公共列 +
/// <c>ChannelForm→Extra["channel_form"]</c>、<c>Verification→Extra["verification"]</c>、
/// <b><c>Token/EncodingAESKey</c> 解密为明文进 Extra</b>（与静态源 <c>WeChatChannelSource</c> 同语义——
/// <c>WeChatEventChannel</c> 验签直接消费明文；密文进 Extra 会使 DB 通道事件验签恒失败）。
/// <c>AppSecret</c> 仍不进公共列（出站经 <see cref="DbWeChatCredentialResolver"/> 解密解析）。</para>
/// <para>取数：经 <see cref="IUserBoundChannelSource"/> 由 <c>CompositeChannelRegistry</c> 构造时绑定域用户，
/// 再以 <c>IDomainUser.Use&lt;DataService&gt;()</c> 直建读取（DataService 的 DI 注册为 throw-factory，
/// 不可 <c>IServiceProvider</c> 直取）。未绑定 → 空列表 + Warning（fail-closed 降级静态源）。</para>
/// <para><c>GetChannels()</c> 为同步契约，DB 读取同步阻塞（配置行少，调用点恒在用户帧内）；读取/解密失败均
/// Warning 留痕后降级，不抛出——单来源故障不阻断组合枚举。</para>
/// </summary>
public sealed class WeChatChannelRegistrySource : IChannelSource, IUserBoundChannelSource
{
    private const int DbPriority = 100;

    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<WeChatChannelRegistrySource> _logger;

    private IDomainUser? _user;
    private WeChatChannelConfigEntityDataService? _ds;

    private WeChatChannelConfigEntityDataService Ds => _ds ??= (_user
        ?? throw new InvalidOperationException(
            $"{nameof(WeChatChannelRegistrySource)} 未绑定 DomainUser——应由 CompositeChannelRegistry 构造时绑定。"))
        .Use<WeChatChannelConfigEntityDataService>();

    public WeChatChannelRegistrySource(
        [FromKeyedServices(WeChatOperationsKeyProviderKeys.WeChatOperations)] ISymmetricKeyProvider keys,
        ILogger<WeChatChannelRegistrySource> logger)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void BindDomainUser(IDomainUser user)
        => _user = user ?? throw new ArgumentNullException(nameof(user));

    /// <inheritdoc />
    public string PlatformType => "wechat";

    /// <inheritdoc />
    /// <remarks>per-channel 能力经 <see cref="ChannelConfig.Capabilities"/> 派生；此处为源级兜底声明。</remarks>
    public ChannelCapabilities Capabilities => ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
        | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu;

    /// <inheritdoc />
    /// <remarks>数值高者优先——DB 权威恒先于静态源（默认 0）。</remarks>
    public int Priority => DbPriority;

    /// <inheritdoc />
    /// <remarks>全行投影（含 disabled——S3 降级判据；B 守卫消费侧按 IsEnabled 过滤活跃数）。</remarks>
    public IReadOnlyList<ChannelConfig> GetChannels()
    {
        if (_user is null)
        {
            _logger.LogWarning(
                "WeChatChannelRegistrySource 未绑定 DomainUser（非 CompositeChannelRegistry 装配链）——跳过 DB 通道来源，降级静态源。");
            return [];
        }

        var entities = GetEntitiesSync();
        var list = new List<ChannelConfig>(entities.Count);
        // ChannelId 序投影——默认选区/枚举确定性（P1-3：无排序时同 Priority 首项依回执行序漂移）
        entities.Sort((a, b) => StringComparer.Ordinal.Compare(a.ChannelId, b.ChannelId));
        foreach (var e in entities)
        {
            list.Add(new ChannelConfig
            {
                ChannelId = e.ChannelId,
                PlatformType = PlatformType,
                AppId = e.AppId,
                AppSecret = null,
                Alias = e.Alias,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Token"] = DecryptOrNull(e.ChannelId, e.TokenCipher),
                    ["EncodingAESKey"] = DecryptOrNull(e.ChannelId, e.EncodingAESKeyCipher),
                    ["channel_form"] = e.ChannelForm,
                    ["verification"] = e.Verification,
                },
                IsEnabled = e.IsEnabled,
                Capabilities = WeChatChannelCapabilityResolver.Resolve(e.ChannelForm, e.Verification),
            });
        }
        return list;
    }

    private string? DecryptOrNull(string channelId, string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return null;

        try
        {
            return _keys.Decrypt(cipher);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 解密失败 → null（下游验签 fail-closed）；异常消息不含明文
            _logger.LogWarning(
                "通道凭证解密失败（ChannelId={ChannelId}）——该通道事件验签将 fail-closed：{Message}",
                channelId, ex.Message);
            return null;
        }
    }

    private List<WeChatChannelConfigEntity> GetEntitiesSync()
    {
        try
        {
            return Ds.EntitySelectAsync(null, 0, 1000, null, default).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            // 守卫/DI 装配错误（DataService 未注册、User 帧缺失等）——Error 级（非瞬时故障），仍降级避免阻断组合
            _logger.LogError(ex,
                "WeChatChannelRegistrySource 装配错误（DataService 解析失败）——本来源降级为空列表，请检查 DI 接线");
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "WeChatChannelRegistrySource 读取 DB 通道配置失败——本来源降级为空列表：{Message}", ex.Message);
            return [];
        }
    }
}
