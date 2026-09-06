using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Notifications.DTOs;

/// <summary>通知订阅实体——用户按通知名订阅（定义级或实体级）。     <para>EntityTypeName/EntityId 均为 null = 定义级订阅（关注该通知全部发布）；     非 null = 实体级订阅（只关注某个具体实体实例）。</para> 的手写 DTO 扩展</summary>
public partial record NotificationSubscriptionEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}