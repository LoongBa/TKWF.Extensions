using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 素材实体（表 <c>TKWF_WeChatMaterial</c>）——素材库（全局共享——无 ChannelId，规则按 <see cref="MaterialEntity.Id"/> 引用，一个素材多规则复用）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>Kind：1=Text（纯文本，<see cref="Content"/>）/ 2=News（图文，Title/Description/Url/PicUrl）。</para>
    /// </summary>
    [Table("TKWF_WeChatMaterial")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MaterialEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>素材名称（运营侧标识）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        /// <summary>回复内容（文本——Kind=Text 时承载纯文本回复；News 时承载文章内容 JSON）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public string? Content { get; set; }

        /// <summary>标题（图文——Kind=News）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(200)]
        public string? Title { get; set; }

        /// <summary>描述（图文——Kind=News）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(1000)]
        public string? Description { get; set; }

        /// <summary>链接（图文——Kind=News）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(500)]
        public string? Url { get; set; }

        /// <summary>封面图 URL（图文——Kind=News）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(500)]
        public string? PicUrl { get; set; }

        /// <summary>素材类型（int——1=Text / 2=News）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int Kind { get; set; }

        /// <summary>启用（默认 true；禁用 → 规则引用该素材时跳过）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}