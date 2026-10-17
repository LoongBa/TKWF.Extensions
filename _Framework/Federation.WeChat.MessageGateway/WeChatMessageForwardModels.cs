namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 转发动作（<see cref="ForwardDecision.Action"/> 取值——方案 §十·〇 ① 转发决策）。
/// </summary>
public enum ForwardAction
{
    /// <summary>本地处理不转发。</summary>
    LocalOnly = 1,

    /// <summary>本地处理 + 异步抄送（Outbox 事件登记，与业务原子）。</summary>
    AsyncCopy = 2,

    /// <summary>同步透传接管本次回复（转发下游 XML 原样作被动回复）。</summary>
    SyncProxy = 3,

    /// <summary>前置处理 + 同步透传。</summary>
    PreprocessSync = 4,

    /// <summary>降级——回退本地兜底（同步透传失败后走本地渲染链，方案 §十·二 ③ 降级链）。</summary>
    LocalFallback = 5,
}

/// <summary>
/// 降级链阶段（方案 §十·二 ③——同步失败后依次尝试 ①本地回复规则 → ②template 兜底 → ③DefaultReplyText）。
/// <para>运行库只做<b>决策步进</b>（<see cref="WeChatMessageForwardDecider.NextDegradeStage"/>），不执行渲染。</para>
/// </summary>
public enum ForwardDegradeStage
{
    /// <summary>无降级（正常动作）。</summary>
    None = 0,

    /// <summary>①本地回复规则（复用既有 handler 本地分支——规则→素材→文本）。</summary>
    LocalRender = 1,

    /// <summary>②template 兜底（Channel.Extra["news_reply"]）。</summary>
    Template = 2,

    /// <summary>③Options.DefaultReplyText（降级链末端）。</summary>
    DefaultReplyText = 3,
}

/// <summary>
/// 转发决策结果（运行库只产出决策，不执行转发/渲染——决策步进由消费方按 <see cref="DegradeStage"/> 执行）。
/// </summary>
/// <param name="Action">决策动作（LocalOnly/AsyncCopy/SyncProxy/PreprocessSync/LocalFallback）。</param>
/// <param name="DegradeStage">降级链阶段（<see cref="ForwardAction.LocalFallback"/> 时指示回退到哪一级；否则 None）。</param>
/// <param name="EndpointId">参与转发的下游端点引用（LocalOnly 恒空；转发类策略取规则端点）。</param>
public sealed record ForwardDecision(ForwardAction Action, ForwardDegradeStage DegradeStage, long? EndpointId)
{
    /// <summary>是否降级（Action == LocalFallback——同步透传失败或未知转发模式）。</summary>
    public bool IsDegraded => Action == ForwardAction.LocalFallback;
}
