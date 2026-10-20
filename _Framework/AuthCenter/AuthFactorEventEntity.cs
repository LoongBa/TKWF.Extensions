using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 认证因子事件实体——append-only 审计事实表（<b>只增不删改</b>：写入即永久，无 Update/Delete 业务语义）。
    /// <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>EventType：1=Bind / 2=Unbind / 3=Verify / 4=Reset；Source：1=Manual / 2=Business / 3=Policy / 4=Provisioning。</para>
    /// <para>Identifier 保留标识快照——因子解绑（物理删除）后仍可追溯该标识的历史事件。</para>
    /// 表名 <c>TKWF_AuthFactorEvent</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。
    /// </summary>
    [Table("TKWF_AuthFactorEvent")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthFactorEvent_UId", nameof(UId) + "," + nameof(OccurredAt))]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthFactorEvent_Dedupe", nameof(DedupeKey), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthFactorEventEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（u-{unixMs}-{rand}）；与 OccurredAt 构成查询索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>因子类型：1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(预留) / 6=Passkey(预留)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int FactorType { get; set; }

        /// <summary>标识快照（规范化形态）——解绑后仍可追溯；保留事件发生时的值，不随因子行删除而消失。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(256)]
        public string Identifier { get; set; } = "";

        /// <summary>事件类型：1=Bind / 2=Unbind / 3=Verify / 4=Reset。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int EventType { get; set; }

        /// <summary>来源：1=Manual / 2=Business / 3=Policy / 4=Provisioning。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int Source { get; set; }

        /// <summary>事件发生时刻（UTC）——业务时间（可能与 ReceivedAt 分离，如补录/异步投递）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime OccurredAt { get; set; }

        /// <summary>接收落库时刻（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        /// <summary>操作者平台 id（null = 系统/匿名操作）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(32)]
        public string? OperatorUId { get; set; }

        /// <summary>操作者显示名（快照，不随档案变更漂移）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(64)]
        public string? OperatorName { get; set; }

        /// <summary>幂等去重键。<b>可空</b>——UNIQUE 索引仅对非空值生效（NULL 多行放行——SQLite/PG 语义）；
        /// 由调用方提供幂等键（如业务事件 id），未提供则不入去重语义（重复事件按事实各自落行）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(256)]
        public string? DedupeKey { get; set; }
    }
}
