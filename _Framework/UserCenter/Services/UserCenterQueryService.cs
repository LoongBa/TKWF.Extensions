using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 用户中心门面实现——组合三 Source + 门面输出强制脱敏 + 缺省降级。
/// <para>领域逻辑进扩展：脱敏（PhoneMasker 强制）+ 降级（未注册/异常 → null/空列表）为单一真相源，
/// 实现方（认证中心/授权面/装配实例）不需要也不得自行处理——防漏防重。</para>
/// <para>DI 注入语义（Oracle P4）：三 Source 经 <c>IServiceProvider.GetService&lt;T&gt;()</c> 可空解析——
/// 未注册 Source 时门面仍可构造，按降级矩阵工作（不采用必需注入，避免未注册即构造失败）。</para>
/// </summary>
public sealed class UserCenterQueryService : IUserCenterQueryService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UserCenterQueryService> _logger;

    /// <summary>构造——注入服务定位器（Source 可空解析）+ 日志。</summary>
    public UserCenterQueryService(IServiceProvider services, ILogger<UserCenterQueryService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var source = _services.GetService<IUserProfileSource>();
        if (source is null)
        {
            _logger.LogWarning("UserCenter 档案读取降级为 null：未注册 IUserProfileSource（认证中心 v0.2.0+ 或装配实例需注册）");
            return null;
        }

        try
        {
            var profile = await source.GetProfileAsync(userId, ct);
            if (profile is null)
                return null;

            // 门面强制脱敏（单一真相源——实现方返回原始 Phone，此处统一 Mask）
            return profile with { Phone = PhoneMasker.Mask(profile.Phone) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 异常静默（Warning 不冒泡）——不阻塞用户中心页面渲染（§5.3 降级矩阵）
            _logger.LogWarning(ex, "UserCenter 档案读取失败（userId={UserId}），降级为 null", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default)
    {
        var source = _services.GetService<IRedemptionHistorySource>();
        if (source is null)
            return [];

        try
        {
            return await source.GetRedemptionsAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UserCenter 兑换历史读取失败（userId={UserId}），降级为空列表", userId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default)
    {
        var source = _services.GetService<IUserAppsSource>();
        if (source is null)
            return [];

        try
        {
            return await source.GetAppsAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UserCenter 我的应用读取失败（userId={UserId}），降级为空列表", userId);
            return [];
        }
    }
}