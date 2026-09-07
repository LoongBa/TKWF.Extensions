using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Notifications.DTOs;

/// <summary>用户通知视图实体（VEntity）——跨表 JOIN <c>UserNotification</c> → <c>Notification</c>，按 UserId + 通知名单查询分页。     <para>V0.2.0：替代两步查询（先按 name 取 Notification.Id 集合，再按集合过滤收件箱）——JOIN 下推 DB，     单查询完成跨表过滤 + 分页 + 排序，顺带返回通知名/严重级别/显示名（GraphQL 路径可用）。     VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para> 的手写 DTO 扩展</summary>
public partial record UserNotificationViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}