using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 登录保护服务契约——AuthLoginAttempt 记录 + 限流窗口计数 + 最近尝试查询。
/// <para>方案 §5.6——只增语义（Store 仅 Create/Count，无 Update/Delete 暴露，对齐 SecurityLog）；</para>
/// <para>策略配置经 <c>AuthCenterOptions.LoginProtection</c>（短信 60s/小时/天/IP + OAuth 10 次/分钟/IP + 口令兑换 5 次/小时）。</para>
/// </summary>
public interface IAuthLoginAttemptService : IDomainService
{
    /// <summary>记录一次登录尝试（成功/失败均记录，追加写）。</summary>
    Task RecordAttemptAsync(AuthLoginAttemptEntity attempt, CancellationToken ct = default);

    /// <summary>按窗口计数判定限流（AuthCenterOptions.LoginProtection 策略按 authType 独立）。</summary>
    Task<bool> IsRateLimitedAsync(string userIdentity, string authType, CancellationToken ct = default);

    /// <summary>按窗口计数判定限流（V0.9.0 P0-NEW-2 双维度——密码链路 <paramref name="ipAddress"/> 非空时补 60min/PerIp 维度；
    /// 其余 authType 行为不变；IP 可空时退化为仅 userIdentity 维度）。</summary>
    Task<bool> IsRateLimitedAsync(string userIdentity, string? ipAddress, string authType, CancellationToken ct = default);

    /// <summary>最近尝试记录（审计/展示）。</summary>
    Task<List<AuthLoginAttemptEntity>> GetRecentAttemptsAsync(string userIdentity, string authType, int count, CancellationToken ct = default);
}
