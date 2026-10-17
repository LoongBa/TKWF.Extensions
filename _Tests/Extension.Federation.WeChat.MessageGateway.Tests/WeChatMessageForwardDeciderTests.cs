using System.Threading.Tasks;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Federation.WeChat.MessageGateway.Tests;

/// <summary>
/// <see cref="WeChatMessageForwardDecider"/> 转发决策 + 降级链单测——纯逻辑直测。
/// 覆盖：四转发策略（LocalOnly/AsyncCopy/SyncProxy/PreprocessSync）/ 同步失败降级 / 未知模式 fail-safe / 降级链步进。
/// </summary>
public class WeChatMessageForwardDeciderTests
{
    private const long Ep1 = 101;

    private static MessageRouteRuleDescriptor Rule(int forwardMode, long? endpointId = Ep1)
        => new(Id: 1, "ch-main", null, null, WeChatMessageMatchModes.None, null, forwardMode, endpointId, 0);

    // ---------- 四转发策略 ----------

    [Fact]
    public void Decide_LocalOnly_ReturnsLocalOnly_IgnoresSyncResult()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.LocalOnly, endpointId: null), syncSucceeded: true);

        Assert.Equal(ForwardAction.LocalOnly, decision.Action);
        Assert.Equal(ForwardDegradeStage.None, decision.DegradeStage);
        Assert.False(decision.IsDegraded);
    }

    [Fact]
    public void Decide_AsyncCopy_ReturnsAsyncCopy_IgnoresSyncResult()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.AsyncCopy), syncSucceeded: false);

        Assert.Equal(ForwardAction.AsyncCopy, decision.Action);
        Assert.Equal(ForwardDegradeStage.None, decision.DegradeStage);
        Assert.False(decision.IsDegraded);
    }

    [Fact]
    public void Decide_SyncProxy_Success_ReturnsSyncProxy()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.SyncProxy), syncSucceeded: true);

        Assert.Equal(ForwardAction.SyncProxy, decision.Action);
        Assert.Equal(ForwardDegradeStage.None, decision.DegradeStage);
        Assert.False(decision.IsDegraded);
        Assert.Equal(Ep1, decision.EndpointId);
    }

    [Fact]
    public void Decide_SyncProxy_Failure_DegradesToLocalFallback()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.SyncProxy), syncSucceeded: false);

        Assert.Equal(ForwardAction.LocalFallback, decision.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, decision.DegradeStage);
        Assert.True(decision.IsDegraded);
    }

    [Fact]
    public void Decide_PreprocessSync_Success_ReturnsPreprocessSync()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.PreprocessSync), syncSucceeded: true);

        Assert.Equal(ForwardAction.PreprocessSync, decision.Action);
        Assert.Equal(ForwardDegradeStage.None, decision.DegradeStage);
        Assert.False(decision.IsDegraded);
    }

    [Fact]
    public void Decide_PreprocessSync_Failure_DegradesToLocalFallback()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(WeChatMessageForwardModes.PreprocessSync), syncSucceeded: false);

        Assert.Equal(ForwardAction.LocalFallback, decision.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, decision.DegradeStage);
        Assert.True(decision.IsDegraded);
    }

    // ---------- 未知模式 fail-safe ----------

    [Fact]
    public void Decide_UnknownForwardMode_FailsSafeToLocalFallback()
    {
        var decision = WeChatMessageForwardDecider.Decide(Rule(forwardMode: 99), syncSucceeded: true);

        Assert.Equal(ForwardAction.LocalFallback, decision.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, decision.DegradeStage);
        Assert.True(decision.IsDegraded);
    }

    // ---------- 异步签名 ----------

    [Fact]
    public async Task DecideAsync_ReturnsSameDecisionAsSync()
    {
        var decision = await WeChatMessageForwardDecider.DecideAsync(
            Rule(WeChatMessageForwardModes.SyncProxy), syncSucceeded: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(ForwardAction.LocalFallback, decision.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, decision.DegradeStage);
    }

    // ---------- 降级链步进（决策步进，不执行渲染） ----------

    [Fact]
    public void NextDegradeStage_Steps_LocalRenderToTemplate()
    {
        Assert.Equal(ForwardDegradeStage.Template,
            WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.LocalRender));
    }

    [Fact]
    public void NextDegradeStage_Steps_TemplateToDefaultReplyText()
    {
        Assert.Equal(ForwardDegradeStage.DefaultReplyText,
            WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.Template));
    }

    [Fact]
    public void NextDegradeStage_EndOfChain_ReturnsNone()
    {
        Assert.Equal(ForwardDegradeStage.None,
            WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.DefaultReplyText));
        Assert.Equal(ForwardDegradeStage.None,
            WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.None));
    }

    [Fact]
    public void NextDegradeStage_FullChain_ThreeSteps()
    {
        // 降级链全步进：LocalRender → Template → DefaultReplyText → None（链尽）
        var stage = ForwardDegradeStage.LocalRender;

        stage = WeChatMessageForwardDecider.NextDegradeStage(stage);
        Assert.Equal(ForwardDegradeStage.Template, stage);

        stage = WeChatMessageForwardDecider.NextDegradeStage(stage);
        Assert.Equal(ForwardDegradeStage.DefaultReplyText, stage);

        stage = WeChatMessageForwardDecider.NextDegradeStage(stage);
        Assert.Equal(ForwardDegradeStage.None, stage);
    }
}
