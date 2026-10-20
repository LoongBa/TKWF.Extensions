using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 联邦登录共享编排门面实现（V0.10.0 P2-2——自 <see cref="ExternalIdpLoginService"/> 抽取的共享子编排）。
/// <para>编排（P1-1 fallback）：UId 非空 → <see cref="IAuthAccountQueryService.GetByUIdAsync"/> 直读复用；
/// 未命中或 UId 空 → <see cref="IExternalIdentityService.GetByExternalIdAsync"/>(ChannelId, ExternalUserId) 映射复用；
/// 无 → 建号（V0.11.0 T6：<c>FederatedProvisioning.AutoProvision</c> 门控，停写 Phone/AuthLevel）
/// + <see cref="IExternalIdentityService.LinkAsync"/>（因素唯一约束冲突 catch 回滚重查幂等）→
/// 回读账号（IsEnabled/IsFrozen 守卫）→ <see cref="ITokenService.IssueTokenAsync"/>
/// （authType=<c>federated</c> + AuthLevel 经 <see cref="IAuthFactorService.GetAuthLevelAsync"/> 派生 +
/// channel_type=identity.ChannelType——null 不写 claim，P2-3）。</para>
/// <para>DI004 铁律：领域服务间调用经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（禁构造注入——映射/账号/签发
/// 均为守卫工厂门面，帧内解析）。注册：<c>AddConstructibleService&lt;IFederatedLoginOrchestrator, FederatedLoginOrchestrator&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FederatedLoginOrchestrator : DomainServiceBase, IFederatedLoginOrchestrator
{
    private readonly ILogger<FederatedLoginOrchestrator> _logger;

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析）
    private IExternalIdentityService? _map;
    private IExternalIdentityService Map => _map ??= User.Use<IExternalIdentityService>();
    private IAuthAccountService? _accountService;
    private IAuthAccountService AccountService => _accountService ??= User.Use<IAuthAccountService>();
    private IAuthAccountQueryService? _accountQuery;
    private IAuthAccountQueryService AccountQuery => _accountQuery ??= User.Use<IAuthAccountQueryService>();
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();
    private IFreezeStateProvider? _freezeState;
    private IFreezeStateProvider FreezeState => _freezeState ??= User.Use<IFreezeStateProvider>();
    private IAuthFactorService? _factors;
    private IAuthFactorService Factors => _factors ??= User.Use<IAuthFactorService>();
    private AuthAccountEntityDataService? _accountDataService;
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();

    private readonly IOptions<AuthCenterOptions> _options;

    public FederatedLoginOrchestrator(IDomainUser user, ILogger<FederatedLoginOrchestrator> logger,
        IOptions<AuthCenterOptions> options) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(FederatedIdentity identity, CancellationToken ct = default)
    {
        // ① UId 直读（P1-1 优先——事件链签发载荷含 UId 时免二次映射；非空才查询——空 UId 直读必 miss）
        if (!string.IsNullOrEmpty(identity.UId))
        {
            var direct = await AccountQuery.GetByUIdAsync(identity.UId, ct);
            if (direct != null)
            {
                // V0.11.0：冻结判定改经 IFreezeStateProvider（多原因冻结行集合）——与原单槽语义等价
                //（原：IsEnabled && !IsFrozenEffective → 签发 / !IsEnabled → DISABLED / 冻结 → FROZEN）。
                if (!direct.IsEnabled)
                    return new LoginResult(false, null, null, "ACCOUNT_DISABLED");
                if (await FreezeState.IsFrozenAsync(direct.UId, ct: ct))
                    return new LoginResult(false, null, null, "ACCOUNT_FROZEN");
                _logger.LogDebug("联邦登录命中 UId 直读——UId={UId}, ChannelType={ChannelType}", identity.UId, identity.ChannelType);
                return await IssueAndBuildAsync(direct, identity, ct);
            }
            // direct null → fallthrough 到映射（P1-1：UId 直读未命中不阻断建号/映射路径）
        }

        // ② 映射：(channel_id, external_user_id) → uid（UId 空或 UId 直读未命中）
        var mapped = await Map.GetByExternalIdAsync(identity.ChannelId, identity.ExternalUserId, ct);
        string uid;
        if (mapped != null)
        {
            uid = mapped.UId;
            _logger.LogDebug("联邦登录命中既有映射——UId={UId}, ChannelId={ChannelId}", uid, identity.ChannelId);
        }
        else
        {
            if (!_options.Value.FederatedProvisioning.AutoProvision)
                return new LoginResult(false, null, null, "ACCOUNT_NOT_PROVISIONED");

            // ③ 建号 + LinkAsync（V0.11.0 T6：停写 Phone/AuthLevel——手机号与认证强度由 AuthFactor 承载）
            var created = new AuthAccountEntity
            {
                UId = AccountIdGenerator.NewUId(),
                TokenVersion = 0
            };
            await AccountService.CreateAsync(created, ct);
            try
            {
                await Map.LinkAsync(created.UId, identity.ChannelId, identity.ExternalUserId, operatorName: null, ct);
                uid = created.UId;
                _logger.LogDebug("联邦登录建号 + 建立联邦映射——UId={UId}, ChannelId={ChannelId}", uid, identity.ChannelId);
            }
            catch (AuthenticationException ex) when (ex.Message == IAuthFactorService.FACTOR_ALREADY_BOUND)
            {
                // 并发首登——因素唯一约束兜底：回滚空军账号，回读胜者映射（不留不可登录的孤行）
                _logger.LogDebug(ex, "联邦登录建号并发冲突——回滚空军账号并重读映射（AuthFactor 唯一约束兜底）");
                var orphan = await AccountDataService.GetByUIdAsync(created.UId, ct);
                if (orphan != null) await AccountDataService.EntityDeleteBatchAsync(new[] { orphan.Id }, ct);
                var winner = await Map.GetByExternalIdAsync(identity.ChannelId, identity.ExternalUserId, ct);
                if (winner is null) return new LoginResult(false, null, null, "ACCOUNT_CREATE_FAILED");
                uid = winner.UId;
            }
        }

        // ④ 回读账号（IsEnabled/IsFrozen——建号后走查询门面取持久化字段）
        var account = await AccountQuery.GetByUIdAsync(uid, ct);
        if (account is null)
        {
            _logger.LogWarning("联邦登录编排失败——账号查询返回 null（UId={UId}）", uid);
            return new LoginResult(false, null, null, "ACCOUNT_NOT_FOUND");
        }
        if (!account.IsEnabled) return new LoginResult(false, null, null, "ACCOUNT_DISABLED");
        if (await FreezeState.IsFrozenAsync(account.UId, ct: ct)) return new LoginResult(false, null, null, "ACCOUNT_FROZEN");   // V0.11.0 冻结判定（多原因冻结行集合）

        return await IssueAndBuildAsync(account, identity, ct);
    }

    /// <summary>⑤ 签 token1（authType=federated + channel_type=identity.ChannelType——null 不写 claim，P2-3）
    /// → <see cref="LoginResult"/>（UserId 取回读实体的持久化 UId——对齐既有编排语义；
    /// P2-1 审核收敛：单参数 account——TokenIssueRequest 与 LoginResult 统一用 account.UId，消除冗余 uid 参数）。</summary>
    private async Task<LoginResult> IssueAndBuildAsync(AuthAccountEntity account, FederatedIdentity identity, CancellationToken ct)
    {
        var authLevel = await Factors.GetAuthLevelAsync(account.UId, ct);
        var token = await TokenService.IssueTokenAsync(new TokenIssueRequest(
            account.UId, AuthTypes.Federated, authLevel, ChannelType: identity.ChannelType), ct);
        _logger.LogDebug("联邦登录成功——UId={UId}, ChannelType={ChannelType}", account.UId, identity.ChannelType);
        return new LoginResult(true, account.UId, token, null);
    }
}