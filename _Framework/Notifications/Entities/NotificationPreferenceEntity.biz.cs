using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>V0.3.0：通知偏好实体——用户对特定通知的通道偏好（覆盖定义级 UseChannels）。     <para>ChannelsJson 存通道列表 JSON（如 <c>["Email"]</c>；null = 回退定义级）。     <c>UX_NotificationPreference_User_Notification</c>（UserId+NotificationName）唯一——每用户每通知至多一条偏好。</para></summary>
public partial class NotificationPreferenceEntity
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