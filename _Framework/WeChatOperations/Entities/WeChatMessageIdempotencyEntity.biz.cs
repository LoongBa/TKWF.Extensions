using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>消息幂等实体（表 <c>TKWF_WeChatMessageIdempotency</c>）——网关消息幂等键（防止同一微信消息重复转发/重复回复，V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="IdempotencyKey"/>：规范化幂等键（ChannelId+MsgId+FromUserName+CreateTime 规范化拼接——      去空白/统一大小写后哈希或定长拼串，语义唯一）。</para>      <para><see cref="CreateTime"/>：微信原始消息时间（<b>原样留存，非 UTC 转换</b>——微信时间戳语义保留）；      <see cref="FirstSeenAt"/>：首次见到该键时间（UTC）。</para>      <para><see cref="LastStatus"/>（int）：1=Pending 处理中 / 2=Forwarded 已转发 / 3=Skipped 已跳过（重复/无规则）。</para>      <para>唯一索引：<see cref="IdempotencyKey"/>——同键二次入站幂等命中（防微信重推）。</para></summary>
public partial class WeChatMessageIdempotencyEntity
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