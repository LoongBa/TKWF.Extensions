using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.RateLimiting;

/// <summary>限流计数表实体——点检查原语（<c>IRateLimitCheck</c>）的 DB 持久化 Provider 计数底册。  <para>SG1 化：声明式实体——partial + <c>[DomainGenerateCode]</c>。</para>  <para>语义（方案 v0.3.0 §五 R2 / ADR-RateLimiting 决策点 2）：fixed window 单窗口计数器——Key 唯一，  窗口内 <c>Count</c> 经 ADR89 <c>UpdateWhereAsync</c> CAS 原子递增（<c>SET Count=Count+1 WHERE Key=@key AND  WindowEndUtc&gt;=@now AND Count&lt;@max</c>），跨实例 DB 行锁正确；首插靠 Key 唯一约束捕获并发冲突重试；  惰性淘汰（过期行随下一次命中重置，无后台清理——孤儿键增长 v0.2.0 清理任务）。</para>  <para>表名/索引 <c>TKWF_</c>/<c>TKWFIX_</c> 前缀（ADR100 表名前缀批次 A 已闭环——新表直接带前缀）。</para></summary>
public partial class RateLimitCounterEntity
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