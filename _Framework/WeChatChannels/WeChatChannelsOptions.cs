using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 微信公众号通道底座配置（<c>TKWF:WeChatChannels</c> 节——2026-11-05 拆分下沉：密钥字段自
/// <c>WeChatOperationsOptions</c>（<c>TKWF:WeChatOperations</c> 节）迁出独立成节——消费方 appsettings 迁移）。
/// <para>配置分层（AGENTS §8）：领域决策（凭证密钥路径/生产模式）归本节；表现层暴露面（REST 路由）由
/// <c>[ApiExpose(RestRoute)]</c> 常量 + <c>[assembly: ExtensionModule("WeChatChannels","wechat-channels")]</c>
/// 前缀承载；领域行为无 Web 层镜像开关（防表现层绕过）。</para>
/// <para><see cref="SecretEncryptionKeyPath"/>/<see cref="IsProduction"/>：凭证 AES-GCM 密钥文件路径/生产模式
/// 标记（keyed <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> 键
/// <see cref="WeChatChannelsKeyProviderKeys.WeChatChannels"/>——生产缺密钥 fail-fast / 开发随机兜底，对齐 AuthCenter E4）。
/// ⚠️ 运营扩展网关（MessageGatewayService/WeChatMessageForwardJob）经<b>同一键</b>消费本配置的 provider——
/// 跨扩展同一信任域（同密钥文件），依赖方向：运营扩展依赖本扩展已注册 keyed provider（O4）。</para>
/// </summary>
[Options("TKWF:WeChatChannels")]
public class WeChatChannelsOptions
{
    /// <summary>凭证 AES-GCM 密钥文件路径（keyed ISymmetricKeyProvider——生产缺密钥 fail-fast / 开发随机兜底）。</summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>生产模式（true=缺密钥拒绝启动，禁 dev 随机兜底；开发置 false）。</summary>
    public bool IsProduction { get; set; } = true;
}