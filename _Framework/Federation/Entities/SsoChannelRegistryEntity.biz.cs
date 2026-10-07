using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation;

/// <summary>通道注册表实体（多通道联邦 Phase 2——DB 动态权威层，方案 docs/Federation/多通道联邦-开发方案.md §3.3）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>凭证模型（方案 §3.1 前瞻核心）：公共列（<see cref="ChannelId"/>/<see cref="PlatformType"/>/<see cref="AppId"/>+      <see cref="AppSecretEncrypted"/>）+ 扩展 JSON 密文列（<see cref="ExtraJsonEncrypted"/>——平台特定负载 AES-GCM      整段加密：WeChat{Token,EncodingAESKey} / OIDC{TokenIssuers,DiscoveryUri} / 支付宝（未来）{AppPrivateKey}——      未来平台零表结构变更）。公共列 <see cref="AppId"/> 仅承载 OAuth 握手应用标识（Oracle M7 映射规则约束），      法人级标识（CorpId/OrgId/AgentId 类）一律进 ExtraJson。</para>      <para>加密语义（方案 M6/M12）：<see cref="AppSecretEncrypted"/> 与 <see cref="ExtraJsonEncrypted"/> 均      AES-GCM 密文列（keyed <c>ISymmetricKeyProvider "Federation"</c>），服务层加解密（<c>SsoChannelRegistryService</c>）——      生产用 DB 层全程密文，明文不落库（静态 Options 明文仅限开发态，方案 M12）。</para>      <para>容量核验（Oracle M6）：<see cref="ExtraJsonEncrypted"/>(4000) 起步——RSA-2048 PEM 私钥约 1.7KB +      OIDC DiscoveryUri+TokenIssuers 组合 1.5KB+ 加密 base64 膨胀 ~33%（P1 定案锁定，避免 P2 迁移）；      <see cref="AppSecretEncrypted"/>(512) 承载对称密钥/AES-GCM 密文足量。</para></summary>
public partial class SsoChannelRegistryEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查) 
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}