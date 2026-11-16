using System;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换码批次实体（表 <c>TKWF_CodeBatch</c>，v0.3.0 批次管理端）——兑换码域固有聚合根「头表」。
/// <para>「头+行」均在扩展内（对齐 DataPort 任务/记录、FileManagement 主/版本先例）：批次计划数量/状态/统计基座承载于头，
/// 码行 <see cref="RedemptionCodeEntity.BatchId"/> 软引用本表 <c>Id</c>（<c>long?</c> 直接存 Id，对齐
/// <c>ManagedFileVersionEntity.FileId</c> 先例——非唯一索引，代码级生命周期保证：批次仅 DRAFT 可删，DRAFT 无码）。</para>
/// <para>状态机：DRAFT(0) → GENERATING(1，CAS 抢占) → GENERATED(2)；GENERATED → REVOKED(3，整批吊销动作)；
/// DRAFT →（物理删除）；失败回滚 → DRAFT。PARTIAL_REVOKED 不落库（派生：Status==2 且 revokedCount&gt;0）。</para>
/// </summary>
[Table("TKWF_CodeBatch")]
[FreeSql.DataAnnotations.Index("TKWFIX_CodeBatch_BatchNo", nameof(BatchNo), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 20)]
public partial class CodeBatchEntity
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>展示批次号（格式 <c>B{yyyyMMdd}{4 位随机大写字母数字}</c>——CSPRNG；唯一约束撞（23505）重试再生成，有限 3 次）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2, StringLength = 32)]
    public string BatchNo { get; set; } = "";

    /// <summary>批次/券名称（≤100）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3, StringLength = 100)]
    public string Name { get; set; } = "";

    /// <summary>产品名（≤100——码行冗余，生成时投影）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, StringLength = 100)]
    public string ProductName { get; set; } = "";

    /// <summary>目标应用（≤100，软引用 AuthApp.AppId；"*" 全产品语义透传）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, StringLength = 100)]
    public string TargetAppId { get; set; } = "";

    /// <summary>兑换后授权时长模板（自然日，null=无 grant 语义）——扩展不解释，消费方编排读。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
    public int? ValidityDays { get; set; }

    /// <summary>批次券有效期（绝对 UTC，未兑换前作废点；null=不过期）——码行生成时继承。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    public DateTime? ExpireAtUtc { get; set; }

    /// <summary>发行渠道（≤100 软词汇——值域归消费方，仅长度校验）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true, StringLength = 100)]
    public string? Channel { get; set; }

    /// <summary>计划数量（1~Options.MaxBatchQuantity 默认 5000）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    public int Quantity { get; set; }

    /// <summary>实际生成数（DRAFT 为 0——统计 total 口径 = GeneratedCount）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public int GeneratedCount { get; set; }

    /// <summary>状态——<remarks>0=DRAFT / 1=GENERATING / 2=GENERATED / 3=REVOKED</remarks>（int 保持，不引枚举类型避免破坏）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11)]
    public int Status { get; set; }

    /// <summary>测试批次标记（拒兑策略归消费方兑换编排）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public bool Test { get; set; }

    /// <summary>生成即导出时间（= GENERATED 落定时刻）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13, IsNullable = true)]
    public DateTime? ExportedAtUtc { get; set; }

    /// <summary>创建人（≤64，透传不做身份校验）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 14, IsNullable = true, StringLength = 64)]
    public string? CreatedBy { get; set; }

    [FreeSql.DataAnnotations.Column(Position = 15, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    [FreeSql.DataAnnotations.Column(Position = 16)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}