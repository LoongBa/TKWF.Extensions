using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>用户通知实体——收件箱行（每收件人一条，关联 <see cref="T:TKWF.Ext.Notifications.NotificationEntity"/>）。     <para>State：0=Unread，1=Read。UserId 对齐 Identity/Permissions 的 long 约定（m7）。</para></summary>
public partial class UserNotificationEntity
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