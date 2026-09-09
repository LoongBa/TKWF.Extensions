using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 抄送记录实体——审批发起/完成时通知相关人（不参与审批）。
/// <para>StartAsync 带 ccUserIds / AddCCAsync 记录；实例提交/终态时发布事件，投递组装消费方 Notifications。
/// 索引 IX_ac_instance（InstanceId，非唯一）。</para>
/// </summary>
[Table("ApprovalCC")]
[FreeSql.DataAnnotations.Index("IX_ac_instance", nameof(InstanceId), IsUnique = false)]
[DomainGenerateCode(DefaultPageSize = 50, SubDomain = "Approval", SubDomainRoutePrefix = "/Approval")]
public partial class ApprovalCCEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>关联审批实例 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long InstanceId { get; set; }

    /// <summary>抄送人用户 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string UserId { get; set; } = "";

    /// <summary>抄送位置（Start/Finish/StartFinish）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public ApprovalCCPosition Position { get; set; } = ApprovalCCPosition.Finish;

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
