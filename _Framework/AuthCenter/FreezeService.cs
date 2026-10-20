using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 多原因冻结服务实现（V0.11.0 Wave 1b——方案 §4.6 D3）——委托 <see cref="AuthAccountFreezeEntityDataService"/>
/// （红线合规，零 IFreeSql/IEntityDAC 直注入）；活跃判据 <c>ReleasedAt == null &amp;&amp; (FreezeEnd == null ||
/// FreezeEnd &gt; UtcNow)</c>（实体注释固化的唯一真值——无投影列）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// </summary>
[DiContractIgnore]
internal sealed class FreezeService : DomainServiceBase, IFreezeService
{
    /// <summary>冻结范围：0=Account（全站登录）。<b>V0.11.0 恒为 0</b>，其余为预留位（实体注释固化不变量）。</summary>
    private const int AccountScope = 0;

    private AuthAccountFreezeEntityDataService? _dataService;
    private AuthAccountFreezeEntityDataService DataService => _dataService ??= User.Use<AuthAccountFreezeEntityDataService>();

    private IAuthAccountService? _accounts;
    private IAuthAccountService Accounts => _accounts ??= User.Use<IAuthAccountService>();

    private readonly IOptions<SecurityLoggingOptions> _securityLogOptions;
    private readonly ILogger<FreezeService> _logger;

    public FreezeService(
        IDomainUser user,
        IOptions<SecurityLoggingOptions> securityLogOptions,
        ILogger<FreezeService> logger) : base(user)
    {
        _securityLogOptions = securityLogOptions ?? throw new ArgumentNullException(nameof(securityLogOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AuthAccountFreezeEntity> FreezeAsync(
        string uid, string reason, DateTime? freezeEnd, string? note, string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (await Accounts.GetByUIdAsync(uid, ct) is null)
            throw new AuthenticationException("ACCOUNT_NOT_FOUND");

        var now = DateTime.UtcNow;

        // 幂等：同 (UId, Reason, Scope) 的活跃行不重复建（方案 §4.6）
        var existing = await DataService.EntityGetAsync(
            f => f.UId == uid && f.Reason == reason && f.Scope == AccountScope
              && f.ReleasedAt == null && (f.FreezeEnd == null || f.FreezeEnd > now), ct);
        if (existing != null)
            return existing;

        var created = await DataService.EntityCreateAsync(new AuthAccountFreezeEntity
        {
            UId = uid,
            Reason = reason,
            Scope = AccountScope,
            Source = source,
            FreezeEnd = freezeEnd,
            CreatedAt = now,
            CreatedBy = CurrentOperatorName(),
            Note = note,
        }, ct);

        InvalidateSnapshot(uid);   // 写路径负责失效读取面快照（方案 §4.6 帧内缓存）
        await WriteSecurityLogAsync(
            SecurityLogEventTypes.Freeze, uid, created.CreatedBy,
            $"原因={reason}；到期={freezeEnd?.ToString("O") ?? "永久"}{(string.IsNullOrWhiteSpace(note) ? string.Empty : $"；备注={note}")}", ct);
        return created;
    }

    /// <inheritdoc />
    public async Task ReleaseFreezeAsync(string uid, string reason, string? operatorName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = DateTime.UtcNow;
        var active = await DataService.EntitySelectAsync(
            f => f.UId == uid && f.Reason == reason && f.ReleasedAt == null
              && (f.FreezeEnd == null || f.FreezeEnd > now), ct: ct);
        await ReleaseRowsAsync(active, operatorName, now, ct);
        if (active.Count > 0)
        {
            InvalidateSnapshot(uid);
            await WriteSecurityLogAsync(SecurityLogEventTypes.Unfreeze, uid, operatorName, $"按原因释放：原因={reason}（{active.Count} 行）", ct);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAllFreezesAsync(string uid, string? operatorName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        var now = DateTime.UtcNow;
        var active = await DataService.EntitySelectAsync(
            f => f.UId == uid && f.ReleasedAt == null && (f.FreezeEnd == null || f.FreezeEnd > now), ct: ct);
        await ReleaseRowsAsync(active, operatorName, now, ct);
        if (active.Count > 0)
        {
            InvalidateSnapshot(uid);
            await WriteSecurityLogAsync(SecurityLogEventTypes.Unfreeze, uid, operatorName, $"释放全部冻结（{active.Count} 行）", ct);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsFrozenAsync(string uid, int? scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        var now = DateTime.UtcNow;
        var hit = scope.HasValue
            ? await DataService.EntityGetAsync(
                f => f.UId == uid && f.Scope == scope.Value && f.ReleasedAt == null
                  && (f.FreezeEnd == null || f.FreezeEnd > now), ct)
            : await DataService.EntityGetAsync(
                f => f.UId == uid && f.ReleasedAt == null && (f.FreezeEnd == null || f.FreezeEnd > now), ct);
        return hit != null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuthAccountFreezeEntity>> ListActiveFreezesAsync(string uid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        var now = DateTime.UtcNow;
        return await DataService.EntitySelectAsync(
            f => f.UId == uid && f.ReleasedAt == null && (f.FreezeEnd == null || f.FreezeEnd > now),
            orderBy: q => q.OrderBy(x => x.CreatedAt), ct: ct);
    }

    /// <summary>软释放（置 ReleasedAt/ReleasedBy——保留审计行，冻结历史不删）。</summary>
    private async Task ReleaseRowsAsync(
        IReadOnlyList<AuthAccountFreezeEntity> rows, string? operatorName, DateTime now, CancellationToken ct)
    {
        var releasedBy = operatorName ?? CurrentOperatorName();
        foreach (var row in rows)
        {
            row.ReleasedAt = now;
            row.ReleasedBy = releasedBy;
            await DataService.EntityUpdateAsync(row, ct);
        }
    }

    /// <summary>帧内操作者显示名（未认证/系统 → null）。</summary>
    private string? CurrentOperatorName()
        => User.IsAuthenticated && !string.IsNullOrEmpty(User.UserName) ? User.UserName : null;

    /// <summary>冻结行变更后失效读取面同帧快照（<see cref="IFreezeStateProvider"/>——写路径负责失效，方案 §4.6）。
    /// 惰性解析（DI004 零豁免）；写→读同帧（冻结后立刻判定）语义正确。</summary>
    private void InvalidateSnapshot(string uid)
        => User.Use<IFreezeStateProvider>().Invalidate(uid);

    /// <summary>SecurityLog 直写（绕过过滤器门控，自读 Options；失败降级不阻断冻结业务）。</summary>
    private async Task WriteSecurityLogAsync(string eventType, string accountUid, string? operatorName, string detail, CancellationToken ct)
    {
        var options = _securityLogOptions.Value;
        if (!options.Enabled) return;
        if (options.EventTypes.Count > 0 && !options.EventTypes.Contains(eventType)) return;

        try
        {
            var store = User.Use<ISecurityLogStore>();
            await store.SaveAsync(new SecurityLogEntry(
                EventType: eventType,
                EventCategory: SecurityLogEventTypes.CategoryAuthentication,
                UserName: accountUid,
                UserId: null,
                IpAddress: null,
                UserAgent: null,
                Result: SecurityLogEventTypes.ResultSuccess,
                Detail: string.IsNullOrEmpty(operatorName) ? detail : $"操作者={operatorName}；{detail}",
                CorrelationId: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "冻结事件 SecurityLog 直写失败——降级不阻断（EventType={EventType}, UId={UId}）", eventType, accountUid);
        }
    }
}
