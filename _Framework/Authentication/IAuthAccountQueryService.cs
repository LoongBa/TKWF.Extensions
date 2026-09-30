using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Authentication;

/// <summary>账号查询服务——对外只读查询契约（UserCenter 桥接 / 装配实例 / 内部复用）。</summary>
public interface IAuthAccountQueryService
{
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default);
}

/// <summary>账号查询服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 IdentityPasswordManager 先例一致（public sealed + TryAddScoped 为注册方）；
/// 本类仅门面委托无外部扩展需求，internal 即可（AuthAccountEntityDataService 亦 internal，注入合法）。</para></summary>
internal sealed class AuthAccountQueryService(AuthAccountEntityDataService dataService) : IAuthAccountQueryService
{
    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => dataService.GetByUIdAsync(uid, ct);
    public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => dataService.GetByPhoneAsync(phone, ct);
    public Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
        => dataService.GetByWechatMpOpenIdAsync(openId, ct);
    public Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
        => dataService.GetByWechatWebOpenIdAsync(openId, ct);
}