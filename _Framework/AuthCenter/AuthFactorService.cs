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
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.AuthCenter.DTOs;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证因素服务实现（V0.11.0 Wave 1b——方案 §4.7 D4）——委托 <see cref="AuthFactorEntityDataService"/> +
/// <see cref="AuthFactorEventEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）；解绑六步中
/// TokenVersion++ 经 <c>User.Use&lt;IAuthAccountService&gt;()</c> 懒加载委托（DI004 零豁免）；
/// 事件写入 → 物理删 → TokenVersion++ 同 <see cref="ITransactionManager"/> UoW（方案 §6.2）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// </summary>
[DiContractIgnore]
internal sealed class AuthFactorService : DomainServiceBase, IAuthFactorService
{
    /// <summary>Identifier 上限（方案 §6.1——256 覆盖 channelId(64)+"|"+externalUserId(128)=193 + 余量）。</summary>
    private const int IdentifierMaxLength = 256;

    private AuthFactorEntityDataService? _factorDataService;
    private AuthFactorEntityDataService FactorDataService => _factorDataService ??= User.Use<AuthFactorEntityDataService>();
    private AuthFactorEventEntityDataService? _eventDataService;
    private AuthFactorEventEntityDataService EventDataService => _eventDataService ??= User.Use<AuthFactorEventEntityDataService>();
    private IAuthAccountService? _accountService;
    private IAuthAccountService AccountService => _accountService ??= User.Use<IAuthAccountService>();

    private readonly IOptions<SecurityLoggingOptions> _securityLogOptions;
    private readonly ILogger<AuthFactorService> _logger;

    /// <summary>守卫工厂仅供给帧内 <see cref="IDomainUser"/> / 日志等基础设施——领域服务禁构造注入业务依赖（DI004 零豁免）。</summary>
    public AuthFactorService(
        IDomainUser user,
        IOptions<SecurityLoggingOptions> securityLogOptions,
        ILogger<AuthFactorService> logger) : base(user)
    {
        _securityLogOptions = securityLogOptions ?? throw new ArgumentNullException(nameof(securityLogOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AuthFactorEntityDto> BindAsync(
        string uid, AuthFactorTypes factorType, string identifier, string? secretHash, bool mustChange,
        AuthFactorSources source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        var normalized = NormalizeIdentifier(factorType, identifier);

        // 幂等预查：同 UId 同 Identifier 已绑 → 返回既有（不写库）
        var existing = await FactorDataService.EntityGetAsync(
            f => f.UId == uid && f.FactorType == (int)factorType && f.Identifier == normalized, ct);
        if (existing != null)
            return AuthFactorEntityDto.FromEntity(existing);

        var now = DateTime.UtcNow;
        try
        {
            var created = await FactorDataService.EntityCreateAsync(new AuthFactorEntity
            {
                UId = uid,
                FactorType = (int)factorType,
                Identifier = normalized,
                SecretHash = secretHash,
                State = 0,
                MustChange = mustChange,
                BoundAt = now,
                CreateTime = now,
                UpdateTime = now,
            }, ct);

            await WriteEventAsync(uid, (int)factorType, normalized, AuthFactorEventTypes.Bind, source, null, ct);
            await WriteSecurityLogAsync(SecurityLogEventTypes.Bind, uid, (int)factorType, normalized, source, null, ct);
            return AuthFactorEntityDto.FromEntity(created);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // 并发唯一约束冲突（方案 §4.7，P1 #8 多方言判定）：重查胜者——同 UId → 幂等返回；
            // 跨 UId（同 Identifier 绑不同账号）→ FACTOR_ALREADY_BOUND
            var winner = await FactorDataService.EntityGetAsync(
                f => f.FactorType == (int)factorType && f.Identifier == normalized, ct);
            if (winner != null)
            {
                if (winner.UId == uid)
                    return AuthFactorEntityDto.FromEntity(winner);
                throw new AuthenticationException(IAuthFactorService.FACTOR_ALREADY_BOUND);
            }
            throw; // 理论不可达：冲突后重查无 = 行被删
        }
    }

    /// <inheritdoc />
    public async Task UnbindAsync(string uid, long factorId, string? operatorName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        // ① 归属校验（方案 §4.7 步骤 1）——不存在或非本 UId 一律 FACTOR_NOT_FOUND（fail-closed 不泄露）
        var factor = await FactorDataService.EntityGetAsync(f => f.Id == factorId, ct);
        if (factor is null || factor.UId != uid)
            throw new AuthenticationException(IAuthFactorService.FACTOR_NOT_FOUND);

        // ② 最后因素守卫（步骤 2）——解绑后 Active 因素数 == 0 → 拒绝
        var active = await FactorDataService.EntitySelectAsync(f => f.UId == uid && f.State == 0, ct: ct);
        if (active.Count <= 1)
            throw new AuthenticationException(IAuthFactorService.LAST_FACTOR_CANNOT_UNBIND);

        // ③ 事件写入 → ④ 物理删除 → ⑤ TokenVersion++（步骤 3~5 同一 UoW——方案 §4.7/§6.2）
        //    事务经 User.GetOptionalService 惰性解析（对齐框架 DomainDataServiceBase.BeginTxScopeAsync）：
        //    InMemory/Testing 宿主未注册 ITransactionManager 时返回 null → 退化为环境 UoW/自动提交，不硬失败。
        var transactionManager = User.GetOptionalService<ITransactionManager>();
        using var scope = transactionManager is null ? (ITransactionScope?)null : await transactionManager.BeginAsync(ct: ct);
        try
        {
            await WriteEventAsync(uid, factor.FactorType, factor.Identifier, AuthFactorEventTypes.Unbind, AuthFactorSources.Manual, operatorName, ct);
            await FactorDataService.EntityDeleteBatchAsync(new[] { factorId }, ct);
            await AccountService.IncrementTokenVersionAsync(uid, ct);
            await WriteSecurityLogAsync(SecurityLogEventTypes.Unbind, uid, factor.FactorType, factor.Identifier, AuthFactorSources.Manual, operatorName, ct);
            if (scope is not null)
                await scope.CommitAsync(ct);
        }
        catch
        {
            if (scope is not null)
                await scope.RollbackAsync(ct);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AuthFactorEntityDto> VerifyAsync(string uid, long factorId, CancellationToken ct = default)
    {
        var factor = await FactorDataService.EntityGetAsync(f => f.Id == factorId, ct);
        if (factor is null || factor.UId != uid)
            throw new AuthenticationException(IAuthFactorService.FACTOR_NOT_FOUND);

        if (factor.VerifiedAt is null)
        {
            factor.State = 0;
            factor.VerifiedAt = DateTime.UtcNow;
            factor.UpdateTime = DateTime.UtcNow;
            factor = await FactorDataService.EntityUpdateAsync(factor, ct);
        }

        await WriteEventAsync(uid, factor.FactorType, factor.Identifier, AuthFactorEventTypes.Verify, AuthFactorSources.Manual, null, ct);
        return AuthFactorEntityDto.FromEntity(factor);
    }

    /// <inheritdoc />
    public async Task<AuthFactorEntityDto> ResetAsync(string uid, long factorId, string? newSecretHash, bool? mustChange = null, CancellationToken ct = default)
    {
        var factor = await FactorDataService.EntityGetAsync(f => f.Id == factorId, ct);
        if (factor is null || factor.UId != uid)
            throw new AuthenticationException(IAuthFactorService.FACTOR_NOT_FOUND);

        factor.SecretHash = newSecretHash;
        if (mustChange.HasValue) factor.MustChange = mustChange.Value;
        factor.SecretUpdatedAt = DateTime.UtcNow;
        factor.UpdateTime = DateTime.UtcNow;
        factor = await FactorDataService.EntityUpdateAsync(factor, ct);

        await WriteEventAsync(uid, factor.FactorType, factor.Identifier, AuthFactorEventTypes.Reset, AuthFactorSources.Manual, null, ct);
        return AuthFactorEntityDto.FromEntity(factor);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuthFactorEntityDto>> ListByUIdAsync(string uid, CancellationToken ct = default)
    {
        var factors = await FactorDataService.EntitySelectAsync(
            f => f.UId == uid, orderBy: q => q.OrderBy(f => f.FactorType).ThenBy(f => f.Id), ct: ct);
        return factors.Select(AuthFactorEntityDto.FromEntity).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyActiveAsync(string uid, CancellationToken ct = default)
    {
        var hit = await FactorDataService.EntityGetAsync(f => f.UId == uid && f.State == 0, ct);
        return hit != null;
    }

    /// <inheritdoc />
    public async Task<string?> GetPrimaryPhoneAsync(string uid, CancellationToken ct = default)
    {
        // 最早 BoundAt 的 Active Sms 因素（方案 §4.8——BoundAt 升序取首行；null 视为最早）
        var phones = await FactorDataService.EntitySelectAsync(
            f => f.UId == uid && f.FactorType == (int)AuthFactorTypes.Sms && f.State == 0,
            orderBy: q => q.OrderBy(f => f.BoundAt), ct: ct);
        return phones.Count == 0 ? null : phones[0].Identifier;
    }

    /// <inheritdoc />
    public async Task<int> GetAuthLevelAsync(string uid, CancellationToken ct = default)
    {
        // 任一 Active Federated → 2，否则 1（方案 §4.8——账号级推导）
        var federated = await FactorDataService.EntityGetAsync(
            f => f.UId == uid && f.FactorType == (int)AuthFactorTypes.Federated && f.State == 0, ct);
        return federated is null ? (int)AuthLevel.Phone : (int)AuthLevel.Federated;
    }

    /// <inheritdoc />
    public async Task<bool> IsBoundToChannelAsync(string uid, string channelPrefix, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelPrefix);
        var prefix = channelPrefix + "|";
        var hit = await FactorDataService.EntityGetAsync(
            f => f.UId == uid && f.FactorType == (int)AuthFactorTypes.Federated && f.State == 0
              && f.Identifier.StartsWith(prefix), ct);
        return hit != null;
    }

    /// <inheritdoc />
    public async Task<AuthFactorEntityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
    {
        // 规范化复用写入面同一单点（方案 §6.1）——两段 trim + 超长/空白守卫语义与 BindAsync 完全一致；
        // 反查不过滤 State（映射存在性语义：Federated 行由 BindAsync 恒写 State=0，Unverified 态不适用于联邦映射）。
        var normalized = NormalizeIdentifier(AuthFactorTypes.Federated, channelId + "|" + externalUserId);
        var entity = await FactorDataService.EntityGetAsync(
            f => f.FactorType == (int)AuthFactorTypes.Federated && f.Identifier == normalized, ct);
        return entity is null ? null : AuthFactorEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<AuthFactorEntityDto?> GetByPhoneAsync(string phone, CancellationToken ct = default)
    {
        // 规范化复用写入面同一单点（方案 §6.1）——与 BindAsync 的 Sms 规范化完全一致；
        // 反查不过滤 State（账号定位语义：Unverified 亦占用该手机号，唯一性由写入面守卫）。
        var normalized = NormalizeIdentifier(AuthFactorTypes.Sms, phone);
        var entity = await FactorDataService.EntityGetAsync(
            f => f.FactorType == (int)AuthFactorTypes.Sms && f.Identifier == normalized, ct);
        return entity is null ? null : AuthFactorEntityDto.FromEntity(entity);
    }

    /// <summary>标识规范化（方案 §6.1 门面单点）——Password=UId；Sms=E.164 去空格/横线；
    /// Email=Trim+ToLowerInvariant；Federated=<c>{channelId}|{externalUserId}</c> 两段 trim；超长拒 <see cref="IAuthFactorService.IDENTIFIER_TOO_LONG"/>。</summary>
    private static string NormalizeIdentifier(AuthFactorTypes factorType, string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        string normalized = factorType switch
        {
            AuthFactorTypes.Password => identifier.Trim(),
            AuthFactorTypes.Sms => identifier.Replace(" ", "").Replace("-", "").Trim(),
            AuthFactorTypes.Email => identifier.Trim().ToLowerInvariant(),
            AuthFactorTypes.Federated => NormalizeFederated(identifier),
            _ => throw new ArgumentOutOfRangeException(nameof(factorType), factorType, "未知因素类型"),
        };

        if (normalized.Length > IdentifierMaxLength)
            throw new AuthenticationException(IAuthFactorService.IDENTIFIER_TOO_LONG);
        return normalized;
    }

    /// <summary>Federated 两段 trim（<c>{channelId}|{externalUserId}</c>；无分隔符则整体 trim）。</summary>
    private static string NormalizeFederated(string identifier)
    {
        var separator = identifier.IndexOf('|');
        if (separator < 0)
            return identifier.Trim();
        return identifier[..separator].Trim() + "|" + identifier[(separator + 1)..].Trim();
    }

    /// <summary>追加因素事件（领域事实源——<c>OccurredAt/ReceivedAt=UtcNow</c>；
    /// OperatorUId/OperatorName 取自帧内 <c>User</c>，<paramref name="operatorNameOverride"/> 非空时优先）。</summary>
    private async Task WriteEventAsync(
        string uid, int factorType, string identifier, AuthFactorEventTypes eventType,
        AuthFactorSources source, string? operatorNameOverride, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await EventDataService.EntityCreateAsync(new AuthFactorEventEntity
        {
            UId = uid,
            FactorType = factorType,
            Identifier = identifier,
            EventType = (int)eventType,
            Source = (int)source,
            OccurredAt = now,
            ReceivedAt = now,
            OperatorUId = CurrentOperatorUId(),
            OperatorName = operatorNameOverride ?? CurrentOperatorName(),
        }, ct);
    }

    /// <summary>SecurityLog 审计副本（领域事实源仍为 AuthFactorEvent；失败降级不阻断绑定业务）。</summary>
    private async Task WriteSecurityLogAsync(
        string eventType, string uid, int factorType, string identifier, AuthFactorSources source,
        string? operatorNameOverride, CancellationToken ct)
    {
        var options = _securityLogOptions.Value;
        if (!options.Enabled) return;
        if (options.EventTypes.Count > 0 && !options.EventTypes.Contains(eventType)) return;

        var detail = $"因素={factorType}；标识={identifier}；来源={(int)source}";
        var operatorName = operatorNameOverride ?? CurrentOperatorName();
        try
        {
            var store = User.Use<ISecurityLogStore>();
            await store.SaveAsync(new SecurityLogEntry(
                EventType: eventType,
                EventCategory: SecurityLogEventTypes.CategoryAuthentication,
                UserName: uid,
                UserId: null,
                IpAddress: null,
                UserAgent: null,
                Result: SecurityLogEventTypes.ResultSuccess,
                Detail: string.IsNullOrEmpty(operatorName) ? detail : $"操作者={operatorName}；{detail}",
                CorrelationId: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "因素事件 SecurityLog 直写失败——降级不阻断（EventType={EventType}, UId={UId}）", eventType, uid);
        }
    }

    /// <summary>帧内操作者平台 id（未认证/系统 → null）。</summary>
    private string? CurrentOperatorUId()
        => User.IsAuthenticated && !string.IsNullOrEmpty(User.UserId) ? User.UserId : null;

    /// <summary>帧内操作者显示名（未认证/系统 → null）。</summary>
    private string? CurrentOperatorName()
        => User.IsAuthenticated && !string.IsNullOrEmpty(User.UserName) ? User.UserName : null;

    /// <summary>
    /// 判定异常链是否含数据库唯一约束冲突（多方言——对齐 <see cref="PlatformAccountMapService"/> 既有模式，
    /// 方案 §4.7 P1 #8）。
    /// </summary>
    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
