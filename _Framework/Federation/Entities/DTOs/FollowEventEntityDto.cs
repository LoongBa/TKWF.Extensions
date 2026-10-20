using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Federation.DTOs;

/// <summary>关注/取关事件事实表（I2 渠道关注关系域——追加写，事实不可丢：事件行无论是否排序更新都落库）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例；      对齐 <see cref="SsoChannelRegistryEntity"/> 体例）。</para>      <para>幂等（方案 §4.3）：<see cref="DedupeKey"/> = <c>{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}</c>      （超 256 字符 → SHA256 十六进制——保留可判定性，截断会产生假碰撞）；微信事件无 MsgId，CreateTime 秒级 + FromUserName + Event      已足够唯一，重复推送必然同键。</para>      <para><see cref="Source"/>（R5）对齐 WeChatOperations 统计维度（扫码来源/渠道标识）；<see cref="EventType"/>      = follow / unfollow / scan_follow / scan_only / message / menu_click（平台厂商语义归一）。</para> 的手写 DTO 扩展</summary>
public partial record FollowEventEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}