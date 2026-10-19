using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 联邦登录共享编排门面实现（V0.10.0 P2-2——自 <see cref="ExternalIdpLoginService"/> 抽取的共享子编排）。
/// <para>编排（P1-1 fallback）：UId 非空 → <see cref="IAuthAccountQueryService.GetByUIdAsync"/> 直读复用；
/// 未命中或 UId 空 → <see cref="ISsoChannelMapService.GetByChannelAsync"/>(ChannelId, ExternalUserId) 映射复用；
/// 无 → 建号（<see cref="AuthLevel.Federated"/>，Phone 可空）+ <see cref="ISsoChannelMapService.LinkAsync"/>
/// （UX 冲突 catch 重查幂等）→ 回读账号（IsEnabled/IsFrozen 守卫）→
/// <see cref="ITokenService.IssueTokenAsync"/>（authType=<c>federated</c> + channel_type=identity.ChannelType——
/// null 不写 claim，P2-3）。</para>
/// <para>DI004 铁律：领域服务间调用经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（禁构造注入——映射/账号/签发
/// 均为守卫工厂门面，帧内解析）。注册：<c>AddConstructibleService&lt;IFederatedLoginOrchestrator, FederatedLoginOrchestrator&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FederatedLoginOrchestrator : DomainServiceBase, IFederatedLoginOrchestrator
{
    private readonly ILogger<FederatedLoginOrchestrator> _logger;

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析）
    private ISsoChannelMapService? _map;
    private ISsoChannelMapService Map => _map ??= User.Use<ISsoChannelMapService>();
    private IAuthAccountService? _accountService;
    private IAuthAccountService AccountService => _accountService ??= User.Use<IAuthAccountService>();
    private IAuthAccountQueryService? _accountQuery;
    private IAuthAccountQueryService AccountQuery => _accountQuery ??= User.Use<IAuthAccountQueryService>();
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();

    public FederatedLoginOrchestrator(IDomainUser user, ILogger<FederatedLoginOrchestrator> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(FederatedIdentity identity, CancellationToken ct = default)
    {
        // ① UId 直读（P1-1 优先——事件链签发载荷含 UId 时免二次映射；非空才查询——空 UId 直读必 miss）
        if (!string.IsNullOrEmpty(identity.UId))
        {
            var direct = await AccountQuery.GetByUIdAsync(identity.UId, ct);
            if (direct != null && direct.IsEnabled && !direct.IsFrozenEffective)
            {
                _logger.LogDebug("联邦登录命中 UId 直读——UId={UId}, ChannelType={ChannelType}", identity.UId, identity.ChannelType);
                return await IssueAndBuildAsync(direct, identity, ct);
            }
            if (direct != null && !direct.IsEnabled)
                return new LoginResult(false, null, null, "ACCOUNT_DISABLED");
            if (direct != null && direct.IsFrozenEffective)
                return new LoginResult(false, null, null, "ACCOUNT_FROZEN");
            // direct null → fallthrough 到映射（P1-1：UId 直读未命中不阻断建号/映射路径）
        }

        // ② 映射：(channel_id, external_user_id) → uid（UId 空或 UId 直读未命中）
        var mapped = await Map.GetByChannelAsync(identity.ChannelId, identity.ExternalUserId, ct);
        string uid;
        if (mapped != null)
        {
            uid = mapped.UId;
            _logger.LogDebug("联邦登录命中既有映射——UId={UId}, ChannelId={ChannelId}", uid, identity.ChannelId);
        }
        else
        {
            // ③ 建号 + LinkAsync（Phone=null 联邦便捷账号——唯一索引对 NULL 放行）
            var created = new AuthAccountEntity
            {
                UId = AccountIdGenerator.NewUId(),
                Phone = null,
                AuthLevel = (int)AuthLevel.Federated,
                TokenVersion = 0
            };
            await AccountService.CreateAsync(created, ct);
            await Map.LinkAsync(created.UId, identity.ChannelId, identity.ExternalUserId, ct);
            uid = created.UId;
            _logger.LogDebug("联邦登录建号 + 建立联邦映射——UId={UId}, ChannelId={ChannelId}", uid, identity.ChannelId);
        }

        // ④ 回读账号（AuthLevel/IsEnabled/IsFrozen——建号后走查询门面取持久化字段）
        var account = await AccountQuery.GetByUIdAsync(uid, ct);
        if (account is null)
        {
            _logger.LogWarning("联邦登录编排失败——账号查询返回 null（UId={UId}）", uid);
            return new LoginResult(false, null, null, "ACCOUNT_NOT_FOUND");
        }
        if (!account.IsEnabled) return new LoginResult(false, null, null, "ACCOUNT_DISABLED");
        if (account.IsFrozenEffective) return new LoginResult(false, null, null, "ACCOUNT_FROZEN");   // V0.9.0 冻结检查（ADR 决策 3）

        return await IssueAndBuildAsync(account, identity, ct);
    }

    /// <summary>⑤ 签 token1（authType=federated + channel_type=identity.ChannelType——null 不写 claim，P2-3）
    /// → <see cref="LoginResult"/>（UserId 取回读实体的持久化 UId——对齐既有编排语义；
    /// P2-1 审核收敛：单参数 account——TokenIssueRequest 与 LoginResult 统一用 account.UId，消除冗余 uid 参数）。</summary>
    private async Task<LoginResult> IssueAndBuildAsync(AuthAccountEntity account, FederatedIdentity identity, CancellationToken ct)
    {
        var token = await TokenService.IssueTokenAsync(new TokenIssueRequest(
            account.UId, AuthTypes.Federated, account.AuthLevel, ChannelType: identity.ChannelType), ct);
        _logger.LogDebug("联邦登录成功——UId={UId}, ChannelType={ChannelType}", account.UId, identity.ChannelType);
        return new LoginResult(true, account.UId, token, null);
    }
}