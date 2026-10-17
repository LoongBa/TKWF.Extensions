using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 菜单版本实体（表 <c>TKWF_WeChatMenuVersion</c>）——菜单发布历史（内容版本——before/after JSON + 发布人/结果，回滚数据源）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>职责分离（Oracle P1）：<b>内容版本</b>（before/after 完整 JSON + 发布审计人/结果）归本表；
    /// 操作审计（who/when/action，不含 JSON）归 <c>MenuPublishLogEntity</c>——发布动作两者同时记录，非发布操作只记 PublishLog。</para>
    /// <para>Result：1=Success / 2=Fail——回滚时加载旧版本 AfterJson 重新发布。</para>
    /// </summary>
    [Table("TKWF_WeChatMenuVersion")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMenuVersion_Channel", nameof(ChannelId))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MenuVersionEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>版本号（每 channel 递增 1，从 1 起）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int VersionNo { get; set; }

        /// <summary>发布前 JSON（备份——首次发布 null/空；text 大文本）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public string? BeforeJson { get; set; }

        /// <summary>发布后 JSON（本版本最终结构；text 大文本——回滚数据源）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public string AfterJson { get; set; } = "";

        /// <summary>操作人（发布执行者标识——合并发布审计）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(100)]
        public string Operator { get; set; } = "";

        /// <summary>发布时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

        /// <summary>发布结果（int——1=Success / 2=Fail）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int Result { get; set; }

        /// <summary>失败错误码（Result=Fail 时非空；WeChat API err.code）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(100)]
        public string? ErrorCode { get; set; }

        /// <summary>失败错误消息（Result=Fail 时非空；不含凭证明文）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(500)]
        public string? ErrorMsg { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}