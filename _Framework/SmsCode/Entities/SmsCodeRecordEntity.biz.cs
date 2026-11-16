using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.SmsCode;

/// <summary>短信验证码记录实体——发送/校验频控与作废的数据源。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，      扩展不感知消费方用户类型；对齐 AuthCenter <c>SmsRecordEntity</c> 先例）。</para>      <para><b>X' 冻结项 1</b>：主体键列名 <c>Subject</c>（不叫 Phone）——不透明主体键，v1 恒=手机号；      <c>DeliveryTarget</c> 与 Subject 分离（MFA 未来 subject=user×method 而投递=绑定号）。</para>      <para>验证码单向散列（SHA256 hex 小写，<c>FixedTimeEquals</c> 比对）落库，不存明文；      校验成功 <c>IsVerified=true</c> 单次消费；<c>FailedAttempts</c> 为同码连续失败计数（M3——失败作废机制落点）。</para>      <para>只增语义（对齐 AuthCenter SmsRecord 先例）：写路径 = 创建（发送）/ MarkVerified（校验消费）/      <c>FailedAttempts</c> CAS 自增（失败）/ 重发作废旧码（ExpireAt 置 now）；无公开 Update/Delete 业务方法。</para></summary>
public partial class SmsCodeRecordEntity
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