using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter;

/// <summary>账号冻结实体——多原因冻结行（V0.11.0 起 AuthAccount.IsFrozen/FreezeEnd 两列的因子化演进底座）。      <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para><b>活跃判据：<c>ReleasedAt == null &amp;&amp; (FreezeEnd == null || FreezeEnd &gt; DateTime.UtcNow)</c></b>——      解冻置 ReleasedAt 非空（软释放保留审计行），到期 FreezeEnd 到时不自动清行（免写路径，判据天然失效）。</para>      <para>Scope：0=Account（全站登录）/ 1=Login / 2=Feature；<b>V0.11.0 恒为 0</b>，其余为预留位。</para>      表名 <c>TKWF_AuthAccountFreeze</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。</summary>
public partial class AuthAccountFreezeEntity
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