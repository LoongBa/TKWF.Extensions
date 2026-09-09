using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Calendar;

/// <summary>日历实体——多日历隔离（Code 唯一索引，F1）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("Calendar")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>     <para>删除语义：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类 <c>hasSoftDelete:false</c>；     删除保护（无事件）由 Manager 层强制（D17）。</para>     <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明（对齐 DataDictionary/Tagging 先例，     FreeSql SQLite 不支持 DateTimeOffset）；不声明 IsDeleted（物理删除）。</para></summary>
public partial class CalendarEntity
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