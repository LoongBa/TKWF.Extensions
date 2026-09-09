using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Calendar.DTOs;

/// <summary>日历事件实体——单次或重复事件（归属日历 + UTC 时间 + 全天标记，F2/F3）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("CalendarEvent")]</c>；列映射用 FreeSql <c>[Column]</c>（全限定）。</para>     <para>重复语义（ADR-Calendar C1/C2/P4）：<c>RecurrenceRule</c> 存 <c>RecurrenceRule.ToString()</c> 规范规则串     （可空 = 单次事件；列名存规则文本非 JSON）；<c>RecurrenceEndUtc</c> 为冗余截止（UNTIL 直取 / COUNT 与展开算法     <b>同源</b>推导取最后 occurrence——写入时受上限保护运行展开，偏差构造性消除），加速重复路径范围预筛。</para>     <para>时长语义（C1/P1/P2）：<c>EndUtc</c> 可空（空 = 瞬时事件）；重复事件允许非空 = <b>首 occurrence 时长锚点</b>     （occurrence 时长 = EndUtc - StartUtc，展开时起点 + 偏移）；AllDay 事件 EndUtc 落库 = 次日 00:00 UTC。</para>     <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）；occurrence 不物化——展开是查询时计算。</para>     <para>索引（P8）：<c>IX_CalendarEvent_CalendarId_StartUtc</c>（单次/默认路径）+ <c>IX_CalendarEvent_CalendarId_RecurrenceEndUtc</c>（重复路径预筛）。</para> 的手写 DTO 扩展</summary>
public partial record CalendarEventEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}