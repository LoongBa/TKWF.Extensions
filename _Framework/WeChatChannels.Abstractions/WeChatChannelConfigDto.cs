using System;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 公众号配置投影 DTO（<b>不含凭证密文</b>——Oracle P1 DTO 投影安全：AppSecret/Token/EncodingAESKey
/// 明文与密文均不回显；<see cref="HasCredentials"/> 仅指示是否已配置凭证）。</summary>
/// <param name="Id">主键。</param>
/// <param name="ChannelId">channel 实例 id。</param>
/// <param name="AppId">公众号 AppId（出站凭证解析键）。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Remark">备注（可空）。</param>
/// <param name="IsEnabled">启用。</param>
/// <param name="ChannelType">通道类型（wechat_event / wechat_oauth 等）。</param>
/// <param name="Alias">对外别名（D03 1.6——审核 P2-1：管理读 API 可见别名，运维可确认是否已一次性设置）。</param>
/// <param name="HasCredentials">是否已配置凭证（AppSecret/Token/EncodingAESKey 任一密文非空）。</param>
/// <param name="CreateTime">创建时间（UTC）。</param>
/// <param name="UpdateTime">更新时间（UTC）。</param>
/// <param name="ChannelForm">通道形态（D03 §4.1——mp_subscription / mp_service / mini_program / work_wechat；
/// 外部消费方展示/判断用，实体透传）。</param>
/// <param name="Verification">认证状态（D03 §4.1——none / verified；外部消费方展示/判断用，实体透传）。</param>
public sealed record WeChatChannelConfigDto(
    long Id,
    string ChannelId,
    string AppId,
    string DisplayName,
    string? Remark,
    bool IsEnabled,
    string ChannelType,
    string? Alias,
    bool HasCredentials,
    DateTime CreateTime,
    DateTime UpdateTime,
    string ChannelForm = "mp_service",
    string Verification = "none");