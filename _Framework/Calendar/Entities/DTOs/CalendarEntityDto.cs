using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Calendar.DTOs;

/// <summary>日历实体——多日历隔离（Code 唯一索引，F1）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("Calendar")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>     <para>删除语义：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类 <c>hasSoftDelete:false</c>；     删除保护（无事件）由 Manager 层强制（D17）。</para>     <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明（对齐 DataDictionary/Tagging 先例，     FreeSql SQLite 不支持 DateTimeOffset）；不声明 IsDeleted（物理删除）。</para> 的手写 DTO 扩展</summary>
public partial record CalendarEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}