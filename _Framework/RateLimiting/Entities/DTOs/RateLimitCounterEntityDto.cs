using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.RateLimiting.DTOs;

/// <summary>限流计数表实体——点检查原语（<c>IRateLimitCheck</c>）的 DB 持久化 Provider 计数底册。  <para>SG1 化：声明式实体——partial + <c>[DomainGenerateCode]</c>。</para>  <para>语义（方案 v0.3.0 §五 R2 / ADR-RateLimiting 决策点 2）：fixed window 单窗口计数器——Key 唯一，  窗口内 <c>Count</c> 经 ADR89 <c>UpdateWhereAsync</c> CAS 原子递增（<c>SET Count=Count+1 WHERE Key=@key AND  WindowEndUtc&gt;=@now AND Count&lt;@max</c>），跨实例 DB 行锁正确；首插靠 Key 唯一约束捕获并发冲突重试；  惰性淘汰（过期行随下一次命中重置，无后台清理——孤儿键增长 v0.2.0 清理任务）。</para>  <para>表名/索引 <c>TKWF_</c>/<c>TKWFIX_</c> 前缀（ADR100 表名前缀批次 A 已闭环——新表直接带前缀）。</para> 的手写 DTO 扩展</summary>
public partial record RateLimitCounterEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}