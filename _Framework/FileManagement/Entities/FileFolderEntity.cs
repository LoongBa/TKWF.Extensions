using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件目录实体——树形目录（Code 唯一 + Level/Path 物化路径，对齐 OrganizationUnit 模式，F1）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("FileFolder")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// <para>删除语义：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类 <c>hasSoftDelete:false</c>；
    /// 删除保护（无子目录且无文件）由 Manager 层强制（D17）。</para>
    /// <para>Path 物化路径（C3 裁定）：由 Code 构建（根 = "/Code/"；子 = 父 Path + Code + "/"），
    /// <b>Code 创建后不可改</b>——重命名只改 Name，子树 Path 不重算（O(1)）。</para>
    /// <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明（对齐 Calendar/DataDictionary 先例，
    /// FreeSql SQLite 不支持 DateTimeOffset）；不声明 IsDeleted（物理删除）。</para>
    /// </summary>
    [Table("FileFolder")]
    [FreeSql.DataAnnotations.Index("UX_FileFolder_Code", nameof(Code), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class FileFolderEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>目录编码（库级唯一；构建 Path 段，创建后不可改——C3）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Code { get; set; } = "";

        /// <summary>目录名称（可含任意字符，不进 Path；重命名只改本字段）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string Name { get; set; } = "";

        /// <summary>父目录 Id（自引用；null = 根目录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
        public long? ParentId { get; set; }

        /// <summary>层级（根 = 0；子 = 父 + 1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int Level { get; set; }

        /// <summary>物化路径（"/Code/"；子 = 父 Path + Code + "/"；≤1024 守卫，对齐 OU C3）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(1024)]
        public string Path { get; set; } = "";

        /// <summary>同级排序（小值在前；默认同级末尾 max+1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int SortOrder { get; set; }

        /// <summary>创建时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
