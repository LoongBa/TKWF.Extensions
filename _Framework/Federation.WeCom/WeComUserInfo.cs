namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信身份信息（<c>getuserinfo</c>/<c>getuserinfo3rd</c> 响应裁剪——双授权流统一收 code 换身份，无独立 token 端点）。
/// <para>三标识形态（Oracle 评审 P0-1 双策略来源）：企业成员返回 <see cref="UserId"/>（自建）/ <see cref="OpenUserid"/>
/// （三方——体系内明示<b>全局唯一</b>）；非企业成员返回 <see cref="OpenId"/>（对当前企业唯一）。</para>
/// <para>external_uid 双策略（P0-1 定案——选取逻辑在 <see cref="WeComApiClient.GetIdentityAsync"/>）：三方成员
/// <c>open_userid</c> / 自建成员 <c>{CorpId}:{userid}</c> 复合 / 非成员 <c>openid</c>；<see cref="UserId"/> 仅组织
/// 维度辅助（DTO 透出/绑定流程用，非联邦映射主键——离职复用风险经 N2 §3.2 治理）。</para>
/// <para><see cref="UserTicket"/>：<c>snsapi_privateinfo</c> 响应条件性返回（自建 96442 + 三方 98179 均返回；
/// 扫码 98177 不返回——Oracle 评审 P1-4 修正），1800s 即用即弃不落库——供装配层经
/// <see cref="WeComApiClient.GetSensitiveInfoAsync"/> 换取敏感信息。</para>
/// </summary>
/// <param name="UserId">企业成员 userid（自建 getuserinfo；三方 getuserinfo3rd 亦返回 Corpid+Userid 组织维度辅助）。</param>
/// <param name="OpenId">非企业成员 openid（对当前企业唯一）。</param>
/// <param name="OpenUserid">三方应用成员 open_userid（体系内明示全局唯一——同服务商跨应用相同，优先作 external_uid）。</param>
/// <param name="ExternalUserid">外部联系人 external_userid（条件性返回——DTO 透出作锚点辅助）。</param>
/// <param name="UserTicket">敏感信息票据（snsapi_privateinfo 响应；1800s 即用即弃，扫码场景不返回）。</param>
public sealed record WeComUserInfo(
    string? UserId,
    string? OpenId,
    string? OpenUserid,
    string? ExternalUserid,
    string? UserTicket);

/// <summary>
/// 企业微信敏感信息（<c>getuserdetail</c> 响应裁剪——user_ticket 一次性换取<b>即用即弃不落库</b>，
/// Oracle 评审 P1-4 数据流闭环：ApiClient 换取 → 本 DTO → 编排层帧内 <c>User.Use&lt;ISsoAccountQueryService&gt;()</c>
/// 消费做绑定 → 帧结束随 GC 回收即弃——不进审计日志 ArgumentsJson/不持久化）。
/// <para><b>⚠️ 敏感字段（Name/Mobile/Email）标注防日志泄露（Oracle 评审 P2-3）</b>——不得写入结构化日志参数、
/// 审计 ArgumentsJson 或任何持久化介质；DeviceId 不透出（隐私最小化）。</para>
/// </summary>
/// <param name="UserId">成员 userid。</param>
/// <param name="Name">姓名（敏感——防日志泄露）。</param>
/// <param name="Mobile">手机号（敏感——防日志泄露）。</param>
/// <param name="Email">邮箱（敏感——防日志泄露）。</param>
/// <param name="Avatar">头像 URL。</param>
/// <param name="Gender">性别（0 未定义/1 男/2 女）。</param>
public sealed record WeComSensitiveInfo(
    string? UserId,
    string? Name,
    string? Mobile,
    string? Email,
    string? Avatar,
    int Gender);
