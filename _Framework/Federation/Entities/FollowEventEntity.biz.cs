using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation;

/// <summary>关注/取关事件事实表（I2 渠道关注关系域——追加写，事实不可丢：事件行无论是否排序更新都落库）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例；      对齐 <see cref="SsoChannelRegistryEntity"/> 体例）。</para>      <para>幂等（方案 §4.3）：<see cref="DedupeKey"/> = <c>{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}</c>      （超 256 字符 → SHA256 十六进制——保留可判定性，截断会产生假碰撞）；微信事件无 MsgId，CreateTime 秒级 + FromUserName + Event      已足够唯一，重复推送必然同键。</para>      <para><see cref="Source"/>（R5）对齐 WeChatOperations 统计维度（扫码来源/渠道标识）；<see cref="EventType"/>      = follow / unfollow / scan_follow / scan_only / message / menu_click（平台厂商语义归一）。</para></summary>
public partial class FollowEventEntity
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