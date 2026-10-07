using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 公共档案源——数据属主（认证中心 v0.2.0+）或装配实例（过渡期）实现。
/// <para>属主扩展在本扩展 Initializer 的 ConfigureServices 中 <c>TryAddScoped</c> 注册实现；
/// 装配实例（过渡期）继承 <c>UserProfileSourceBase</c> 最小适配（模板基类，见 UserCenter 主包）。</para>
/// <para>⚠️ <b>V0.9.0 退役（ADR-AuthCenter-身份域数据模型与密码能力边界 C.14）</b>：UserCenter 零存储聚合层退役——
/// 本接口 DEPRECATE（V0.9.0 标 Obsolete + 保留实现，V1.0.0 移除）；档案读改经 AuthCenter
/// <c>IAuthAccountQueryService.GetProfileByUIdAsync</c>（UserProfile 1:1 表）或业务扩展 VEntity。</para>
/// </summary>
[Obsolete("UserCenter 聚合层退役（ADR C.14）——V1.0.0 移除；档案读改经 AuthCenter IAuthAccountQueryService.GetProfileByUIdAsync 或业务扩展 VEntity")]
public interface IUserProfileSource
{
    /// <summary>
    /// 按平台内部 id（JWT sub / DomainUser 标识）取公共档案；不存在返回 null。
    /// </summary>
    /// <remarks>返回的 <see cref="UserProfileDto.Phone"/> 为<b>原始</b>手机号——
    /// 门面输出前经 PhoneMasker 强制脱敏（实现方不需自行脱敏）。</remarks>
    Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// 兑换历史源——授权面（口令兑换体系）或装配实例实现。
/// </summary>
/// <remarks>与 <see cref="IUserAppsSource"/> 拆为两接口（同属授权面）：
/// 支持授权面扩展<b>分阶段实现</b>（先兑换后应用），单一接口迫使一次性实现全量。</remarks>
public interface IRedemptionHistorySource
{
    /// <summary>
    /// 取用户口令兑换历史。
    /// </summary>
    /// <remarks><see cref="RedemptionRecordDto.CodeMasked"/> 由实现方保证脱敏
    /// （授权面已脱敏存储，UserCenter 不持有/不存储明文，仅透传）。</remarks>
    Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// 我的应用源——授权面或装配实例实现（聚合授权 + 使用情况，<b>不含学习明细</b>——儿童数据红线，契约文档约束）。
/// </summary>
public interface IUserAppsSource
{
    /// <summary>取用户已授权应用列表。</summary>
    Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// 用户中心门面——消费方注入入口（UserCenter 主包实现）。
/// <para>⚠️ <b>仅本人防护</b>（需求 L385）：本门面方法显式接收 userId 参数，
/// 调用方（装配端点）必须从 DomainUser/令牌解析 userId，<b>不信任客户端传参</b>——
/// 越权查询他人档案 = 装配层责任（门面不做用户上下文隐式推断，防隐式 IDOR）。</para>
/// </summary>
public interface IUserCenterQueryService
{
    /// <summary>取公共档案（Phone 输出前强制脱敏）；档案不存在返回 null。</summary>
    Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>取兑换历史（未注册 Source → 空列表；code 已脱敏透传）。</summary>
    Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default);

    /// <summary>取我的应用（未注册 Source → 空列表）。</summary>
    Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default);
}