using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 应用目录 DTO——"我的应用"聚合的 AppName 来源（目录维护返回载体，授权面自有）。
/// </summary>
public sealed record AuthAppDto(
    string AppId,
    string AppName,
    string? Icon,
    bool IsEnabled);

/// <summary>
/// 应用目录管理门面——"我的应用" AppName 来源（运营数据维护）。
/// <para>⚠️ 软引用语义：<see cref="AuthAppDto.AppId"/> 与 <c>AuthGrant.AppId</c>/<c>RedemptionCode.TargetAppId</c> 为字符串约定
/// （无 FK 约束）——删除前检查引用；建议 <see cref="SetEnabledAsync"/> 软禁用而非物理删除（防 vm_UserApps 丢 AppName）。</para>
/// </summary>
public interface IAuthAppService : IDomainService
{
    /// <summary>创建应用（AppId 唯一——重复抛 InvalidOperationException("AUTHAPP_ALREADY_EXISTS")）。</summary>
    Task<AuthAppDto> CreateAppAsync(string appId, string appName, string? icon = null, CancellationToken ct = default);

    /// <summary>更新应用（AppId 不存在抛 InvalidOperationException("AUTHAPP_NOT_FOUND")；可空参数 = 不更新）。</summary>
    Task<AuthAppDto> UpdateAppAsync(string appId, string? appName = null, string? icon = null, CancellationToken ct = default);

    /// <summary>软禁用/启用（false = 应用不出现在"我的应用"聚合语义由目录状态承载——不物理删除）。</summary>
    Task SetEnabledAsync(string appId, bool isEnabled, CancellationToken ct = default);
}

/// <summary>应用目录管理门面实现——委托 <see cref="AuthAppEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）。</summary>
[DiContractIgnore]
internal sealed class AuthAppService : DomainServiceBase, IAuthAppService
{
    private readonly ILogger<AuthAppService> _logger;

    private AuthAppEntityDataService? _appDataService;
    private AuthAppEntityDataService AppDataService => _appDataService ??= User.Use<AuthAppEntityDataService>();

    public AuthAppService(IDomainUser user, ILogger<AuthAppService> logger) : base(user)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<AuthAppDto> CreateAppAsync(string appId, string appName, string? icon = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (await AppDataService.GetByAppIdAsync(appId, ct) is not null)
            throw new InvalidOperationException($"AUTHAPP_ALREADY_EXISTS appId={appId}");

        var now = DateTime.UtcNow;
        var entity = new AuthAppEntity
        {
            AppId = appId,
            AppName = appName,
            Icon = icon,
            IsEnabled = true,
            CreateTime = now,
            UpdateTime = now,
        };
        await AppDataService.EntityCreateAsync(entity, ct);
        return Map(entity);
    }

    /// <inheritdoc />
    public async Task<AuthAppDto> UpdateAppAsync(string appId, string? appName = null, string? icon = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        var entity = await AppDataService.GetByAppIdAsync(appId, ct)
            ?? throw new InvalidOperationException($"AUTHAPP_NOT_FOUND appId={appId}");

        if (appName is not null) entity.AppName = appName;
        if (icon is not null) entity.Icon = icon;
        entity.UpdateTime = DateTime.UtcNow;
        await AppDataService.EntityUpdateAsync(entity, ct);
        return Map(entity);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(string appId, bool isEnabled, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        var entity = await AppDataService.GetByAppIdAsync(appId, ct)
            ?? throw new InvalidOperationException($"AUTHAPP_NOT_FOUND appId={appId}");

        entity.IsEnabled = isEnabled;
        entity.UpdateTime = DateTime.UtcNow;
        await AppDataService.EntityUpdateAsync(entity, ct);
    }

    private static AuthAppDto Map(AuthAppEntity e)
        => new(e.AppId, e.AppName, e.Icon, e.IsEnabled);
}
