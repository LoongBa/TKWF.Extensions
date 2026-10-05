namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 登录编排结果（V0.6.0 门面升级——Oracle P0-1：表现层零编排终态补全）。
/// <para>门面内完成 认证（Provider.AuthenticateAsync）→ 取账号（IAuthAccountQueryService）→ 签发（ITokenService）
/// 全编排——端点只调一个门面，不串多门面（V0.5.0 ADR92 既定原则保持）。</para>
/// <para>Success=true 时 <see cref="UserId"/> 为账号 UId、<see cref="Token"/> 含完整签发结果（access/refresh/expires_in）；
/// Success=false 时 <see cref="FailReason"/> 透传 Provider 失败语义（装配层负责 HTTP 映射）。</para>
/// </summary>
/// <param name="Success">认证 + 签发是否成功。</param>
/// <param name="UserId">成功时的账号 UId（令牌 sub 来源——装配层/测试取用）。</param>
/// <param name="Token">成功时的完整签发结果（含 AccessToken/RefreshToken/ExpiresIn）。</param>
/// <param name="FailReason">失败时的语义码（如 SMS_CODE_INVALID / SMS_PROVIDER_NOT_ENABLED / ACCOUNT_NOT_FOUND）。</param>
public sealed record LoginResult(
    bool Success,
    string? UserId,
    TokenIssueResult? Token,
    string? FailReason);
