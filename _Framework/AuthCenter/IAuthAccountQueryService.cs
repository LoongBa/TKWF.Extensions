using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>账号查询服务——对外只读查询契约（UserCenter 桥接 / 装配实例 / 内部复用）。</summary>
public interface IAuthAccountQueryService : IDomainService
{
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default);
}

/// <summary>账号查询服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 IdentityPasswordManager 先例一致（public sealed + TryAddScoped 为注册方）；
/// 本类仅门面委托无外部扩展需求，internal 即可（AuthAccountEntityDataService 亦 internal，注入合法）。
/// V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册改
/// <c>AddConstructibleService&lt;IAuthAccountQueryService, AuthAccountQueryService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// V0.6.0（SSO 立项，ADR-SSO Oracle P1-2）：补实现 <see cref="ISsoAccountQueryService"/>——SSO 消费面契约
/// （返回不可变 DTO <see cref="SsoAccountDto"/>，敏感字段不出契约包）。
/// V0.6.0（SSO.WeChat 评审 P1-1）：补实现 <see cref="ISsoAccountLinkService"/>——联盟锚点写契约。</para></summary>
[DiContractIgnore]
internal sealed class AuthAccountQueryService : DomainServiceBase, IAuthAccountQueryService, ISsoAccountQueryService, ISsoAccountLinkService
{
    private AuthAccountEntityDataService? _dataService;
    private AuthAccountEntityDataService DataService => _dataService ??= User.Use<AuthAccountEntityDataService>();

    public AuthAccountQueryService(IDomainUser user) : base(user)
    {
    }

    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => DataService.GetByUIdAsync(uid, ct);
    public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => DataService.GetByPhoneAsync(phone, ct);
    public Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
        => DataService.GetByWechatMpOpenIdAsync(openId, ct);
    public Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
        => DataService.GetByWechatWebOpenIdAsync(openId, ct);

    /// <inheritdoc cref="ISsoAccountQueryService.GetByUIdAsync"/>
    /// <remarks>显式接口实现——与 <see cref="IAuthAccountQueryService.GetByUIdAsync"/> 同名同参不同返回类型（实体 vs DTO），
    /// C# 隐式实现签名冲突，须显式实现区分。</remarks>
    async Task<SsoAccountDto?> ISsoAccountQueryService.GetByUIdAsync(string uid, CancellationToken ct)
    {
        var entity = await DataService.GetByUIdAsync(uid, ct);
        return entity is null ? null : ToDto(entity);
    }

    /// <inheritdoc cref="ISsoAccountLinkService.GetByFederationAnchorAsync"/>
    public async Task<SsoAccountDto?> GetByFederationAnchorAsync(string federationAnchorOpenId, CancellationToken ct)
    {
        var entity = await DataService.GetByFederationAnchorAsync(federationAnchorOpenId, ct);
        return entity is null ? null : ToDto(entity);
    }

    /// <inheritdoc cref="ISsoAccountLinkService.SetFederationAnchorAsync"/>
    public async Task<SsoAccountDto> SetFederationAnchorAsync(string uid, string federationAnchorOpenId, CancellationToken ct)
    {
        await DataService.SetFederationAnchorAsync(uid, federationAnchorOpenId, ct);
        var entity = await DataService.GetByUIdAsync(uid, ct);
        return entity is null
            ? new SsoAccountDto(uid, federationAnchorOpenId, null, null, 0)
            : ToDto(entity);
    }

    /// <summary>实体 → SSO 消费面 DTO（只映射消费字段，敏感字段不外泄）。</summary>
    private static SsoAccountDto ToDto(AuthAccountEntity entity)
        => new SsoAccountDto(
            UId: entity.UId,
            FederationAnchorOpenId: entity.FederationAnchorOpenId,
            Nickname: entity.Nickname,
            AvatarUrl: entity.Avatar,
            AuthLevel: entity.AuthLevel);
}