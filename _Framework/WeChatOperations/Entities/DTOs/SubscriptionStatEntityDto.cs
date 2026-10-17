using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>订阅统计实体（表 <c>TKWF_WeChatSubscriptionStat</c>）——按 channel 按日统计订阅/取关（subscribe/unsubscribe 事件驱动，每日 upsert）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>唯一约束 (ChannelId, StatDate)：每日每 channel 一行——<c>ISubscriptionStatService.RecordAsync</c> 按此键每日 upsert 原子累加。</para> 的手写 DTO 扩展</summary>
public partial record SubscriptionStatEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}