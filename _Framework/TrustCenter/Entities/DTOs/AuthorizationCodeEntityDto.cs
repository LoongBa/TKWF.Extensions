using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.TrustCenter.DTOs;

/// <summary>OAuth 授权码实体（面② SSO 联邦流——拆分命名 2026-10-16，ADR109 决策 4 修订）。      <para>自原 <c>AccessCodeEntity</c> 面②拆分：<c>authorization code → token2</c>      （OAuth authorization code 语义，字段级消费）——独立 <c>IAuthorizationCodeService</c>。      只存 <c>SHA256(code)</c> 索引不存原文；TTL；<c>used</c> 原子 CAS 防重放；      <c>code_verifier_hash</c> PKCE 可选（defense in depth）。</para>      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para> 的手写 DTO 扩展</summary>
public partial record AuthorizationCodeEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}