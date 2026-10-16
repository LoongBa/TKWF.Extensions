using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter;

/// <summary>OAuth 授权码实体（面② SSO 联邦流——拆分命名 2026-10-16，ADR109 决策 4 修订）。      <para>自原 <see cref="AccessCodeEntity"/> 面②拆分：<c>authorization code → token2</c>      （OAuth authorization code 语义，字段级消费）——独立 <c>IAuthorizationCodeService</c>。      只存 <c>SHA256(code)</c> 索引不存原文；TTL；<c>used</c> 原子 CAS 防重放；      <c>code_verifier_hash</c> PKCE 可选（defense in depth）。</para>      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para></summary>
public partial class AuthorizationCodeEntity
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