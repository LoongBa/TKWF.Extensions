using System;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台} authorize URL 构造辅助（静态纯字符串——**可选组件，WeCom 先例**）。
/// <para>模板取舍（N1 定案）：WeChat 先例无此组件（authorize 构造归装配层）；仅当平台 authorize URL
/// 构造复杂（多流/多形态/特殊尾缀）时仿 WeCom 增设——降装配层双流 URL 出错风险。
/// 单流平台（WeChat/QQ）authorize 构造归装配层即可，删除本文件。</para>
/// <para>WeCom 先例形态：双授权流 URL 模板（Webview <c>#wechat_redirect</c> 尾缀仅 Webview 流必填；
/// 扫码 <c>login_type=CorpApp|ServiceApp</c>）；参数 fail-fast 校验 + <c>Uri.EscapeDataString</c> 编码。</para>
/// </summary>
public static class {Platform}AuthorizeUrlBuilder
{
    /// <summary>
    /// 构造授权跳转 authorize URL。
    /// <para>⚠️ 平台差异：authorize 端点 URL / 必需参数 / scope / 特殊尾缀（#wechat_redirect）/
    /// 扫码与跳转双流差异——协议事实先 librarian 官方文档核实。</para>
    /// </summary>
    /// <param name="clientId">平台 ClientId（AppId）。</param>
    /// <param name="redirectUri">回调地址（须编码前完整可信域名——回调域名完全匹配否则平台报错）。</param>
    /// <param name="state">CSRF 防伪参数（会话绑定归装配层）。</param>
    /// <param name="scope">授权 scope（装配层决定，默认值兜底）。</param>
    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string scope = "default")
    {
        if (string.IsNullOrEmpty(clientId)) throw new ArgumentException("clientId 不能为空", nameof(clientId));
        if (string.IsNullOrEmpty(redirectUri)) throw new ArgumentException("redirectUri 不能为空", nameof(redirectUri));
        if (string.IsNullOrEmpty(state)) throw new ArgumentException("state 不能为空", nameof(state));

        // ⚠️ 平台差异：端点 URL + 参数名（response_type=code + client_id + redirect_uri + state + scope + display）
        return $"https://graph.{platform}.com/oauth2.0/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(scope)}";
    }
}
