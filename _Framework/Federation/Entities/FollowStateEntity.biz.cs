using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation;

/// <summary>关注关系状态聚合根（I2 渠道关注关系域——「当前是否关注」唯一权威；单用户单渠道一行）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4；对齐      <see cref="SsoChannelRegistryEntity"/> 体例）。</para>      <para>排序守卫（方案 §4.3 oracle6 C4——<b>不可用元组 &gt; 运算符</b>，且该形式无法翻译 SQL，必须两段比较）：      <code>      newer = evt.OccurredAt &gt; state.LastEventAt           || (evt.OccurredAt == state.LastEventAt &amp;&amp; evt.ReceivedAt &gt; state.LastReceivedAt);      </code>      仅当 <c>newer</c> 时覆盖状态字段；事件行无论是否 newer 都落库（事实不可丢）。两处实现必须同型：      ① <c>FollowStateService.RecordAsync</c> 的 LINQ <c>Where</c>（条件更新 <c>EntityUpdateWhereAsync</c>）；      ② 更新前的内存判定（返回 0 行时判定「被排序守卫挡下」而非「行不存在」）。</para>      <para>聚合根语义（§2.2/R8）：状态是<b>领域事实</b>（非派生投影）——事件流重放是重建手段非定义，Rebuild 只用于      数据修复；与 Iteration 1 D9「禁派生投影列」判据区分（见 ADR）。</para></summary>
public partial class FollowStateEntity
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