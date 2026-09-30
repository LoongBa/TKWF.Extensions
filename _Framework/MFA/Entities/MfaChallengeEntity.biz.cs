using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.MFA;

/// <summary>MFA 挑战票据实体——挑战-验证流状态承载（一次性 + TTL + 单次消费）。  <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaChallenge")]</c>；  列映射用 FreeSql <c>[Column]</c>（全限定）。</para>  <para>挑战票据模型（ADR-MFA-挑战票据与验证模型 定案）：SMS 场景 <c>CodeHash</c> 落 SHA256 码（明文不落库）；  TOTP 场景 <c>CodeHash</c> null（无状态验证——票据仅作流程句柄 + 频控挂点 + 审计）。</para>  <para>单次消费：<c>IsConsumed</c> 原子翻转（成功验证后同票据/同码拒绝——防重放）；TTL <c>ExpireAt</c>（5min 对齐 OAuthTicket）。  尝试频控归内存滑动窗口（per-user×method，Oracle C4——本实体不设 Attempts 列）。</para>  <para>索引：<c>IX_MfaChallenge_User_Method</c>（UserId+Method——按用户方法查活动挑战）+ <c>IX_MfaChallenge_Expire</c>（过期清理）。</para></summary>
public partial class MfaChallengeEntity
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