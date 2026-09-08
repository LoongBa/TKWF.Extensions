using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元实体——树形部门/团队/分组（物化路径 Level/Path，写入维护）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("OrganizationUnit")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// <para>删除语义（C2 裁定）：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类
    /// <c>hasSoftDelete:false</c>；删除保护（无子节点 + 无关联用户）从源头防误删；已删 Code 可复用。</para>
    /// <para>Code 白名单（C3）：<c>[A-Za-z0-9_.-]</c> 正则校验（Manager 层强制），拒绝 LIKE 通配符与空白——
    /// 保证 <c>Path LIKE '{ou.Path}%'</c> 前缀查询无字符歧义。</para>
    /// </summary>
    [Table("OrganizationUnit")]
    [FreeSql.DataAnnotations.Index("UX_OrganizationUnit_Code", nameof(Code), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class OrganizationUnitEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>组织单元编码（唯一，白名单 [A-Za-z0-9_.-]，创建后不可修改）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Code { get; set; } = "";

        /// <summary>父组织单元 Id（自引用；null = 根）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public long? ParentId { get; set; }

        /// <summary>显示名（可含任意字符，不进 Path）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string Name { get; set; } = "";

        /// <summary>同级排序（小值在前；创建/移动追加到末尾 max+1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int SortOrder { get; set; }

        /// <summary>层级深度（根=0；物化路径冗余，写入维护）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int Level { get; set; }

        /// <summary>物化路径（如 "/A/B/C/"；写入维护，最长 1024）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(1024)]
        public string Path { get; set; } = "";

        /// <summary>是否启用（默认 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（ServerTime Local——DB 填充；CanUpdate=false 列级兜底）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9, ServerTime = System.DateTimeKind.Local, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }

        /// <summary>更新时间（ServerTime Local——DB 填充）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, ServerTime = System.DateTimeKind.Local)]
        public DateTimeOffset UpdateTime { get; set; }
    }
}
