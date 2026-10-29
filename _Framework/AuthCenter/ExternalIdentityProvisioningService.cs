using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部身份 L2 预置 + 档案合并门面实现（D03 1.4 §6.1/§6.2——事件路径专用）。
/// <para><see cref="ProvisionAsync"/> 与登录路径 <see cref="FederatedLoginOrchestrator"/> 共享映射/建号内核，
/// 不新造机制：payload.PlatformUid 非空直读复用 → <see cref="IExternalIdentityService.GetByExternalIdAsync"/>
/// (ChannelId, ExternalUserId) 映射复用 → AutoProvision 门控建号 + <see cref="IExternalIdentityService.LinkAsync"/>
/// （AuthFactor 唯一约束冲突 catch → 回滚空军账号 → 回读胜者——并发首登幂等）。</para>
/// <para><see cref="MergeProfileAsync"/> 按 D03 §5.2 优先级策略合并写入 <see cref="UserProfileEntity"/>——
/// 字段级只写实体既有列（Nickname/Avatar/Gender；City/Province/Country/Language/SubscribeStatus/SubscribeTime/Phone
/// 实体无列，本迭代忽略），绝不覆盖 user_filled / "微信用户"默认值。</para>
/// <para>DI004 铁律：领域服务调用经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（守卫工厂帧内解析）。
/// 注册：<c>AddConstructibleService&lt;IExternalIdentityProvisioningService, ExternalIdentityProvisioningService&gt;()</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。无端点（不加 <c>[GenerateController]</c>）。</para>
/// </summary>
[DiContractIgnore]
public sealed class ExternalIdentityProvisioningService : DomainServiceBase, IExternalIdentityProvisioningService
{
    private readonly ILogger<ExternalIdentityProvisioningService> _logger;

    /// <summary>微信未设置昵称时的默认值（D03 §5.2——默认值不得覆盖真实值）。</summary>
    private const string DefaultNickname = "微信用户";

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析）
    private IExternalIdentityService? _map;
    private IExternalIdentityService Map => _map ??= User.Use<IExternalIdentityService>();
    private IAuthAccountService? _accountService;
    private IAuthAccountService AccountService => _accountService ??= User.Use<IAuthAccountService>();
    private IAuthAccountQueryService? _accountQuery;
    private IAuthAccountQueryService AccountQuery => _accountQuery ??= User.Use<IAuthAccountQueryService>();
    private AuthAccountEntityDataService? _accountDataService;
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();
    private UserProfileEntityDataService? _profileDataService;
    private UserProfileEntityDataService ProfileDataService => _profileDataService ??= User.Use<UserProfileEntityDataService>();

    private readonly IOptions<AuthCenterOptions> _options;

    public ExternalIdentityProvisioningService(IDomainUser user, ILogger<ExternalIdentityProvisioningService> logger,
        IOptions<AuthCenterOptions> options) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<ProvisionedIdentity> ProvisionAsync(FederatedTicketPayload payload, CancellationToken ct = default)
    {
        // ① UId 直读（A11 事件预置——载荷已含平台身份时免二次映射；非空才查询——空 UId 直读必 miss）
        if (!string.IsNullOrEmpty(payload.PlatformUid))
        {
            var direct = await AccountQuery.GetByUIdAsync(payload.PlatformUid, ct);
            if (direct != null)
            {
                _logger.LogDebug("外部身份预置命中 UId 直读——UId={UId}, ChannelId={ChannelId}", payload.PlatformUid, payload.ChannelId);
                return new ProvisionedIdentity(payload.PlatformUid, IsNew: false);
            }
            // direct null → fallthrough 到映射（同登录编排：直读未命中不阻断映射/建号路径）
        }

        // ② 映射：(ChannelId, ExternalUserId) → uid（与非空但未命中的 UId 同路径复用）
        var mapped = await Map.GetByExternalIdAsync(payload.ChannelId, payload.ExternalUserId, ct);
        if (mapped != null)
        {
            // 回读账号存在性（映射行可能因账号被物理清理而残留 → 视同无映射走建号，不返回幽灵 UId）
            var mappedAccount = await AccountQuery.GetByUIdAsync(mapped.UId, ct);
            if (mappedAccount != null)
            {
                _logger.LogDebug("外部身份预置命中既有映射——UId={UId}, ChannelId={ChannelId}", mapped.UId, payload.ChannelId);
                return new ProvisionedIdentity(mapped.UId, IsNew: false);
            }
            _logger.LogWarning("外部身份预置映射命中但账号不存在——按无映射处理（UId={UId}, ChannelId={ChannelId}）",
                mapped.UId, payload.ChannelId);
        }

        // ③ AutoProvision 门控（领域决策，同登录编排——false 拒建号不绑定）
        if (!_options.Value.FederatedProvisioning.AutoProvision)
            throw new DomainException("ACCOUNT_NOT_PROVISIONED") { ErrorCode = "ACCOUNT_NOT_PROVISIONED" };

        // ④ 建号 + LinkAsync（同登录编排内核，停写 Phone/AuthLevel——认证强度由 AuthFactor 承载）
        var created = new AuthAccountEntity
        {
            UId = AccountIdGenerator.NewUId(),
            TokenVersion = 0
        };
        await AccountService.CreateAsync(created, ct);
        string uid;
        try
        {
            await Map.LinkAsync(created.UId, payload.ChannelId, payload.ExternalUserId, operatorName: null, ct);
            uid = created.UId;
            _logger.LogDebug("外部身份预置建号 + 建立联邦映射——UId={UId}, ChannelId={ChannelId}", uid, payload.ChannelId);
            return new ProvisionedIdentity(uid, IsNew: true);
        }
        catch (AuthenticationException ex) when (ex.Message == IAuthFactorService.FACTOR_ALREADY_BOUND)
        {
            // 并发首登——因素唯一约束兜底：回滚空军账号，回读胜者映射（不留不可登录的孤行）
            _logger.LogDebug(ex, "外部身份预置建号并发冲突——回滚空军账号并重读映射（AuthFactor 唯一约束兜底）");
            var orphan = await AccountDataService.GetByUIdAsync(created.UId, ct);
            if (orphan != null) await AccountDataService.EntityDeleteBatchAsync(new[] { orphan.Id }, ct);
            var winner = await Map.GetByExternalIdAsync(payload.ChannelId, payload.ExternalUserId, ct);
            if (winner is null) throw new DomainException("ACCOUNT_CREATE_FAILED") { ErrorCode = "ACCOUNT_CREATE_FAILED" };
            return new ProvisionedIdentity(winner.UId, IsNew: false);
        }
    }

    /// <inheritdoc />
    /// <remarks>实体无列字段（City/Province/Country/Language/SubscribeStatus/SubscribeTime/Phone）本迭代忽略不写。</remarks>
    public async Task MergeProfileAsync(string platformUid, ExternalProfile profile, string source, CancellationToken ct = default)
    {
        // ① 载入既有档案（1:1；null = 未建档直接创建）
        var entity = await AccountQuery.GetProfileByUIdAsync(platformUid, ct);
        bool isNew = entity is null;
        entity ??= new UserProfileEntity { UId = platformUid };

        // ② 逐字段合并（仅实体既有列：Nickname/Avatar/Gender）
        bool wrote = false;
        var existingSource = entity.ProfileSource;
        wrote |= MergeField(entity.Nickname, profile.Nickname, source, existingSource, v => entity.Nickname = v, DefaultNickname);
        wrote |= MergeField(entity.Avatar, profile.AvatarUrl, source, existingSource, v => entity.Avatar = v, null);
        wrote |= MergeField(entity.Gender, profile.Gender, source, existingSource, v => entity.Gender = v, null);

        // ③ 来源戳：永不降级（低优先级源写其它字段不得削弱 user_filled 保护）；
        //    更高/同优先级源即使本次无字段变更也认领戳（防后续低优先级同源写入绕过保护）
        var stampUpgrade = PriorityOf(source) >= PriorityOf(existingSource);
        if (!wrote && !(stampUpgrade && HasAnyProfileValue(profile))) return;
        if (stampUpgrade) entity.ProfileSource = source;
        entity.LastProfileSourceAt = DateTime.UtcNow;
        if (isNew)
        {
            await ProfileDataService.CreateOrUpdateAsync(entity, ct); // 创建路径全量持久化（含来源戳）
        }
        else
        {
            entity.UpdateTime = DateTime.UtcNow;
            // 更新路径：CreateOrUpdateAsync 仅复制档案 5 列不复制 ProfileSource 列——同程序集原子全字段更新保来源戳
            await ProfileDataService.EntityUpdateAsync(entity, ct);
        }
    }

    /// <summary>本次负载是否含可合并档案值（Nickname/AvatarUrl/Gender 任一非空）。</summary>
    private static bool HasAnyProfileValue(ExternalProfile profile)
        => !string.IsNullOrWhiteSpace(profile.Nickname)
            || !string.IsNullOrWhiteSpace(profile.AvatarUrl)
            || !string.IsNullOrWhiteSpace(profile.Gender);

    /// <summary>单字段合并（D03 §5.2：补空/升级；同源刷新以新为准；绝不覆盖 user_filled；默认昵称不覆盖真实值）。
    /// 返回是否实际写入。</summary>
    private bool MergeField(string? existing, string? newValue, string source, string? existingSource,
        Action<string> write, string? defaultMarker)
    {
        if (newValue is null) return false;
        // 默认值不反向污染真实值（"微信用户" 既不能覆盖真实昵称，也不能被当真实值保留）
        if (defaultMarker != null && newValue == defaultMarker && existing != defaultMarker) return false;
        if (existing is null)
        {
            write(newValue);
            return true;
        }
        if (existing == newValue)
            return false;
        if (existingSource == source) // 同源刷新以新为准（含 user_filled 自身刷新——用户改昵称）
        {
            write(newValue);
            return true;
        }
        if (existingSource == ProfileSource.UserFilled)
            return false; // user_filled 绝对保护——其余源不覆盖
        if (PriorityOf(source) > PriorityOf(existingSource) && defaultMarker != null && existing == defaultMarker)
        {
            write(newValue); // 低→高升级：既有为默认昵称（"微信用户"）才可覆盖
            return true;
        }
        return false;
    }

    /// <summary>档案来源优先级（D03 §5.2：user_filled &gt; merchant_set(仅L3) &gt; oauth_userinfo &gt; event；未知/空 = 0）。</summary>
    private static int PriorityOf(string? source) => source switch
    {
        ProfileSource.UserFilled => 4,
        ProfileSource.MerchantSet => 3,
        ProfileSource.OAuthUserInfo => 2,
        ProfileSource.Event => 1,
        _ => 0
    };
}