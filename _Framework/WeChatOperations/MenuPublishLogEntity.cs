using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 菜单操作审计实体（表 <c>TKWF_WeChatMenuPublishLog</c>）——操作审计（who/when/action，<b>不含 JSON 内容</b>——JSON 归 MenuVersion）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>职责分离（Oracle P1）：发布动作 MenuVersion（记内容）+ MenuPublishLog（记审计）同时记录；
    /// 非发布操作（读取/删除/测试）只记本表。</para>
    /// <para>Action（int）：1=Read（拉取当前菜单）/ 2=Create（发布）/ 3=Delete（删除）/ 4=Test（试发测试）。</para>
    /// </summary>
    [Table("TKWF_WeChatMenuPublishLog")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMenuPublishLog_Channel", nameof(ChannelId))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MenuPublishLogEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>操作人（执行者标识）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string Operator { get; set; } = "";

        /// <summary>操作时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime OperateAt { get; set; } = DateTime.UtcNow;

        /// <summary>操作类型（int——1=Read / 2=Create / 3=Delete / 4=Test）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int Action { get; set; }

        /// <summary>备注（操作上下文，如回滚源版本号等）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(500)]
        public string? Remark { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}