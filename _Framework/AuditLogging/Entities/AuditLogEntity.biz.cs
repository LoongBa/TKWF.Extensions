using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuditLogging;

/// <summary>审计日志表实体——记录方法级调用事件（调用者、目标方法、参数脱敏 JSON、耗时、成功/异常、关联 ID）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("AuditLog")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para></summary>
public partial class AuditLogEntity
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