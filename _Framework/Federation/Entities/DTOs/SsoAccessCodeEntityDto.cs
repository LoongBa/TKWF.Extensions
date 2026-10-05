using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Federation.DTOs;

/// <summary>SSO 授权码实体（联邦 accesscode，通道 B 票据）——120s 单次 + SHA256 存储 + 原子 CAS + PKCE 可选。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>设计文档 §6.1 + Oracle P2-1/P2-2：只存 <c>SHA256(code)</c> 索引不存原文；TTL 120 秒；      <c>used</c> 原子 CAS（<c>UPDATE ... WHERE used=false</c>）防重放；<c>code_verifier_hash</c> PKCE 可选      （defense in depth——复用 IOAuthTicketService 既有 PKCE 资产语义，Oracle P2-1）。</para> 的手写 DTO 扩展</summary>
public partial record SsoAccessCodeEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}