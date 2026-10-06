using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信（WeCom）channel 配置（单应用实例——按 <see cref="ChannelId"/> 区分多应用）。
/// <para>凭证自持（Oracle P2-4）：channel 凭证归平台适配库自持——开发明文 appsettings.json 配置；
/// 生产 AES-GCM 密文或装配注入（K8s secret mount），CorpSecret/EncodingAESKey 永不明文进配置库。</para>
/// <para><see cref="CorpSecret"/>：应用级/通讯录级 corpsecret（<b>缓存键 = corpid + ":" + corpsecret——Oracle 评审
/// P1-2：应用级 vs 通讯录级 token 互不覆盖</b>）；三方应用 = SuiteSecret（<see cref="CorpId"/> = SuiteId）。</para>
/// <para><see cref="IsThirdParty"/>：三方应用标志（默认 false 自建）——决定 getuserinfo 端点
/// （自建 <c>/cgi-bin/auth/getuserinfo</c> vs 三方 <c>/cgi-bin/auth/getuserinfo3rd</c>）+ external_uid 双策略
/// （三方成员 <c>open_userid</c> 全局唯一 / 自建成员 <c>{CorpId}:{userid}</c> 复合——Oracle 评审 P0-1）
/// + 事件回调 receiveid 期望（自建 corpid / 三方 suiteid——P1-1）。</para>
/// <para><see cref="EnableSensitiveInfo"/>（Oracle 评审 P1-5）：启用 <c>snsapi_privateinfo</c> 敏感信息增强
/// （user_ticket 1800s 即用即弃不落库）——启用且 <see cref="AgentId"/> 缺 → 启动 fail-fast（channel 构造抛）。</para>
/// </summary>
public class WeComChannelConfig
{
    /// <summary>channel 实例 id（企业微信应用 id——<c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>企业 CorpID（自建应用）/ SuiteID（三方应用——<c>appid</c> 参数与回调 receiveid 期望）。</summary>
    public string CorpId { get; set; } = "";

    /// <summary>应用 CorpSecret（自建）/ SuiteSecret（三方）——gettoken 换取企业 access_token（缓存键成分）。</summary>
    public string CorpSecret { get; set; } = "";

    /// <summary>应用 AgentId（<c>snsapi_privateinfo</c> 授权必填——启用敏感信息时 fail-fast 校验；扫码流参数）。</summary>
    public string AgentId { get; set; } = "";

    /// <summary>是否三方应用（默认 false 自建）——端点/双策略/receiveid 期望切换（Oracle 评审 P0-1/P1-1）。</summary>
    public bool IsThirdParty { get; set; }

    /// <summary>是否启用 snsapi_privateinfo 敏感信息增强（user_ticket 即用即弃）——启用且 AgentId 缺 → fail-fast（P1-5）。</summary>
    public bool EnableSensitiveInfo { get; set; }

    /// <summary>回调验签令牌（事件/通讯录推送 <c>msg_signature</c> 用——T5 入站信任根）。</summary>
    public string Token { get; set; } = "";

    /// <summary>消息加解密密钥 EncodingAESKey（T5 回调 AES 解密用——43 字符，Base64Decode 派生 32 字节 AESKey）。</summary>
    public string EncodingAESKey { get; set; } = "";
}

/// <summary>
/// 企业微信网关库配置选项（<c>TKWF:Federation:WeCom</c> 节——Channels 列表按应用区分 + DefaultScope 兜底）。
/// <para>[Options("TKWF:Federation:WeCom")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 WeChatOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。</para>
/// <para><see cref="DefaultScope"/>（Oracle 评审 P1-5）：scope 选取归装配层（authorize URL 构造时决定
/// snsapi_base/privateinfo）——本配置作兜底默认（缺省 <c>snsapi_base</c> 静默仅 userid）。</para>
/// </summary>
[Options("TKWF:Federation:WeCom")]
public class WeComOptions
{
    /// <summary>默认授权 scope（兜底——装配层构造 authorize URL 未显式指定时使用；snsapi_base 静默仅 userid）。</summary>
    public string DefaultScope { get; set; } = "snsapi_base";

    /// <summary>channel 配置列表（多应用实例——每项一个 ChannelId/CorpId/CorpSecret/AgentId/Token/EncodingAESKey）。</summary>
    public List<WeComChannelConfig> Channels { get; set; } = [];
}
