using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>账号管理服务——对外写契约（DMP 渐进替换影子账号 upsert / 装配实例 / 内部复用）。
/// <para>写路径暴露裁定（ADR-Authentication-账号写契约）：v0.2.0 方案 C1「写路径不暴露」因 DMP P1 令牌替换
/// 真实消费需求反转——平台管理员影子 AuthAccount 必须可由消费端创建/更新/失效（TokenService.RefreshTokenAsync
/// 强依赖 AuthAccount 存在 + IsEnabled + TokenVersion，L196-200）。</para></summary>
public interface IAuthAccountService : IDomainService
{
    /// <summary>按平台内部 id 查询账号（upsert 流 read 前置；与 <see cref="IAuthAccountQueryService"/> 重复委托同一 DataService——单注入便利）。</summary>
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);

    /// <summary>创建账号（回写自增 Id；CreateTime/UpdateTime UTC——影子账号 Phone 可空：唯一索引对 NULL 放行）。</summary>
    Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default);

    /// <summary>更新账号（先设 UpdateTime；IsEnabled 禁用/回用由此路）。</summary>
    Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default);

    /// <summary>TokenVersion 自增——密码/绑定变更后旧 Refresh Token 失效（DMP 改密 → 影子账号 bump → 旧 refresh REFRESH_STALE）。</summary>
    Task IncrementTokenVersionAsync(string uid, CancellationToken ct = default);
}

/// <summary>账号管理服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 <see cref="AuthAccountQueryService"/> 先例一致（public 契约 + internal 实现 + TryAddScoped 注册）。
/// V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册改
/// <c>AddConstructibleService&lt;IAuthAccountService, AuthAccountService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para></summary>
[DiContractIgnore]
internal sealed class AuthAccountService : DomainServiceBase, IAuthAccountService
{
    private AuthAccountEntityDataService? _dataService;
    private AuthAccountEntityDataService DataService => _dataService ??= User.Use<AuthAccountEntityDataService>();

    public AuthAccountService(IDomainUser user) : base(user)
    {
    }

    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => DataService.GetByUIdAsync(uid, ct);
    public Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default)
        => DataService.CreateAsync(account, ct);
    public Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default)
        => DataService.UpdateAsync(account, ct);
    public Task IncrementTokenVersionAsync(string uid, CancellationToken ct = default)
        => DataService.IncrementTokenVersionAsync(uid, ct);
}