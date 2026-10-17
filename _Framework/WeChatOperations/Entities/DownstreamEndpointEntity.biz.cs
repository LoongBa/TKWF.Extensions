using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>下游转发端点实体（表 <c>TKWF_WeChatDownstreamEndpoint</c>）——网关消息转发下游端点（HTTP 出站目标，V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="BaseUrl"/> + <see cref="Path"/>（可空）拼接出站目标；<see cref="HttpMethod"/> 默认 "POST"（微信被动回复/转发语义）。</para>      <para><see cref="AuthType"/>（int）：0=None 无鉴权 / 1=Bearer 头携带 / 2=Header 自定义头携带。      <see cref="AuthTokenCipher"/>：AES-GCM 单段密文（base64(nonce‖cipher‖tag)，对齐 <see cref="WeChatChannelConfigEntity"/> 凭证密文列）——      <b>明文永不落库</b>；解密仅出站瞬时。</para>      <para>唯一索引：<see cref="ChannelId"/> + <see cref="Name"/>（channel 内端点名唯一——按名选区/去重）。</para></summary>
public partial class DownstreamEndpointEntity
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