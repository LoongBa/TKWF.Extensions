using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 应用授权查询门面——只读（V0.8.0 `/grants` 端点数据底座消费面）。
/// <para>查询用户对指定应用（或全部）的有效授权记录（Status=Active，按 CreateTime 倒序）——应用授权语义
/// （用户授权某应用访问其数据的持久授权，跨会话），非会话状态。</para>
/// </summary>
public interface IAuthGrantQueryService : IDomainService
{
    /// <summary>查询用户对指定应用（或全部）的有效授权记录（Status=Active，按 CreateTime 倒序）。</summary>
    Task<IReadOnlyList<AuthGrantEntity>> GetGrantsAsync(string userId, string? appId = null, CancellationToken ct = default);
}

/// <summary>
/// 应用授权查询门面实现——委托 <see cref="AuthGrantEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）。
/// <para>V0.8.0（ADR90 门面范式）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册 <c>AddConstructibleService&lt;IAuthGrantQueryService, AuthGrantQueryService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class AuthGrantQueryService : DomainServiceBase, IAuthGrantQueryService
{
    private AuthGrantEntityDataService? _dataService;
    private AuthGrantEntityDataService DataService => _dataService ??= User.Use<AuthGrantEntityDataService>();

    public AuthGrantQueryService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuthGrantEntity>> GetGrantsAsync(string userId, string? appId = null, CancellationToken ct = default)
        => DataService.GetGrantsAsync(userId, appId, ct);
}
