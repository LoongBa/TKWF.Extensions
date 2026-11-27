using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 通道在用守卫实现（WeChatOperations 侧）——检查运营<b>有效配置</b>是否引用该通道：回复规则 /
/// 菜单快照（仅 <c>MenuJson</c> 非空即"有菜单内容"）/ 网关路由规则 / 下游端点（删通道会语义悬空，与 DeleteMaterialAsync 引用保护同型）。
/// <para><b>显式放行（不门禁）</b>：菜单版本（回滚历史，无清理入口——门禁会致"发布过菜单即永不可删"）、
/// 交互/发布/转发日志、幂等键（历史/瞬态）——避免已下线通道永不可清理。菜单快照按内容判定，
/// 故查看空菜单、或已清空菜单（`MenuService.DeleteAsync` 置空 MenuJson）后放行。</para>
/// <para>经 <c>TryAddEnumerableConstructible&lt;IChannelUsageGuard, X&gt;()</c> 注册。</para>
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
        if (await User.Use<MenuSnapshotEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId && e.MenuJson != "", ct) is not null)
            return new ChannelUsageCheck(true, "存在菜单内容");
        if (await User.Use<MessageRouteRuleEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在网关路由规则");
        if (await User.Use<DownstreamEndpointEntityDataService>().EntityGetAsync(e => e.ChannelId == channelId, ct) is not null)
            return new ChannelUsageCheck(true, "存在下游端点");

        return new ChannelUsageCheck(false);
    }
}
