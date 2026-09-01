using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典定义——字典聚合根（编码/名称/描述/启用）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("DictionaryDefinition")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("DictionaryDefinition")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class DictionaryDefinitionEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>字典编码（唯一，如 "Gender"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Code { get; set; } = "";

        /// <summary>显示名（如 "性别"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string DisplayName { get; set; } = "";

        /// <summary>描述。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>是否启用（默认 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}