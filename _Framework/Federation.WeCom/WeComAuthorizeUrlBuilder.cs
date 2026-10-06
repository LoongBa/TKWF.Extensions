using System;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信 authorize URL 构造辅助（静态纯字符串——Oracle 评审 P2-4：降装配层双流 URL 出错风险）。
/// <para>双授权流 URL 模板（Oracle 评审 P1-3——两流构造差异归装配层，库只收 code）：</para>
/// <para>① <b>内置客户端 Webview 跳转</b>：<c>open.weixin.qq.com/connect/oauth2/authorize</c>
/// ——<c>#wechat_redirect</c> 尾缀<b>仅 Webview 流必填</b>；<c>agentid</c> 仅 <c>snsapi_privateinfo</c> 必填
/// （P1-5）；scope 由调用方传入（装配层决定 snsapi_base/privateinfo，<see cref="WeComOptions.DefaultScope"/> 兜底）；</para>
/// <para>② <b>桌面端扫码</b>：<c>login.work.weixin.qq.com/wwlogin/sso/login</c>
/// ——<c>login_type=CorpApp|ServiceApp</c> 区分自建/三方（<c>@wecom/jssdk</c> <c>ww.createWWLoginPanel</c> 内嵌组件
/// 对应形态），无 <c>#wechat_redirect</c>。</para>
/// </summary>
public static class WeComAuthorizeUrlBuilder
{
    /// <summary>
    /// 内置客户端 Webview 跳转 authorize URL（企业微信客户端内网页授权）。
    /// </summary>
    /// <param name="corpId">企业 CorpID（三方 = SuiteID）。</param>
    /// <param name="redirectUri">回调地址（须编码前完整可信域名——回调域名完全匹配否则 50001）。</param>
    /// <param name="scope">授权 scope（snsapi_base 静默 / snsapi_privateinfo 敏感增强）。</param>
    /// <param name="state">CSRF 防伪参数（会话绑定归装配层）。</param>
    /// <param name="agentId">应用 AgentId（snsapi_privateinfo 必填——P1-5 fail-fast 前置）。</param>
    public static string BuildWebviewUrl(string corpId, string redirectUri, string scope, string state, string? agentId = null)
    {
        if (string.IsNullOrEmpty(corpId)) throw new ArgumentException("corpId 不能为空", nameof(corpId));
        if (string.IsNullOrEmpty(redirectUri)) throw new ArgumentException("redirectUri 不能为空", nameof(redirectUri));
        if (string.IsNullOrEmpty(scope)) throw new ArgumentException("scope 不能为空", nameof(scope));
        if (string.IsNullOrEmpty(state)) throw new ArgumentException("state 不能为空", nameof(state));

        var agent = string.IsNullOrEmpty(agentId) ? "" : $"&agentid={Uri.EscapeDataString(agentId)}";
        return $"https://open.weixin.qq.com/connect/oauth2/authorize?appid={Uri.EscapeDataString(corpId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(scope)}&state={Uri.EscapeDataString(state)}{agent}#wechat_redirect";
    }

    /// <summary>
    /// 桌面端扫码 authorize URL（login.work.weixin.qq.com——<c>@wecom/jssdk</c> 内嵌组件/302 跳转对应形态）。
    /// </summary>
    /// <param name="corpId">企业 CorpID（三方 = SuiteID）。</param>
    /// <param name="agentId">应用 AgentId（扫码流参数）。</param>
    /// <param name="redirectUri">回调地址。</param>
    /// <param name="state">CSRF 防伪参数。</param>
    /// <param name="loginType">login_type——CorpApp（自建）/ ServiceApp（三方）。</param>
    public static string BuildScanUrl(string corpId, string agentId, string redirectUri, string state, string loginType = "CorpApp")
    {
        if (string.IsNullOrEmpty(corpId)) throw new ArgumentException("corpId 不能为空", nameof(corpId));
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentException("agentId 不能为空", nameof(agentId));
        if (string.IsNullOrEmpty(redirectUri)) throw new ArgumentException("redirectUri 不能为空", nameof(redirectUri));
        if (string.IsNullOrEmpty(state)) throw new ArgumentException("state 不能为空", nameof(state));

        return $"https://login.work.weixin.qq.com/wwlogin/sso/login?login_type={Uri.EscapeDataString(loginType)}&appid={Uri.EscapeDataString(corpId)}&agentid={Uri.EscapeDataString(agentId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
    }
}
