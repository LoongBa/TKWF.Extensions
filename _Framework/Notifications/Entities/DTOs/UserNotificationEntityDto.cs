using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Notifications.DTOs;

/// <summary>用户通知实体——收件箱行（每收件人一条，关联 <see cref="T:TKWF.Ext.Notifications.NotificationEntity"/>）。     <para>State：0=Unread，1=Read。UserId 对齐 Identity/Permissions 的 long 约定（m7）。</para> 的手写 DTO 扩展</summary>
public partial record UserNotificationEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}