using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter;

/// <summary>SSO 授权码实体（联邦 accesscode，通道 B 票据）——120s 单次 + SHA256 存储 + 原子 CAS + PKCE 可选。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>设计文档 §6.1 + Oracle P2-1/P2-2：只存 <c>SHA256(code)</c> 索引不存原文；TTL 120 秒；      <c>used</c> 原子 CAS（<c>UPDATE ... WHERE used=false</c>）防重放；<c>code_verifier_hash</c> PKCE 可选      （defense in depth——复用 IOAuthTicketService 既有 PKCE 资产语义，Oracle P2-1）。</para></summary>
public partial class AccessCodeEntity
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