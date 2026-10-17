using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>消息路由规则实体（表 <c>TKWF_WeChatMessageRouteRule</c>）——网关消息路由规则（V0.2.0 增网关模块）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="MsgType"/>（string）：微信消息类型（text/image/voice/video/shortvideo/location/link/event）——空 = 全类型。      <see cref="Event"/>（string）：仅 <see cref="MsgType"/> = event 参与（subscribe/unsubscribe/SCAN/CLICK/LOCATION），空 = 全事件。</para>      <para><see cref="MatchMode"/>（int）：0=None 无匹配（纯类型/事件路由）/ 1=Exact 完全匹配 / 2=Contains 包含匹配。      <see cref="TriggerKey"/>：匹配键（MatchMode=None 时恒空）。</para>      <para><see cref="ForwardMode"/>（int）：1=LocalOnly 仅本地回复 / 2=AsyncCopy 异步复制 / 3=SyncProxy 同步代理 /      4=PreprocessSync 预处理后同步。<see cref="EndpointId"/>：下游端点引用——LocalOnly 恒空（仅本地回复不转发下游）。</para>      <para>规则读一律按 <see cref="ChannelId"/> 过滤（多公众号数据隔离），同 channel 内按 <see cref="SortOrder"/> 升序尝试。</para></summary>
public partial class MessageRouteRuleEntity
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