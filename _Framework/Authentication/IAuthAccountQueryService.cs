using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

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
/// 本类仅门面委托无外部扩展需求，internal 即可（AuthAccountEntityDataService 亦 internal，注入合法）。</para></summary>
internal sealed class AuthAccountQueryService : IAuthAccountQueryService
{
    private AuthAccountEntityDataService? _dataService;
    private readonly IDomainUser _user;
    private AuthAccountEntityDataService DataService => _dataService ??= _user.Use<AuthAccountEntityDataService>();

    public AuthAccountQueryService(IDomainUser user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => DataService.GetByUIdAsync(uid, ct);
    public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => DataService.GetByPhoneAsync(phone, ct);
    public Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
        => DataService.GetByWechatMpOpenIdAsync(openId, ct);
    public Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
        => DataService.GetByWechatWebOpenIdAsync(openId, ct);
}