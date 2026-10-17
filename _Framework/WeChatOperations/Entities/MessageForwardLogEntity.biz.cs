using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>消息转发日志实体（表 <c>TKWF_WeChatMessageForwardLog</c>）——网关消息转发流水（一次入站 → 路由决策 → 出站结果全链记录，V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="Status"/>（int）：1=Queued 已入队 / 2=Success 成功 / 3=Fail 失败 / 4=Timeout 超时 /      5=SkippedDuplicate 重复跳过（幂等命中）/ 6=NoRule 无匹配规则。</para>      <para><see cref="ResponseBody"/> 截断 500（出站响应原样截断留存）；<see cref="ErrorMsg"/> 脱敏 1000（异常消息不含明文凭证）。</para>      <para>日志按 <see cref="ChannelId"/> + <see cref="MsgId"/> 检索（非唯一索引——同一消息多路由/重试可多行）。</para></summary>
public partial class MessageForwardLogEntity
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