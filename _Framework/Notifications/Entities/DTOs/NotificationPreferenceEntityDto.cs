using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Notifications.DTOs;

/// <summary>V0.3.0：通知偏好实体——用户对特定通知的通道偏好（覆盖定义级 UseChannels）。     <para>ChannelsJson 存通道列表 JSON（如 <c>["Email"]</c>；null = 回退定义级）。     <c>UX_NotificationPreference_User_Notification</c>（UserId+NotificationName）唯一——每用户每通知至多一条偏好。</para> 的手写 DTO 扩展</summary>
public partial record NotificationPreferenceEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}