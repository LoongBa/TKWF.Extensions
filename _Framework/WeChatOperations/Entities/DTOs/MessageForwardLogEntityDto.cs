using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>消息转发日志实体（表 <c>TKWF_WeChatMessageForwardLog</c>）——网关消息转发流水（一次入站 → 路由决策 → 出站结果全链记录，V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="Status"/>（int）：1=Queued 已入队 / 2=Success 成功 / 3=Fail 失败 / 4=Timeout 超时 /      5=SkippedDuplicate 重复跳过（幂等命中）/ 6=NoRule 无匹配规则。</para>      <para><see cref="ResponseBody"/> 截断 500（出站响应原样截断留存）；<see cref="ErrorMsg"/> 脱敏 1000（异常消息不含明文凭证）。</para>      <para>日志按 <see cref="ChannelId"/> + <see cref="MsgId"/> 检索（非唯一索引——同一消息多路由/重试可多行）。</para> 的手写 DTO 扩展</summary>
public partial record MessageForwardLogEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}