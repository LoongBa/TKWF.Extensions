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

    /// <summary>
    /// 通道配置内存缓存 TTL 秒数（D03 §4.2.3 三机制之一「TTL 兜底」——默认 120，建议区间 60–300）。
    /// <para>缓存内容 = 解密后明文 <see cref="ChannelConfig"/>（进程内信任边界，不落盘不进日志）；
    /// 键 = <c>ch:{ChannelId}</c> + <c>ch:all</c> + <c>ch:default</c>（方案 §8.2）。
    /// 显式失效（<c>InvalidateAsync</c>）/ 事件失效（<c>ChannelConfigChanged</c>）/ TTL 过期三机制并存，
    /// TTL 为兜底——静态源零装配场景配置进程生命周期恒定，TTL 仅兜底 DB 源变更未发布事件的情形。</param>
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 120;
}
