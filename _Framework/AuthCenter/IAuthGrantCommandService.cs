using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>授权来源常量（Oracle P2-4——镜像 OAuthTicketTypes/SmsScenes 范式）。</summary>
public static class AuthGrantSources
{
    /// <summary>登录授权（v0.8.0 唯一写入源——exchange 成功即落）。</summary>
    public const string Login = "login";

    /// <summary>口令兑换（B-口令兑换产品线预留——本迭代不写入）。</summary>
    public const string Redeem = "redeem";
}

/// <summary>
/// 应用授权写入门面——跨聚合写归门面（V0.8.0，Oracle P1-1：OAuthTicketService 不得直触 AuthGrantEntityDataService）。
/// <para>upsert 幂等：UserId+AppId+Source 唯一约束（UX_AuthGrant_User_App_Source），存在刷新 Active / 不存在创建；
/// DB 唯一约束兜底 TOCTOU（并发两写败者冲突 → catch → 重查转 update）。</para>
/// </summary>
public interface IAuthGrantCommandService : IDomainService
{
    /// <summary>记录登录授权（upsert 幂等：UserId+AppId+Source=Login 唯一约束，存在刷新 Active / 不存在创建）。</summary>
    /// <remarks>匿名面声明：exchange 为匿名端点调用链内触发（门面帧内）。</remarks>
    [AllowAnonymousFlag]
    Task RecordLoginGrantAsync(string userId, string appId, CancellationToken ct = default);
}

/// <summary>
/// 应用授权写入门面实现——委托 <see cref="AuthGrantEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）。
/// <para>V0.8.0（ADR90 门面范式）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册 <c>AddConstructibleService&lt;IAuthGrantCommandService, AuthGrantCommandService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class AuthGrantCommandService : DomainServiceBase, IAuthGrantCommandService
{
    private AuthGrantEntityDataService? _dataService;
    private AuthGrantEntityDataService DataService => _dataService ??= User.Use<AuthGrantEntityDataService>();

    public AuthGrantCommandService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task RecordLoginGrantAsync(string userId, string appId, CancellationToken ct = default)
    {
        var source = AuthGrantSources.Login;

        // (a) upsert 幂等：复合键存在 → 刷新 Active 态（UpdateTime），不存在 → 创建
        var existing = await DataService.GetByKeyAsync(userId, appId, source, ct);
        if (existing is not null)
        {
            await DataService.RefreshActiveAsync(existing.Id, DateTime.UtcNow, ct);
            return;
        }

        try
        {
            await DataService.CreateAsync(new AuthGrantEntity
            {
                UserId = userId,
                AppId = appId,
                Scopes = "",           // login 源 v0.8.0 空串（scope 授权后续迭代）
                ValidUntil = null,     // 应用授权有效期 null = 持续至吊销（非会话有效期）
                Source = source,
                Status = 0             // Active
            }, ct);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // (b) DB 唯一约束兜底 TOCTOU：并发两写败者唯一冲突 → 重查转 update（不抛——幂等语义）
            var winner = await DataService.GetByKeyAsync(userId, appId, source, ct);
            if (winner is not null)
            {
                await DataService.RefreshActiveAsync(winner.Id, DateTime.UtcNow, ct);
            }
            else
            {
                throw; // 理论不可达（冲突后重查无 = 行被删除）——抛原异常 fail-fast
            }
        }
    }

    /// <summary>
    /// 判定异常链是否含数据库唯一约束冲突（对齐 FileManagement/OrganizationUnit IsUniqueConstraintViolation 先例）。
    /// <para>SQLite：SqliteErrorCode 19 = SQLITE_CONSTRAINT（主项目未显式引用 Sqlite 包，以消息特征判定；
    /// 兜底消息特征 UNIQUE constraint failed / duplicate key / Duplicate entry 覆盖其他 Provider）。</para>
    /// </summary>
    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
