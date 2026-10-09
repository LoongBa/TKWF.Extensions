using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉开放平台 channel 配置（单应用实例——按 <see cref="ChannelId"/> 区分多应用）。
/// <para>凭证自持（Oracle P2-4）：channel 凭证归平台适配库自持——开发明文 appsettings.json 配置；
/// 生产 AES-GCM 密文或装配注入（K8s secret mount），AppSecret/EncodingAESKey 永不明文进配置库。</para>
/// <para><see cref="CorpId"/>（Oracle 评审 P1-1）：事件订阅回调解密明文尾部 <c>receiveid</c> 校验第二道闸门
/// ——forged org-change event 可授予企业访问权，敏感度高于微信消息事件；同时是 getbyunionid 企业上下文。</para>
/// <para>企业级 token（Oracle 评审 P1-5）：钉钉新模式复用 OAuth 凭证（AppKey/AppSecret）换企业 token，
/// 无独立 enterprise secret——<see cref="AppKey"/>/<see cref="AppSecret"/> 即新旧双协议企业 token 凭证。</para>
/// </summary>
public class DingTalkChannelConfig
{
    /// <summary>channel 实例 id（钉钉应用 id——<c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 对外名（URL 路径段 /sso/oauth/{alias}/callback / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>企业 CorpId（事件回调 <c>receiveid</c> 校验——P1-1 第二道闸门；getbyunionid 企业上下文）。</summary>
    public string CorpId { get; set; } = "";

    /// <summary>应用 AppKey（凭证解析键——DingTalkApiClient 按 AppKey 精确匹配；新旧协议企业 token 凭证）。</summary>
    public string AppKey { get; set; } = "";

    /// <summary>应用 AppSecret（生产 AES-GCM 密文或装配注入，永不明文进配置库）。</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>事件订阅令牌（回调验签 <c>sha1(sort(token,timestamp,nonce,msg_encrypt))</c> 用——T5 入站信任根）。</summary>
    public string Token { get; set; } = "";

    /// <summary>事件加解密密钥 EncodingAESKey（T5 事件推送 AES 解密用——43 字符，Base64Decode 派生 32 字节 AESKey）。</summary>
    public string EncodingAESKey { get; set; } = "";
}

/// <summary>
/// 钉钉开放平台网关库配置选项（<c>TKWF:Federation:DingTalk</c> 节——Channels 列表按应用区分）。
/// <para>[Options("TKWF:Federation:DingTalk")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 WeChatOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。</para>
/// </summary>
[Options("TKWF:Federation:DingTalk")]
public class DingTalkOptions
{
    /// <summary>channel 配置列表（多应用实例——每项一个 ChannelId/CorpId/AppKey/AppSecret/Token/EncodingAESKey）。</summary>
    public List<DingTalkChannelConfig> Channels { get; set; } = [];
}
