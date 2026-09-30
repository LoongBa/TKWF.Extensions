using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 过渡期模板基类（兑换历史源）——装配实例继承此基类实现 <see cref="IRedemptionHistorySource"/>，仅需实现查询钩子。
/// <para>异常降级：Source 抛异常 → 空列表 + Warning（对齐用户中心降级矩阵）——不阻塞用户中心页面渲染。</para>
/// <para>终态：授权面扩展内实现白地接口（兑换记录数据自有）。</para>
/// </summary>
public abstract class RedemptionHistorySourceBase : IRedemptionHistorySource
{
    private readonly ILogger _logger;

    /// <summary>构造——注入日志。</summary>
    protected RedemptionHistorySourceBase(ILogger<RedemptionHistorySourceBase> logger) => _logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var raws = await QueryRawRedemptionsAsync(userId, ct);
            var result = new List<RedemptionRecordDto>(raws.Count);
            foreach (var raw in raws)
                result.Add(MapToRedemption(raw));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UserCenter 兑换历史读取失败（userId={UserId}），降级为空列表", userId);
            return [];
        }
    }

    /// <summary>装配实例实现——返回原始兑换行列表。</summary>
    protected abstract Task<IReadOnlyList<RawRedemption>> QueryRawRedemptionsAsync(string userId, CancellationToken ct);

    /// <summary>映射钩子（可覆写）——默认投影（CodeMasked 由实现方保证已脱敏，门面透传）。</summary>
    protected virtual RedemptionRecordDto MapToRedemption(RawRedemption raw)
        => new(raw.CodeMasked, raw.ProductName, raw.TargetAppId, raw.RedeemedAtUtc, raw.Status);

    /// <summary>原始兑换行载体（sealed record 默认投影）。</summary>
    public sealed record RawRedemption(
        string CodeMasked,
        string? ProductName,
        string? TargetAppId,
        DateTime RedeemedAtUtc,
        string Status);
}