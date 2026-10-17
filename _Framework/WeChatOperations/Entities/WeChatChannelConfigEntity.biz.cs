using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>公众号渠道配置实体（表 <c>TKWF_WeChatChannelConfig</c>）——基础配置（DB 权威，方案 §3.3.4 两源并存：DB 优先 → Options 降级）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>🔒 凭证安全（Oracle P1 安全五补充）：<see cref="AppSecretCipher"/>/<see cref="TokenCipher"/>/<see cref="EncodingAESKeyCipher"/>      三字段为 AES-GCM 单段密文（base64(nonce‖cipher‖tag)，对齐 AuthSurface V0.2.0 规范）——<b>明文永不落库</b>；      DTO 投影不含凭证（密文不回显）；解密仅出站瞬时。</para>      <para>唯一索引：ChannelId（公众号实例 id）/AppId（公众号 AppId——出站凭证解析键）。</para></summary>
public partial class WeChatChannelConfigEntity
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