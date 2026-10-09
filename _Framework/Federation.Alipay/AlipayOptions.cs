using System.Collections.Generic;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝开放平台 channel 配置（单实例——按 <see cref="ChannelId"/> 区分多应用，N4 T1）。
/// <para>凭证自持（Oracle P2-4）：支付宝无对称 AppSecret（RSA2 双向签名）——商户私钥（<see cref="PrivateKeyPath"/>）
/// 签请求 + 支付宝公钥（<see cref="AlipayPublicKeyPath"/>）验签响应，均为 PEM 文件路径（生产 fail-fast 缺钥拒，
/// F7 验收）。多通道联邦（v0.3.0）下凭证路径经 <see cref="AlipayChannelSource"/> 投影进 <see cref="ChannelConfig"/> 的
/// Extra 字典（非对称/对称分离——RSA 私钥不进公共列 AppSecret，方案 F5 零表结构变更）。</para>
/// <para>⚠️ 私钥/公钥 PEM 文件 chmod 600 不进代码库；支持 KMS/装配注入路径配置（N4 §六风险）。</para>
/// </summary>
public class AlipayChannelConfig
{
    /// <summary>channel 实例 id（<c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 对外名（URL 路径段 /sso/oauth/{alias}/callback / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>支付宝开放平台 AppId（凭证解析键——按 AppId 精确匹配）。</summary>
    public string AppId { get; set; } = "";

    /// <summary>商户私钥 PEM 文件路径（PKCS8——生产 fail-fast 缺钥拒；签请求 RSA2 = SHA256WithRSA）。</summary>
    public string PrivateKeyPath { get; set; } = "";

    /// <summary>支付宝公钥 PEM 文件路径（验签响应/通知——网关同步响应自实现验签，AlipaySignService）。</summary>
    public string AlipayPublicKeyPath { get; set; } = "";

    /// <summary>手机号可选增强开关（默认 false——须企业资质 + 申请 mobile 字段审核，行业类目限制，非稳定能力，N4 §3.4）。</summary>
    public bool EnableMobile { get; set; }
}

/// <summary>
/// 支付宝开放平台平台网关库配置选项（<c>TKWF:Federation:Alipay</c> 节——Channels 列表按应用区分）。
/// <para>[Options("TKWF:Federation:Alipay")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 WeChatOptions 先例；亦可在消费方 ConfigureExtensions 编程覆盖）。</para>
/// </summary>
[Options("TKWF:Federation:Alipay")]
public class AlipayOptions
{
    /// <summary>channel 配置列表（每项一个 ChannelId/AppId/私钥公钥路径/EnableMobile）。</summary>
    public List<AlipayChannelConfig> Channels { get; set; } = [];
}
