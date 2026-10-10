using TKW.Framework.Domain;

namespace TKWF.Ext.Federation;

/// <summary>
/// 连接层通道注册表加密密钥配置（TKWF:Federation:ChannelRegistry 节）。
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：原 <c>FederationOptions</c> 迁 TrustCenter 改
/// <c>TrustCenterOptions</c>（信任内核专用）——Federation 连接层仍自持
/// <c>SsoChannelRegistryEntity</c>（多通道配置持久化，AppSecret/ExtraJson AES-GCM），
/// 故本类独立承载通道注册表加解密密钥路径（keyed <c>ISymmetricKeyProvider</c>
/// <see cref="TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys.Federation"/>——
/// 主框架常量已存在，直接复用；对齐 <see cref="DbChannelRegistry"/> 既有模式）。</para>
/// </summary>
[Options("TKWF:Federation:ChannelRegistry")]
public class FederationChannelRegistryOptions
{
    /// <summary>通道注册表 AppSecret/ExtraJson AES-GCM 加密密钥派生文件路径（对齐 AuthCenter SecretEncryptionKeyPath）。</summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>生产标记（true = fail-fast 门——缺密钥拒绝启动；开发随机兜底）。</summary>
    public bool IsProduction { get; set; }
}
