using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 微信消息转发决策器（纯逻辑——方案 §十·〇 ① 运行库，转发决策 + 降级链决策）。
/// <para>输入命中规则 + 同步透传结果，产出 <see cref="ForwardDecision"/>（动作 + 降级链阶段），
/// <b>不执行转发/渲染</b>——降级链为<b>决策步进</b>（<see cref="NextDegradeStage"/>），渲染归扩展/消费方。</para>
/// <para>降级链语义（方案 §十·二 ③）：SyncProxy/PreprocessSync 失败 → LocalFallback（本地渲染）→
/// template 兜底 → DefaultReplyText——运行库只步进阶段，末端后返回 None 表示链尽。</para>
/// </summary>
public static class WeChatMessageForwardDecider
{
    /// <summary>
    /// 转发决策（异步签名——保持与门面调用一致的异步形态；纯逻辑无 IO，返回已完成 ValueTask）。
    /// </summary>
    /// <param name="rule">命中的路由规则描述。</param>
    /// <param name="syncSucceeded">同步透传（SyncProxy/PreprocessSync）是否成功——仅同步策略参与；其余策略忽略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>决策结果。</returns>
    public static ValueTask<ForwardDecision> DecideAsync(
        MessageRouteRuleDescriptor rule,
        bool syncSucceeded,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Decide(rule, syncSucceeded));
    }

    /// <summary>
    /// 转发决策（同步形态——纯函数直测入口）。
    /// </summary>
    /// <param name="rule">命中的路由规则描述。</param>
    /// <param name="syncSucceeded">同步透传是否成功（仅 SyncProxy/PreprocessSync 参与）。</param>
    /// <returns>决策结果。</returns>
    public static ForwardDecision Decide(MessageRouteRuleDescriptor rule, bool syncSucceeded)
    {
        ArgumentNullException.ThrowIfNull(rule);

        switch (rule.ForwardMode)
        {
            case WeChatMessageForwardModes.LocalOnly:
                // 本地处理不转发——同步结果不参与
                return new ForwardDecision(ForwardAction.LocalOnly, ForwardDegradeStage.None, rule.EndpointId);

            case WeChatMessageForwardModes.AsyncCopy:
                // 本地处理 + 异步抄送——不阻塞响应，同步结果不参与
                return new ForwardDecision(ForwardAction.AsyncCopy, ForwardDegradeStage.None, rule.EndpointId);

            case WeChatMessageForwardModes.SyncProxy:
                // 同步透传接管本次回复：成功 → 透传；失败 → 降级本地渲染链
                return syncSucceeded
                    ? new ForwardDecision(ForwardAction.SyncProxy, ForwardDegradeStage.None, rule.EndpointId)
                    : new ForwardDecision(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, rule.EndpointId);

            case WeChatMessageForwardModes.PreprocessSync:
                // 前置处理 + 同步透传：成功 → 透传；失败 → 降级本地渲染链
                return syncSucceeded
                    ? new ForwardDecision(ForwardAction.PreprocessSync, ForwardDegradeStage.None, rule.EndpointId)
                    : new ForwardDecision(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, rule.EndpointId);

            default:
                // 未知转发模式：fail-safe 降级本地兜底（本地渲染链起步——不阻断回复）
                return new ForwardDecision(ForwardAction.LocalFallback, ForwardDegradeStage.LocalRender, rule.EndpointId);
        }
    }

    /// <summary>
    /// 降级链步进（决策步进，不执行渲染）：LocalRender → Template → DefaultReplyText；
    /// 已到 <see cref="ForwardDegradeStage.DefaultReplyText"/>（链末端）或 None 时返回 None（链尽，消费方停止步进）。
    /// </summary>
    /// <param name="stage">当前降级阶段。</param>
    /// <returns>下一降级阶段；链尽返回 <see cref="ForwardDegradeStage.None"/>。</returns>
    public static ForwardDegradeStage NextDegradeStage(ForwardDegradeStage stage)
        => stage switch
        {
            ForwardDegradeStage.LocalRender => ForwardDegradeStage.Template,
            ForwardDegradeStage.Template => ForwardDegradeStage.DefaultReplyText,
            _ => ForwardDegradeStage.None,
        };
}
