using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台} channel 配置（单实例——按 <see cref="ChannelId"/> 区分多实例）。
/// <para>凭证自持（Oracle P2-4）：channel 凭证归平台适配库自持，**不复用** Authentication
/// <c>IPlatformCredentialService</c>——开发明文 appsettings.json 配置；生产 AES-GCM 密文或装配注入
/// （K8s secret mount），AppSecret 永不明文进配置库。</para>
/// <para>⚠️ 模板定案（N1 F5 权威事实源）：与 {Platform}Options 同文件——WeChat/WeCom/Google 先例
/// 均无独立 ChannelConfig.cs（对齐 WeChatOptions.cs 先例，类而非 record）。</para>
/// </summary>
public class {Platform}ChannelConfig
{
    /// <summary>channel 实例 id（<c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>平台 AppId（凭证解析键——{Platform}ApiClient 按 AppId 精确匹配）。</summary>
    public string AppId { get; set; } = "";

    /// <summary>平台 AppSecret（生产 AES-GCM 密文或装配注入，永不明文进配置库）。</summary>
    public string AppSecret { get; set; } = "";

    // 有入站回调平台追加（对齐 WeChatChannelConfig 先例）：
    //   /// <summary>平台令牌（事件推送验签用——入站信任根）。</summary>
    //   public string Token { get; set; } = "";
    //   /// <summary>消息加解密密钥 EncodingAESKey（事件推送 AES 解密用——43 字符，Base64Decode 派生 32 字节 AESKey）。</summary>
    //   public string EncodingAESKey { get; set; } = "";
    // 平台差异点：CorpId/AgentId（WeCom）、EnableUnionId 开关（QQ）等
}

/// <summary>
/// {平台}平台网关库配置选项（<c>TKWF:Federation:{Namespace}</c> 节——Channels 列表按实例区分）。
/// <para>[Options("TKWF:Federation:{Namespace}")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 AuthCenterOptions/FederationOptions/WeChatOptions 先例；亦可在消费方 ConfigureExtensions 编程覆盖）。</para>
/// </summary>
[Options("TKWF:Federation:{Namespace}")]
public class {Platform}Options
{
    /// <summary>channel 配置列表（每项一个 ChannelId/AppId/AppSecret）。</summary>
    public List<{Platform}ChannelConfig> Channels { get; set; } = [];
}
