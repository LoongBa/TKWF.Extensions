using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>消息幂等实体（表 <c>TKWF_WeChatMessageIdempotency</c>）——网关消息幂等键（防止同一微信消息重复转发/重复回复，V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="IdempotencyKey"/>：规范化幂等键（ChannelId+MsgId+FromUserName+CreateTime 规范化拼接——      去空白/统一大小写后哈希或定长拼串，语义唯一）。</para>      <para><see cref="CreateTime"/>：微信原始消息时间（<b>原样留存，非 UTC 转换</b>——微信时间戳语义保留）；      <see cref="FirstSeenAt"/>：首次见到该键时间（UTC）。</para>      <para><see cref="LastStatus"/>（int）：1=Pending 处理中 / 2=Forwarded 已转发 / 3=Skipped 已跳过（重复/无规则）。</para>      <para>唯一索引：<see cref="IdempotencyKey"/>——同键二次入站幂等命中（防微信重推）。</para> 的手写 DTO 扩展</summary>
public partial record WeChatMessageIdempotencyEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}