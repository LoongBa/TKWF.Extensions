using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Permissions;

/// <summary>权限授予表实体——(PermissionName, ProviderName, ProviderKey) 业务唯一。     <para>V0.2.0（SG1 化）：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（<b>不指定 UserType</b>，     ADR42 D4 扩展实体不自建 UserInfo，DataService 用非泛型 <c>DomainDataServiceBase&lt;TEntity,TDto&gt;</c>     接受消费方上下文）。SG1 自动生成 <see cref="!:IDomainEntity"/> 部分（含 <c>IsFromPersistentSource</c>     默认实现覆盖）与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("PermissionGrant")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表，     V4.9.57+ 仅发现 BCL <c>[Table]</c>）；列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，     全限定避免与 BCL Schema 特性名冲突）。</para></summary>
public partial class PermissionGrantEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查) 
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}