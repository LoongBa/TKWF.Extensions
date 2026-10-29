using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 弱身份清理门面实现（D03 1.4 §6.3 O2——能力归 AuthCenter，策略归业务；<c>inactiveDays</c> 由调用方传入）。
/// <para><b>弱身份判据（本迭代实用形态）</b>——AuthAccount 无独立"联邦专用"标记（FederatedProvisioning 只建普通账号），
/// 故采用全账号表 best-effort 扫描 + 组合探测：① 无 Password(1)/Sms(2) 凭据因素（有密码/短信即强身份，排除）；
/// ② 无 UserProfile 档案行；③ 未冻结（<see cref="IFreezeStateProvider.IsFrozenAsync"/>）；④ 自 <c>CreateTime</c> 起
/// 无交互满 <c>inactiveDays</c>；⑤ <c>IsEnabled=false</c>（软删恢复窗口行）仍列入，<see cref="WeakIdentityItem.IsDisabled"/> 标注。</para>
/// <para><b>成本说明（有意为之，勿加缓存）</b>：判据组合无数据库索引支撑，扫描为逐账号 N+1 探测——
/// 面向运营的清理/查询场景 + 显式条件，可接受；调用方应以合理 <c>InactiveDays</c> 收缩语义面。</para>
/// <para>DI004 铁律：领域服务经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（禁构造注入）；注册
/// <c>AddConstructibleService&lt;IWeakIdentityCleanupService, WeakIdentityCleanupService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeakIdentityCleanupService : DomainServiceBase, IWeakIdentityCleanupService
{
    private readonly ILogger<WeakIdentityCleanupService> _logger;

    private AuthAccountEntityDataService? _accountDataService;
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();
    private UserProfileEntityDataService? _profileDataService;
    private UserProfileEntityDataService ProfileDataService => _profileDataService ??= User.Use<UserProfileEntityDataService>();
    private AuthFactorEntityDataService? _factorDataService;
    private AuthFactorEntityDataService FactorDataService => _factorDataService ??= User.Use<AuthFactorEntityDataService>();
    private AuthAccountFreezeEntityDataService? _freezeDataService;
    private AuthAccountFreezeEntityDataService FreezeDataService => _freezeDataService ??= User.Use<AuthAccountFreezeEntityDataService>();
    private IAuthFactorService? _factors;
    private IAuthFactorService Factors => _factors ??= User.Use<IAuthFactorService>();
    private IFreezeStateProvider? _freezeState;
    private IFreezeStateProvider FreezeState => _freezeState ??= User.Use<IFreezeStateProvider>();

    public WeakIdentityCleanupService(IDomainUser user, ILogger<WeakIdentityCleanupService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WeakIdentityPage> QueryWeakIdentitiesAsync(WeakIdentityCriteria criteria, CancellationToken ct = default)
    {
        if (criteria.InactiveDays < 0)
            throw new DomainException("INVALID_ARGUMENT") { ErrorCode = "INVALID_ARGUMENT" };

        var pageIndex = criteria.PageIndex < 1 ? 1 : criteria.PageIndex;
        var pageSize = Math.Clamp(criteria.PageSize, 1, MaxPageSize);
        var cutoff = DateTime.UtcNow.AddDays(-criteria.InactiveDays);

        // 全表扫描（判据组合无索引，见类注释成本说明）——CreateTime 升序 + UId 序 tiebreak 保证确定性
        var weak = new List<WeakIdentityItem>();
        var skip = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await AccountDataService.EntitySelectAsync(
                predicate: null, skip: skip, limit: ScanBatchSize,
                orderBy: q => q.OrderBy(a => a.CreateTime).ThenBy(a => a.UId), ct: ct);
            foreach (var account in page)
            {
                if (await IsWeakIdentityAsync(account, cutoff, ct))
                    weak.Add(new WeakIdentityItem(account.UId, account.CreateTime, IsDisabled: !account.IsEnabled));
            }
            if (page.Count < ScanBatchSize) break;
            skip += ScanBatchSize;
        }

        var total = weak.Count;
        var items = weak.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return new WeakIdentityPage(items, total, HasMore: pageIndex * pageSize < total);
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(IReadOnlyList<string> platformUids, PurgeMode mode, CancellationToken ct = default)
    {
        // 幂等：空列表直接返回 0
        if (platformUids is null || platformUids.Count == 0) return 0;
        // 非法模式显式拒绝（fail-safe——避免未知枚举值落到物理删除分支）
        if (mode != PurgeMode.SoftDelete && mode != PurgeMode.PhysicalDelete)
            throw new DomainException("INVALID_ARGUMENT") { ErrorCode = "INVALID_ARGUMENT" };

        var processed = 0;
        foreach (var uid in platformUids)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(uid)) continue;
            try
            {
                // 强身份守卫上提：两种模式共用（非 Federated 因素 / 零因素账号一律不清）
                if (await IsStrongIdentityAsync(uid, ct))
                {
                    _logger.LogWarning("弱身份清理跳过——强身份或非联邦账号（UId={UId}, Mode={Mode}）", uid, mode);
                    continue;
                }
                // 逐账号 best-effort（运维面）：单个失败记日志继续，不阻断整批
                switch (mode)
                {
                    case PurgeMode.SoftDelete when await SoftDeleteAsync(uid, ct):
                        processed++;
                        break;
                    case PurgeMode.PhysicalDelete when await PhysicalDeleteAsync(uid, ct):
                        processed++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "弱身份清理失败（best-effort 继续）——UId={UId}, Mode={Mode}", uid, mode);
            }
        }
        return processed;
    }

    /// <summary>强身份守卫：零因素（无联邦建号证据，含消费方影子管理员）或含非 Federated 因素 → 不清。</summary>
    private async Task<bool> IsStrongIdentityAsync(string uid, CancellationToken ct)
    {
        var factors = await Factors.ListByUIdAsync(uid, ct);
        if (factors.Count == 0) return true;
        foreach (var factor in factors)
        {
            if (factor.FactorType != (int)AuthFactorTypes.Federated) return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<WeakIdentityStats> GetWeakStatsAsync(CancellationToken ct = default)
    {
        // 基线统计：只按身份形态（无凭据因素/无档案/未冻结）判弱，不计 InactiveDays——
        // inactiveDays 是调用方侧策略（运营配置面），统计卡片展示身份形态基线
        var accountTotal = 0;
        var weakCount = 0;
        var skip = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await AccountDataService.EntitySelectAsync(
                predicate: null, skip: skip, limit: ScanBatchSize,
                orderBy: q => q.OrderBy(a => a.CreateTime), ct: ct);
            foreach (var account in page)
            {
                accountTotal++;
                if (await IsWeakIdentityAsync(account, cutoff: null, ct)) weakCount++;
            }
            if (page.Count < ScanBatchSize) break;
            skip += ScanBatchSize;
        }

        var ratio = accountTotal == 0 ? 0d : (double)weakCount / accountTotal;
        return new WeakIdentityStats(weakCount, accountTotal, ratio);
    }

    /// <summary>单账号弱身份判定（判据见类注释；<paramref name="cutoff"/> null = 跳过无交互天数检查——统计基线形态用）。</summary>
    private async Task<bool> IsWeakIdentityAsync(AuthAccountEntity account, DateTime? cutoff, CancellationToken ct)
    {
        // ① 身份形态：必须有至少一个因素，且全部为 Federated（allowlist——联邦路径建号只产 Federated 因素）
        //    零因素账号不判弱（无联邦建号证据，含消费方影子管理员）；任何非 Federated 因素（Password/Sms/Email/Totp/Passkey）= 强身份
        var factors = await Factors.ListByUIdAsync(account.UId, ct);
        if (factors.Count == 0) return false;
        foreach (var factor in factors)
        {
            if (factor.FactorType != (int)AuthFactorTypes.Federated)
                return false;
        }

        // ② 无档案
        var profile = await ProfileDataService.GetByUIdAsync(account.UId, ct);
        if (profile is not null) return false;

        // ③ 未冻结
        if (await FreezeState.IsFrozenAsync(account.UId, ct: ct)) return false;

        // ④ 无交互天数阈值：CreateTime 不早于截止 → 排除（null 略过——统计基线形态；IsEnabled=false 行仍列入）
        return cutoff is null || account.CreateTime <= cutoff.Value;
    }

    /// <summary>软删单账号——已禁用跳过（不计入本次处理数）；原子 CAS：仅当仍启用时翻转为禁用。</summary>
    private async Task<bool> SoftDeleteAsync(string uid, CancellationToken ct)
    {
        var account = await AccountDataService.GetByUIdAsync(uid, ct);
        if (account is null)
        {
            _logger.LogWarning("弱身份软删跳过——账号不存在（UId={UId}）", uid);
            return false;
        }
        if (!account.IsEnabled)
        {
            _logger.LogDebug("弱身份软删跳过——已处于禁用态（UId={UId}）", uid);
            return false;
        }

        // CAS 条件更新：WHERE UId=@uid AND IsEnabled=true（并发兜底——同 UoW 原子，影响行数=真实翻转数）
        var affected = await AccountDataService.EntityUpdateWhereAsync(
            a => a.UId == uid && a.IsEnabled,
            a => new AuthAccountEntity { IsEnabled = false, UpdateTime = DateTime.UtcNow },
            ct);
        if (affected > 0) return true;

        _logger.LogDebug("弱身份软删跳过——并发下已非启用态（UId={UId}）", uid);
        return false;
    }

    /// <summary>
    /// 物理删单账号（GDPR 删除权）——级联：因素行 → 档案行 → 冻结行 → 账号行。
    /// <para><b>强身份守卫（fail-safe）</b>：见 <see cref="PurgeAsync"/> 入口统一守卫（零因素/非 Federated 因素跳过）；
    /// 本方法不再重复判定，记日志不入本次处理数；不抛异常（运维面 per-account 语义）。</para>
    /// <para><b>已知边界（P2-6，转达登记）</b>：<c>AuthFactorEvent</c>（append-only 领域事实源，含 Identifier 快照）、
    /// <c>AuthLoginAttempt</c>/<c>SmsRecord</c>（IP/手机号）、<c>AuthRefreshToken</c>/<c>AuthTokenBlacklist</c>/
    /// <c>OAuthTicket</c>/<c>AuthGrant</c>（UserId 关联）不随本能力删除——保留审计与安全轨迹，其保留期由各域
    /// 自有清理任务负责（完整 GDPR 级联待后续迭代 + 事务包裹）。</para>
    /// </summary>
    private async Task<bool> PhysicalDeleteAsync(string uid, CancellationToken ct)
    {
        var account = await AccountDataService.GetByUIdAsync(uid, ct);
        if (account is null)
        {
            _logger.LogWarning("弱身份物理删跳过——账号不存在（UId={UId}）", uid);
            return false;
        }

        // ① 因素行（联邦因素随账号删除——无独立生命周期）
        var factorRows = await FactorDataService.EntitySelectAsync(f => f.UId == uid, ct: ct);
        if (factorRows.Count > 0)
            await FactorDataService.EntityDeleteBatchAsync(factorRows.Select(f => f.Id), ct);

        // ② 档案行（1:1——随账号删除）
        var profileRows = await ProfileDataService.EntitySelectAsync(p => p.UId == uid, ct: ct);
        if (profileRows.Count > 0)
            await ProfileDataService.EntityDeleteBatchAsync(profileRows.Select(p => p.Id), ct);

        // ③ 冻结行（随账号删除——审计副本 SecurityLog 保留，行级永久记录语义容忍）
        var freezeRows = await FreezeDataService.EntitySelectAsync(f => f.UId == uid, ct: ct);
        if (freezeRows.Count > 0)
            await FreezeDataService.EntityDeleteBatchAsync(freezeRows.Select(f => f.Id), ct);

        // ④ 账号行
        await AccountDataService.EntityDeleteBatchAsync(new[] { account.Id }, ct);
        _logger.LogInformation("弱身份物理删完成（GDPR）——UId={UId}", uid);
        return true;
    }

    /// <summary>单批扫描行数（判据组合无索引——分页扫描粒度，勿调小到页大小以下）。</summary>
    private const int ScanBatchSize = 200;

    /// <summary>分页上限（契约 DTO 约束 1..200——越界钳制不抛）。</summary>
    private const int MaxPageSize = 200;
}