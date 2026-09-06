using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// 数据导入批次记录实体——记录每次导入的元数据（批次号/文件哈希/状态/统计/错误摘要）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("DataImportRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// <para>FileHash 唯一索引防重复导入（幂等检查入口）；BatchNo 供消费方按批次回滚。</para>
    /// </summary>
    [Table("DataImportRecord")]
    [FreeSql.DataAnnotations.Index("IX_dir_filehash", nameof(FileHash), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("IX_dir_batchno", nameof(BatchNo), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class DataImportRecordEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>批次号（GUID，消费方回滚入口）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string BatchNo { get; set; } = "";

        /// <summary>文件哈希（SHA256，幂等检查——同文件二次导入拒绝）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string FileHash { get; set; } = "";

        /// <summary>原始文件名。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string FileName { get; set; } = "";

        /// <summary>Provider 名称（如 "miniexcel"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(128)]
        public string ProviderName { get; set; } = "";

        /// <summary>导入状态（Processing/Succeeded/Failed/PartiallySucceeded）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(64)]
        public string Status { get; set; } = "Processing";

        /// <summary>验证通过数（独立于批次持久化）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int SuccessCount { get; set; }

        /// <summary>行级失败数。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int FailedCount { get; set; }

        /// <summary>批次级持久化失败数。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public int BatchFailureCount { get; set; }

        /// <summary>开始时间（UTC）——由任务服务显式赋值（SQLite Provider 类型映射缺 DateTimeOffset，时间列统一用 DateTime）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
        public DateTime StartTime { get; set; }

        /// <summary>结束时间（UTC）——由任务服务显式赋值（SQLite Provider 类型映射缺 DateTimeOffset，时间列统一用 DateTime）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
        public DateTime? EndTime { get; set; }

        /// <summary>错误摘要（前 N 个失败消息拼接，便于快速排查）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(2048)]
        public string? ErrorSummary { get; set; }

        /// <summary>创建人（CreatedUser.UserGuid，Oracle m6：CreatedBy 审计字段）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        /// <summary>创建时间（UTC）——由任务服务显式赋值（SQLite Provider 类型映射缺 DateTimeOffset，时间列统一用 DateTime）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>更新时间（UTC）——由任务服务显式赋值（SQLite Provider 类型映射缺 DateTimeOffset，时间列统一用 DateTime）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 15)]
        public DateTime UpdateTime { get; set; }
    }
}