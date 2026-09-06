using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.DataPort.DTOs;

/// <summary>数据导入批次记录实体——记录每次导入的元数据（批次号/文件哈希/状态/统计/错误摘要）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("DataImportRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>     <para>FileHash 唯一索引防重复导入（幂等检查入口）；BatchNo 供消费方按批次回滚。</para> 的手写 DTO 扩展</summary>
public partial record DataImportRecordEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}