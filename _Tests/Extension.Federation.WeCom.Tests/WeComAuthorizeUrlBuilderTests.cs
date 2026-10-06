using System;
using TKWF.Federation.WeCom;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// <see cref="WeComAuthorizeUrlBuilder"/> 测试——双授权流 URL 模板（Oracle 评审 P1-3/P2-4）：
/// Webview <c>#wechat_redirect</c> 尾缀 vs 桌面扫码 <c>login_type</c> 参数；agentid 条件拼接（P1-5）。
/// </summary>
public class WeComAuthorizeUrlBuilderTests
{
    [Fact]
    public void BuildWebviewUrl_ContainsAllParams_WithWechatRedirectSuffix()
    {
        var url = WeComAuthorizeUrlBuilder.BuildWebviewUrl(
            corpId: "ww_test_corp",
            redirectUri: "https://app.loongba.cn/sso/wecom/callback",
            scope: "snsapi_base",
            state: "abc123");

        Assert.StartsWith("https://open.weixin.qq.com/connect/oauth2/authorize?", url);
        Assert.Contains("appid=ww_test_corp", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapp.loongba.cn%2Fsso%2Fwecom%2Fcallback", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=snsapi_base", url);
        Assert.Contains("state=abc123", url);
        Assert.EndsWith("#wechat_redirect", url);  // Webview 流尾缀必填（P1-3）
        Assert.DoesNotContain("agentid=", url);    // 非 privateinfo 场景不拼 agentid
    }

    [Fact]
    public void BuildWebviewUrl_WithAgentId_AppendsAgent()
    {
        var url = WeComAuthorizeUrlBuilder.BuildWebviewUrl(
            corpId: "ww_test_corp", redirectUri: "https://app.loongba.cn/cb", scope: "snsapi_privateinfo", state: "s1", agentId: "1000002");

        Assert.Contains("scope=snsapi_privateinfo", url);
        Assert.Contains("agentid=1000002", url);   // snsapi_privateinfo 必填 AgentId（P1-5）
        Assert.EndsWith("#wechat_redirect", url);
    }

    [Fact]
    public void BuildScanUrl_ContainsLoginType_NoWechatRedirect()
    {
        var url = WeComAuthorizeUrlBuilder.BuildScanUrl(
            corpId: "ww_test_corp",
            agentId: "1000002",
            redirectUri: "https://app.loongba.cn/sso/wecom/callback",
            state: "abc123");

        Assert.StartsWith("https://login.work.weixin.qq.com/wwlogin/sso/login?", url);
        Assert.Contains("login_type=CorpApp", url);            // 自建应用（P1-3：login_type 区分 CorpApp/ServiceApp）
        Assert.Contains("appid=ww_test_corp", url);
        Assert.Contains("agentid=1000002", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapp.loongba.cn%2Fsso%2Fwecom%2Fcallback", url);
        Assert.Contains("state=abc123", url);
        Assert.DoesNotContain("#wechat_redirect", url);        // 扫码流无尾缀（P1-3）
    }

    [Fact]
    public void BuildScanUrl_ServiceApp_LoginType()
    {
        var url = WeComAuthorizeUrlBuilder.BuildScanUrl("ww_test_corp", "1000002", "https://app.loongba.cn/cb", "s1", loginType: "ServiceApp");
        Assert.Contains("login_type=ServiceApp", url);          // 三方应用（P1-3）
    }

    [Fact]
    public void Build_EmptyParams_Throws()
    {
        Assert.Throws<ArgumentException>(() => WeComAuthorizeUrlBuilder.BuildWebviewUrl("", "https://x/cb", "snsapi_base", "s"));
        Assert.Throws<ArgumentException>(() => WeComAuthorizeUrlBuilder.BuildScanUrl("ww", "1000002", "", "s"));
        Assert.Throws<ArgumentException>(() => WeComAuthorizeUrlBuilder.BuildScanUrl("ww", "", "https://x/cb", "s"));
    }
}
