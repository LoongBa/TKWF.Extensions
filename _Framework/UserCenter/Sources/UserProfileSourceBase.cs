using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 过渡期模板基类（公共档案源）——装配实例继承此基类实现 <see cref="IUserProfileSource"/>，仅需实现查询钩子。
/// <para>机制体现（对齐 <c>IdentityUserHelperBase</c> / <c>AuthenticationUserHelperBase</c> 先例）：
/// 映射/降级/日志管线预实现于扩展内，装配实例只补"查数据"最短钩子——领域逻辑留在扩展。</para>
/// <para>终态：认证中心 v0.2.0+ 扩展内<b>直接实现白地接口</b>（AuthAccount 数据自有，无需模板钩子）
/// 并 <c>TryAddScoped</c> 注册——装配实例删除过渡桥接类即可，消费方零代码变更。</para>
/// </summary>
public abstract class UserProfileSourceBase : IUserProfileSource
{
    private readonly ILogger _logger;

    /// <summary>构造——注入日志（脱敏降级管线用）。</summary>
    protected UserProfileSourceBase(ILogger<UserProfileSourceBase> logger) => _logger = logger;

    /// <inheritdoc />
    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var raw = await QueryRawProfileAsync(userId, ct);   // 装配实例仅实现：查 AuthAccount / 其他身份源
            return raw is null ? null : MapToProfile(userId, raw);  // 映射钩子（可覆写，默认投影）
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UserCenter 档案读取失败（userId={UserId}），降级为 null", userId);
            return null;
        }
    }

    /// <summary>装配实例实现——返回原始档案行（任意自有类型：AuthAccountEntity / 自定义 POCO）。</summary>
    protected abstract Task<RawProfile?> QueryRawProfileAsync(string userId, CancellationToken ct);

    /// <summary>映射钩子（可覆写）——默认投影公共档案字段 + Phone 原始值（门面负责脱敏）。</summary>
    protected virtual UserProfileDto MapToProfile(string userId, RawProfile raw)
        => new(userId, raw.Phone, raw.IsWechatBound, raw.Nickname, raw.AvatarUrl, raw.IsTeacherVerified, raw.AuthLevel);

    /// <summary>原始档案行载体（sealed record「默认投影」——装配实例可覆写 MapToProfile 从自有类型直接构造 DTO，Raw* 非强约束）。</summary>
    public sealed record RawProfile(
        string? Phone,
        bool IsWechatBound,
        string? Nickname,
        string? AvatarUrl,
        bool IsTeacherVerified,
        int AuthLevel);
}