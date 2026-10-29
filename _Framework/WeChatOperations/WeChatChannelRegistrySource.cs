using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信通道 DB 注册表源（D03 §4.2 供给层——1.7 改造，替代 <c>DbWeChatChannelSource</c>：Priority=100 高优先
/// DB 权威 + ChannelForm/Verification/Capabilities 投影 + Alias 双键 + 全行投影（含 disabled——S3 降级判据））。
/// <para>投影（方案 §8.3 映射表）：<c>ChannelId→ChannelId</c> / <c>Alias→Alias</c> /
/// <c>ChannelForm→Extra["channel_form"]</c> / <c>Verification→Extra["verification"]</c> /
/// <c>AppId→AppId</c> / <c>IsEnabled→IsEnabled</c>（<b>全行投影</b>——disabled 行保留，消费侧
/// <c>B 守卫 Where(IsEnabled)</c> 过滤活跃数 + S3 降级命中 disabled 返回提示）；派生能力 →
/// <see cref="ChannelConfig.Capabilities"/>（per-channel 动态覆盖 DIM——方案 §4.3 确认点 A）。</para>
/// <para>⚠️ 凭证安全姿态（Oracle P1 安全 3，保留现状）：<b>AppSecret 密文不进公共列</b>（投影 null，
/// 出站凭证经 <see cref="DbWeChatCredentialResolver"/> 解密路径单独解析）；Token/EncodingAESKey <b>密文进 Extra</b>
/// （不解密——事件验签信任根经 resolver 解密；密文不进明文 Extra 防泄露）。投影仅声明通道存在性/选区，
/// 与事件验签热路径的凭证获取解耦。</para>
/// <para>⚠️ <see cref="IChannelSource.GetChannels"/> 为<b>同步契约</b>——DB 读取经 DataService
/// <c>EntitySelectAsync</c> 同步阻塞（<c>.GetAwaiter().GetResult()</c>）：调用点恒在用户帧内
/// （CompositeChannelRegistry 经 <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析 → 源集合枚举），无死锁；
/// 配置行少（公众号 scale）阻塞可忽略。DB 不可用 → 空列表（Composite 已按来源 try/catch 跳过）。</para>
/// <para>注册：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IChannelSource, WeChatChannelRegistrySource&gt;())</c>
/// ——普通 DI 集合；<b>Priority=100 语义替代「Insert(0) 前置注册序」</b>（D03 §8.2——组合按 Priority 降序首命中，
/// DB 源恒先于静态源（默认 0）与 Federation 主包 SsoChannelRegistryEntity 系源（0，停用新装配））。</para>
/// </summary>
public sealed class WeChatChannelRegistrySource : IChannelSource
{
    private const int DbPriority = 100;

    private readonly IServiceProvider _sp;

    private WeChatChannelConfigEntityDataService? _ds;
    private WeChatChannelConfigEntityDataService Ds => _ds ??= _sp.GetRequiredService<WeChatChannelConfigEntityDataService>();

    public WeChatChannelRegistrySource(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    /// <inheritdoc />
    public string PlatformType => "wechat";

    /// <inheritdoc />
    /// <remarks>DIM 覆盖——per-channel 能力经 <see cref="ChannelConfig.Capabilities"/> 派生填充（源级声明仅兜底）。</remarks>
    public ChannelCapabilities Capabilities => ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
        | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu;

    /// <inheritdoc />
    /// <remarks>数值高者优先——DB 权威（WeChatOperations）恒先于静态源/停用源（D03 §8.4 条件 5 定稿）。</remarks>
    public int Priority => DbPriority;

    /// <inheritdoc />
    /// <remarks>全行投影（含 disabled——S3 降级判据；B 守卫消费侧 Where(IsEnabled) 过滤活跃数）。</remarks>
    public IReadOnlyList<ChannelConfig> GetChannels()
    {
        // IChannelSource 同步契约 + DataService 异步——同步阻塞（帧内调用无死锁；见类注释）
        var entities = GetEntitiesSync();
        var list = new List<ChannelConfig>(entities.Count);
        foreach (var e in entities)
        {
            list.Add(new ChannelConfig
            {
                ChannelId = e.ChannelId,
                PlatformType = PlatformType,
                AppId = e.AppId,
                AppSecret = null,   // 凭证密文不进公共列（Oracle P1 安全 3——出站解密走 resolver）
                Alias = e.Alias,    // 1.6 alias 双键（对外名投影）
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    // 凭证密文承载——仅声明通道存在；验签信任根经 resolver 解密（见类注释）
                    ["Token"] = e.TokenCipher,
                    ["EncodingAESKey"] = e.EncodingAESKeyCipher,
                    // D03 §4.1 通道形态/认证状态（方案 §8.3 投影映射表——Extra 承载，不挤 FA 公共列）
                    ["channel_form"] = e.ChannelForm,
                    ["verification"] = e.Verification,
                },
                IsEnabled = e.IsEnabled,   // 全行投影（含 disabled——S3 降级判据）
                Capabilities = WeChatChannelCapabilityResolver.Resolve(e.ChannelForm, e.Verification),   // 派生不落库（§4.3）
            });
        }
        return list;
    }

    private List<WeChatChannelConfigEntity> GetEntitiesSync()
    {
        try
        {
            return Ds.EntitySelectAsync(null, 0, 1000, null, default).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB 不可用 → 空列表（Composite 已按来源容错跳过——降级静态源）
            return [];
        }
    }
}