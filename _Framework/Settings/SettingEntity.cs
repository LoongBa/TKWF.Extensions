using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置表实体——存储分层键值对设置（名称 + 提供者定位 + 值 + 描述）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("Setting")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("Setting")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SettingEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>设置名称（同一 Provider 内唯一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(256)]
        public string Name { get; set; } = "";

        /// <summary>设置值（JSON 字符串）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public string? Value { get; set; }

        /// <summary>提供者名称（如 Global / Tenant / User）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string ProviderName { get; set; } = "";

        /// <summary>提供者键（租户 ID / 用户 ID 等，Global 层可为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(128)]
        public string? ProviderKey { get; set; }

        /// <summary>设置描述。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>是否对客户端可见（默认 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public bool IsVisibleToClients { get; set; } = true;

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}
