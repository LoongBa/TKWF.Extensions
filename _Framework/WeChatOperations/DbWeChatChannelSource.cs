using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// DB 优先微信通道来源（两源并存接入点 ②——设计文档 §3.3.4：DB 读 <see cref="WeChatChannelConfigEntity"/>
/// → <see cref="ChannelConfig"/> 投影注册为 <see cref="IChannelSource"/> 集合元素——CompositeChannelRegistry
/// 组合语义：DB 源优先 → Options 源（平台库 WeChatChannelSource）回退）。
/// <para>⚠️ <see cref="IChannelSource.GetChannels"/> 为<b>同步契约</b>（平台库 Options 投影同契约）——DB 读取
/// 经 DataService <c>EntityGetAsync</c> 同步阻塞（<c>.GetAwaiter().GetResult()</c>）：调用点恒在用户帧内
/// （CompositeChannelRegistry 经 <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析 → StaticChannelRegistry 枚举来源），
/// 无 SynchronizationContext（ASP.NET Core）→ 无死锁；配置行少（公众号 scale）阻塞可忽略。DB 不可用 → 空列表
/// （StaticChannelRegistry.GetAllAsync 已按来源 try/catch 跳过——对齐仓库降级矩阵惯例）。</para>
/// <para>投影（对齐平台库 WeChatChannelSource）：<c>ChannelId→ChannelId</c> / <c>AppId→AppId</c> /
/// <c>IsEnabled→IsEnabled</c> / <c>ChannelType→PlatformType="wechat"</c>；<b>Token/EncodingAESKey 密文不解密
/// —Extra 承载</b>（密文进 Extra——DB 权威投影仅声明通道存在性/选区，事件验签信任根经 DbWeChatCredentialResolver
/// 解密出站侧解析；不进 Extra 明文防泄露）。⚠️ Oracle P1 安全 3：AppSecret 密文<b>不进</b>公共列
/// （ChannelConfig.AppSecret——投影置空，出站凭证经 resolver 解密路径单独解析）。</para>
/// <para>注册：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IChannelSource, DbWeChatChannelSource&gt;())</c>
/// ——普通 DI 集合（契约非 IDomainService）；⚠️ **DB 源须在 Options 源之前注册**（CompositeChannelRegistry
/// 遍历顺序 = 注册顺序，DbWeChatChannelSource 注册在前才保证"DB 优先"——§九 验收 7，Initializer 注册顺序保证）。</para>
/// </summary>
public sealed class DbWeChatChannelSource : IChannelSource
{
    private readonly IServiceProvider _sp;

    private WeChatChannelConfigEntityDataService? _ds;
    private WeChatChannelConfigEntityDataService Ds => _ds ??= _sp.GetRequiredService<WeChatChannelConfigEntityDataService>();

    public DbWeChatChannelSource(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    /// <inheritdoc />
    public string PlatformType => "wechat";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
    {
        // IChannelSource 同步契约 + DataService 异步——同步阻塞（帧内调用无死锁；见类注释）
        var entities = GetEntitiesSync();
        var list = new List<ChannelConfig>(entities.Count);
        foreach (var e in entities)
        {
            if (!e.IsEnabled)
                continue;   // 禁用通道不投影（对齐 WeChatChannelSource 选区语义——IsEnabled 前端过滤）
            list.Add(new ChannelConfig
            {
                ChannelId = e.ChannelId,
                PlatformType = PlatformType,
                AppId = e.AppId,
                AppSecret = null,   // 凭证密文不进公共列（Oracle P1 安全 3——出站解密走 resolver）
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    // Token/EncodingAESKey 密文承载——仅声明通道存在；验签信任根经 resolver 解密（见类注释）
                    ["Token"] = e.TokenCipher,
                    ["EncodingAESKey"] = e.EncodingAESKeyCipher,
                },
                IsEnabled = true,
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
            // DB 不可用 → 空列表（StaticChannelRegistry 已按来源容错跳过——降级 Options）
            return [];
        }
    }
}