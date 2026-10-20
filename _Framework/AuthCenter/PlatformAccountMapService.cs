using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 跨系统映射服务——平台内部 id ↔ 业务 app + 业务本地 id（统一 DMP 双机制，方案 §5.9）。
/// <para>DMP V4.0 管理员走映射表、会员走外键——Ext 以映射表统一承载；DMP 迁移时会员映射迁入本表（需求 §6 约束 1）。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部经 <see cref="PlatformAccountMapEntityDataService"/>
/// 内部转发访问器（Entity*，同程序集）委托查询/写入。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IPlatformAccountMapService, PlatformAccountMapService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para>V0.11.0（方案 §4.1 T1 / §4.10 D6 / T4，Iteration 1 Wave 1c）：联邦身份唯一权威 =
/// <see cref="AuthFactorEntity"/> 的 Federated 因素行（Identifier = <c>{channelId}|{externalUserId}</c>）——
/// 本类实现 <see cref="IExternalIdentityService"/> 四方法全部委托 <see cref="IAuthFactorService"/>；
/// <c>ISsoChannelMapService</c>/<c>ISsoAccountLinkService</c> 与本类 UnionId 映射同批退役
/// （映射表回归纯业务系统映射，联邦映射列删）。</para>
/// </summary>
[DiContractIgnore]
public sealed class PlatformAccountMapService : DomainServiceBase, IPlatformAccountMapService, IExternalIdentityService
{
    private PlatformAccountMapEntityDataService? _dataService;
    private readonly ILogger<PlatformAccountMapService> _logger;

    private PlatformAccountMapEntityDataService DataService => _dataService ??= User.Use<PlatformAccountMapEntityDataService>();

    private IAuthFactorService? _factorService;
    private IAuthFactorService Factors => _factorService ??= User.Use<IAuthFactorService>();

    public PlatformAccountMapService(IDomainUser user, ILogger<PlatformAccountMapService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<PlatformAccountMapEntity?> GetByPlatformAsync(string platformAccountId, string businessAppId, CancellationToken ct = default)
        => DataService.EntityGetAsync(m => m.PlatformAccountId == platformAccountId && m.BusinessAppId == businessAppId, ct);

    /// <inheritdoc />
    public Task<PlatformAccountMapEntity?> GetByBusinessAsync(string businessAppId, string businessLocalId, CancellationToken ct = default)
        => DataService.EntityGetAsync(m => m.BusinessAppId == businessAppId && m.BusinessLocalId == businessLocalId, ct);

    /// <inheritdoc />
    public async Task<PlatformAccountMapEntity> LinkAsync(
        string platformAccountId, string businessAppId, string businessLocalId, CancellationToken ct = default)
    {
        // upsert：同键（唯一索引 TKWFIX_PlatformAccountMap）存在 → 幂等返回（不写库）；不存在 → 新建。
        var existing = await DataService.EntityGetAsync(
            m => m.PlatformAccountId == platformAccountId
                && m.BusinessAppId == businessAppId
                && m.BusinessLocalId == businessLocalId, ct);

        if (existing != null)
            return existing;

        var created = new PlatformAccountMapEntity
        {
            PlatformAccountId = platformAccountId,
            BusinessAppId = businessAppId,
            BusinessLocalId = businessLocalId,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        };
        return await DataService.EntityCreateAsync(created, ct);
    }

    // ── 外部身份统一契约（IExternalIdentityService，V0.11.0 方案 §4.10 D6/T4）──
    //    权威 = AuthFactorEntity 的 Federated 因素行（Identifier = {channelId}|{externalUserId}）。
    //    四方法全部委托 IAuthFactorService——唯一约束兜底/最后因素守卫/事件留痕/标识规范化收敛在该门面单点，
    //    本类不做任何数据面读写（避免双写源）。
    //    ⚠️ 本类有两个 LinkAsync 重载（业务 app 映射三参 / 联邦身份映射四参）——新增调用点须按重载语义显式取参，
    //    或经接口 User.Use<IExternalIdentityService>() 解析以免歧义。

    /// <inheritdoc />
    public async Task<ExternalIdentityDto> LinkAsync(
        string uid, string channelId, string externalUserId, string? operatorName, CancellationToken ct)
    {
        // Source=Business：本门面服务业务事件链路（渠道关注关系/联邦登录编排等业务决策方驱动）；
        // 建号期自动绑定归 Provisioning（由建号路径自行表达）。
        // operatorName 不透传 BindAsync（其无 operator 形参——Bind 事件 OperatorUId/OperatorName 取自帧内 User）。
        var factor = await Factors.BindAsync(
            uid, AuthFactorTypes.Federated, ComposeIdentifier(channelId, externalUserId),
            source: AuthFactorSources.Business, ct: ct);

        return ProjectFactor(factor, channelId, externalUserId);
    }

    // 不存在 / 非本账号 → 幂等返回（契约语义 + fail-closed 不泄露他人绑定：归属不符不区分"不存在"与"属他人"）；
    // 最后因素守卫（LAST_FACTOR_CANNOT_UNBIND）不吞——解绑联邦映射若致账号零可登录凭据须显式失败
    // （fail-closed：宁可调用方先补凭据，也不静默留下无凭据账号）。
    /// <inheritdoc />
    public async Task UnlinkAsync(
        string uid, string channelId, string externalUserId, string? operatorName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        var factor = await Factors.GetByExternalIdAsync(channelId, externalUserId, ct);
        if (factor is null || !string.Equals(factor.UId, uid, StringComparison.Ordinal))
            return;

        await Factors.UnbindAsync(uid, factor.Id, operatorName, ct);
    }

    /// <inheritdoc />
    public async Task<ExternalIdentityDto?> GetByExternalIdAsync(
        string channelId, string externalUserId, CancellationToken ct)
    {
        var factor = await Factors.GetByExternalIdAsync(channelId, externalUserId, ct);
        return factor is null ? null : ProjectFactor(factor, channelId, externalUserId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        var factors = await Factors.ListByUIdAsync(uid, ct);
        var result = new List<ExternalIdentityDto>(factors.Count);
        foreach (var factor in factors)
        {
            if (factor.FactorType != (int)AuthFactorTypes.Federated)
                continue;

            // 投影解析：Identifier = {channelId}|{externalUserId}——畸形行（无分隔符/任一段为空）不投影，
            // 非联邦映射语义（NormalizeFederated 对无分隔符输入保留整串，此处按"无 channel 段"跳过）
            var separator = factor.Identifier.IndexOf('|');
            if (separator <= 0 || separator == factor.Identifier.Length - 1)
                continue;

            result.Add(new ExternalIdentityDto(
                factor.UId,
                factor.Identifier[..separator],
                factor.Identifier[(separator + 1)..],
                factor.BoundAt ?? factor.CreateTime));
        }
        return result;
    }

    /// <summary>联邦标识组装（<c>{channelId}|{externalUserId}</c>——两段 trim；规范化/超长守卫归
    /// <see cref="IAuthFactorService"/> 单点，本类不做二次加工）。</summary>
    private static string ComposeIdentifier(string channelId, string externalUserId)
        => channelId.Trim() + "|" + externalUserId.Trim();

    /// <summary>因素行 → 外部身份 DTO（ChannelId/ExternalUserId 用调用方原值：与因素行 Identifier 的两段同源，
    /// 避免往返 trim 语义漂移；LinkedAt 取 BoundAt，缺失回退 CreateTime——BoundAt 可空列）。</summary>
    private static ExternalIdentityDto ProjectFactor(AuthFactorEntityDto factor, string channelId, string externalUserId)
        => new(factor.UId, channelId.Trim(), externalUserId.Trim(), factor.BoundAt ?? factor.CreateTime);
}
