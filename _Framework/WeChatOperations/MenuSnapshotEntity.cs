using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 菜单基准实体（表 <c>TKWF_WeChatMenuSnapshot</c>）——本地权威基线（菜单结构的本地基准，与 MenuVersion 同一事务同步）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>每 channel 一行（唯一索引 ChannelId）——<see cref="MenuJson"/> 为微信菜单完整 JSON（大文本）；
    /// 发布成功 / 拉取确认后更新（方案 §3.3.1 Oracle P2：MenuSnapshot 与 MenuVersion 一致性——发布成功后同步更新两者，同一事务保证一致）。</para>
    /// </summary>
    [Table("TKWF_WeChatMenuSnapshot")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMenuSnapshot_ChannelId", nameof(ChannelId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MenuSnapshotEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id（唯一索引——每 channel 一行本地基准）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>菜单完整 JSON（微信按钮结构，text 大文本——本地权威基线）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public string MenuJson { get; set; } = "";

        /// <summary>更新时间（UTC——基线刷新时刻）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC——审计惯例）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}