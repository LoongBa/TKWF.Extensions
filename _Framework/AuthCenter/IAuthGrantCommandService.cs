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
/// 应用授权写入门面——跨聚合写归门面（V0.8.0，Oracle P1-1：OAuthTicketService 不得直触 AuthGrantEntityDataService；
/// V0.3.0 泛化 T-7：<see cref="RecordGrantAsync"/> Source/ValidUntil 参数化——redeem 源写路径）。
/// <para>upsert 幂等：UserId+AppId+Source 唯一约束（UX_AuthGrant_User_App_Source），存在刷新 Active+覆写 ValidUntil / 不存在创建；
/// DB 唯一约束兜底 TOCTOU（并发两写败者冲突 → catch → 重查转 update）。</para>
/// </summary>
public interface IAuthGrantCommandService : IDomainService
{
    /// <summary>记录应用授权（upsert 幂等泛化——Source/ValidUntil 参数化；redeem 源写路径 T-7）。
    /// 复合键 (UserId+AppId+Source) 存在 → 以传入 validUntilUtc 覆写 + Status=Active + UpdateTime；
    /// 不存在 → 创建（ValidUntil=传入值）。⚠️ 覆写=last-writer-wins，并发叠加防丢失更新（B1 I-5c 按用户串行化）归消费方编排。</summary>
    /// <remarks>匿名面声明：redeem 源为匿名/服务间调用链内触发（门面帧内）。</remarks>
    [AllowAnonymousFlag]
    Task RecordGrantAsync(string userId, string appId, string source, DateTime? validUntilUtc, CancellationToken ct = default);

    /// <summary>记录登录授权（upsert 幂等：UserId+AppId+Source=Login 唯一约束，存在刷新 Active / 不存在创建）。
    /// 委托 <see cref="RecordGrantAsync"/>——login 源 ValidUntil 恒 null。</summary>
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
    public Task RecordLoginGrantAsync(string userId, string appId, CancellationToken ct = default)
        => RecordGrantAsync(userId, appId, AuthGrantSources.Login, null, ct);   // login 源 ValidUntil 恒 null（T-7 委托——行为保持）

    /// <inheritdoc />
    public async Task RecordGrantAsync(string userId, string appId, string source, DateTime? validUntilUtc, CancellationToken ct = default)
    {
        // (a) upsert 幂等：复合键存在 → 覆写 ValidUntil + 刷新 Active 态（UpdateTime），不存在 → 创建
        var existing = await DataService.GetByKeyAsync(userId, appId, source, ct);
        if (existing is not null)
        {
            await DataService.RefreshActiveWithValidUntilAsync(existing.Id, validUntilUtc, DateTime.UtcNow, ct);
            return;
        }

        try
        {
            await DataService.CreateAsync(new AuthGrantEntity
            {
                UserId = userId,
                AppId = appId,
                Scopes = "",           // v0.8.0 空串（scope 授权后续迭代）
                ValidUntil = validUntilUtc,
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
                await DataService.RefreshActiveWithValidUntilAsync(winner.Id, validUntilUtc, DateTime.UtcNow, ct);
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
