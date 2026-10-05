using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信公众平台 channel 配置（单公众号实例——按 <see cref="ChannelId"/> 区分多公众号）。
/// <para>凭证自持（Oracle P2-4）：channel 凭证归平台适配库自持，**不复用** Authentication
/// <c>IPlatformCredentialService</c>——开发明文 appsettings.json 配置；生产 AES-GCM 密文或装配注入
/// （K8s secret mount），AppSecret 永不明文进配置库。</para>
/// </summary>
public class WeChatChannelConfig
{
    /// <summary>channel 实例 id（公众号 id——T4/T5 <c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>公众号 AppId（凭证解析键——WeChatApiClient 按 AppId 精确匹配）。</summary>
    public string AppId { get; set; } = "";

    /// <summary>公众号 AppSecret（生产 AES-GCM 密文或装配注入，永不明文进配置库）。</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>公众号令牌（事件推送验签 <c>msg_signature</c> 用——T5 入站信任根）。</summary>
    public string Token { get; set; } = "";

    /// <summary>消息加解密密钥 EncodingAESKey（T5 事件推送 AES 解密用——43 字符，Base64Decode 派生 32 字节 AESKey）。</summary>
    public string EncodingAESKey { get; set; } = "";
}

/// <summary>
/// 微信公众平台网关库配置选项（<c>TKWF:Federation:WeChat</c> 节——Channels 列表按公众号区分）。
/// <para>[Options("TKWF:Federation:WeChat")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 AuthCenterOptions/FederationOptions 先例——模式 A 双通道；亦可在消费方 ConfigureExtensions 编程覆盖）。</para>
/// </summary>
[Options("TKWF:Federation:WeChat")]
public class WeChatOptions
{
    /// <summary>channel 配置列表（多公众号实例——每项一个 ChannelId/AppId/AppSecret/Token/EncodingAESKey）。</summary>
    public List<WeChatChannelConfig> Channels { get; set; } = [];
}
