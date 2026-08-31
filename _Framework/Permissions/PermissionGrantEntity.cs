using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// 权限授予表实体——(PermissionName, ProviderName, ProviderKey) 业务唯一。
    /// <para>V0.2.0（SG1 化）：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（<b>不指定 UserType</b>，
    /// ADR42 D4 扩展实体不自建 UserInfo，DataService 用非泛型 <c>DomainDataServiceBase&lt;TEntity,TDto&gt;</c>
    /// 接受消费方上下文）。SG1 自动生成 <see cref="IDomainEntity"/> 部分（含 <c>IsFromPersistentSource</c>
    /// 默认实现覆盖）与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("PermissionGrant")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表，
    /// V4.9.57+ 仅发现 BCL <c>[Table]</c>）；列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，
    /// 全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("PermissionGrant")]
    [DomainGenerateCode(DefaultPageSize = 50, SubDomain = "Permissions", SubDomainRoutePrefix = "/Permissions")]
    public partial class PermissionGrantEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>权限名（如 "Order.Create"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(200)]
        public string PermissionName { get; set; } = "";

        /// <summary>授予 provider（如 "User"/"Role"/"Member"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string ProviderName { get; set; } = "";

        /// <summary>provider 键（如 UserIdString）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = "";

        /// <summary>是否授予。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsGranted { get; set; }
    }
}
