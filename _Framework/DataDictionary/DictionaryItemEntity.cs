using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典项——归属某字典定义的具体选项（编码/显示名/值/排序/启用）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("DictionaryItem")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("DictionaryItem")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class DictionaryItemEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属字典定义 ID（索引）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long DefinitionId { get; set; }

        /// <summary>字典项编码（如 "Male"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string Code { get; set; } = "";

        /// <summary>显示名（如 "男"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string DisplayName { get; set; } = "";

        /// <summary>关联值（可为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(256)]
        public string? Value { get; set; }

        /// <summary>排序（小值在前）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int Order { get; set; }

        /// <summary>是否启用（默认 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>父项编码（业务编码引用，V0.2.0 树形分组）。根节点为 null。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(128)]
        public string? ParentCode { get; set; }

        /// <summary>层级深度（V0.2.0 树形分组）。根节点为 0。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public int Level { get; set; }

        /// <summary>物化路径（V0.2.0 树形分组，形如 "/root/child/grandchild"）。根节点为 "/{Code}"。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(1024)]
        public string Path { get; set; } = "";
    }
}