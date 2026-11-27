using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 通道在用守卫实现（WeChatOperations 侧）——检查运营<b>配置</b>是否引用该通道：回复规则 / 菜单快照 / 菜单版本 /
/// 网关路由规则 / 下游端点（删通道会语义悬空，与 DeleteMaterialAsync 引用保护同型）。
/// <para>日志类（交互/发布/转发日志、幂等键）为历史/瞬态，不作删除门禁——否则已下线通道永不可清理。
/// 经 <c>TryAddEnumerableConstructible&lt;IChannelUsageGuard, X&gt;()</c> 注册。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WeChatOperationsChannelUsageGuard : DomainServiceBase, IChannelUsageGuard
{
    public WeChatOperationsChannelUsageGuard(IDomainUser user) : base(user) { }

    /// <inheritdoc />
    public async Task<ChannelUsageCheck> CheckAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return new ChannelUsageCheck(false);

        if (await User.Use<ReplyRuleEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在回复规则");
        if (await User.Use<MenuSnapshotEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在菜单快照");
        if (await User.Use<MenuVersionEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在菜单版本");
        if (await User.Use<MessageRouteRuleEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在网关路由规则");
        if (await User.Use<DownstreamEndpointEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在下游端点");

        return new ChannelUsageCheck(false);
    }
}
