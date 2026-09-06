using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>通知订阅实体——用户按通知名订阅（定义级或实体级）。     <para>EntityTypeName/EntityId 均为 null = 定义级订阅（关注该通知全部发布）；     非 null = 实体级订阅（只关注某个具体实体实例）。</para></summary>
public partial class NotificationSubscriptionEntity
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