using System;
using System.Linq;
using System.Security.Authentication;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Authentication;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 应用授权查询门面——只读（V0.8.0 `/grants` 端点数据底座消费面；B3b 生成化：userId 改读 ambient——
/// <see cref="ITokenVerificationContext.Current"/>，请求非 Bearer 认证 → 抛 <see cref="AuthenticationException"/> 401）。
/// <para>查询当前用户对指定应用（或全部）的有效授权记录（Status=Active，按 CreateTime 倒序）——应用授权语义
/// （用户授权某应用访问其数据的持久授权，跨会话），非会话状态。</para>
/// </summary>
public interface IAuthGrantQueryService : IDomainService
{
    /// <summary>查询当前请求 Bearer 用户对指定应用（或全部）的有效授权记录（Status=Active，按 CreateTime 倒序）——
    /// 返回 <see cref="GrantsResponse"/>。⚠️ 查询键微差：生成端点查询参数为 <c>appId</c>（camelCase——
    /// 生成器不透传 [FromQuery]，文档化微差）。</summary>
    [ApiExpose(RestRoute = "/grants", RestMethod = "GET")]   // AllowAnonymous=false（默认省略）——已认证端点，Bearer 必需（RequireAuthorization）
    Task<GrantsResponse> GetGrantsAsync(string? appId = null, CancellationToken ct = default);
}

/// <summary>
/// 应用授权查询门面实现——委托 <see cref="AuthGrantEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）。
/// <para>V0.8.0（ADR90 门面范式）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册 <c>AddConstructibleService&lt;IAuthGrantQueryService, AuthGrantQueryService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// B3b：ctor 注入 <see cref="ITokenVerificationContext"/>（ambient——userId 读当前 Bearer 验签产物）；
/// Scope 逗号拆分 + GrantView 映射下沉门面（原 Web 层逻辑）。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class AuthGrantQueryService : DomainServiceBase, IAuthGrantQueryService
{
    private AuthGrantEntityDataService? _dataService;
    private AuthGrantEntityDataService DataService => _dataService ??= User.Use<AuthGrantEntityDataService>();
    private readonly ITokenVerificationContext _context;

    public AuthGrantQueryService(IDomainUser user, ITokenVerificationContext context) : base(user)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<GrantsResponse> GetGrantsAsync(string? appId = null, CancellationToken ct = default)
    {
        var current = _context.Current;
        if (current is null)
            throw new AuthenticationException("AUTH_REQUIRED");

        var grants = await DataService.GetGrantsAsync(current.UserId, appId, ct);
        return new GrantsResponse(grants.Select(g => new GrantView(
            AppId: g.AppId,
            Scopes: g.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ValidUntil: g.ValidUntil,
            Source: g.Source,
            Status: g.Status)).ToList());
    }
}
