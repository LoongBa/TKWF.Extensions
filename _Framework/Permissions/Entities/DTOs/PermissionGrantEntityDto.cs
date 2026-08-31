using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Permissions.DTOs;

/// <summary>权限授予表实体——(PermissionName, ProviderName, ProviderKey) 业务唯一。     <para>V0.2.0（SG1 化）：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（<b>不指定 UserType</b>，     ADR42 D4 扩展实体不自建 UserInfo，DataService 用非泛型 <c>DomainDataServiceBase&lt;TEntity,TDto&gt;</c>     接受消费方上下文）。SG1 自动生成 <see cref="!:IDomainEntity"/> 部分（含 <c>IsFromPersistentSource</c>     默认实现覆盖）与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("PermissionGrant")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表，     V4.9.57+ 仅发现 BCL <c>[Table]</c>）；列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，     全限定避免与 BCL Schema 特性名冲突）。</para> 的手写 DTO 扩展</summary>
public partial record PermissionGrantEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}