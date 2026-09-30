using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 过渡期模板基类（我的应用源）——装配实例继承此基类实现 <see cref="IUserAppsSource"/>，仅需实现查询钩子。
/// <para>异常降级：Source 抛异常 → 空列表 + Warning（对齐用户中心降级矩阵）。</para>
/// <para>终态：授权面扩展内实现白地接口（授权快照数据自有）。</para>
/// </summary>
public abstract class UserAppsSourceBase : IUserAppsSource
{
    private readonly ILogger _logger;

    /// <summary>构造——注入日志。</summary>
    protected UserAppsSourceBase(ILogger<UserAppsSourceBase> logger) => _logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var raws = await QueryRawAppsAsync(userId, ct);
            var result = new List<UserAppDto>(raws.Count);
            foreach (var raw in raws)
                result.Add(MapToApp(raw));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UserCenter 我的应用读取失败（userId={UserId}），降级为空列表", userId);
            return [];
        }
    }

    /// <summary>装配实例实现——返回原始应用列表。</summary>
    protected abstract Task<IReadOnlyList<RawApp>> QueryRawAppsAsync(string userId, CancellationToken ct);

    /// <summary>映射钩子（可覆写）——默认投影（UsageSummary 契约约束：不含学习明细——儿童数据红线）。</summary>
    protected virtual UserAppDto MapToApp(RawApp raw)
        => new(raw.AppId, raw.AppName, raw.IsAuthorized, raw.ExpiresAtUtc, raw.UsageSummary);

    /// <summary>原始应用载体（sealed record 默认投影）。</summary>
    public sealed record RawApp(
        string AppId,
        string? AppName,
        bool IsAuthorized,
        DateTime? ExpiresAtUtc,
        string? UsageSummary);
}