namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换业务错误码（镜像 AuthCenter OAuthTicketErrorCodes 范式）——经 BCL <c>AuthenticationException</c> 抛出
/// （<c>new AuthenticationException(RedemptionErrorCodes.Xxx)</c>，对齐 AuthCenter 票据/兑换域惯例）。
/// </summary>
public static class RedemptionErrorCodes
{
    /// <summary>兑换码不存在（无效码）。</summary>
    public const string CodeInvalid = "REDEMPTION_CODE_INVALID";

    /// <summary>兑换码已被兑换（一码一兑）。</summary>
    public const string CodeUsed = "REDEMPTION_CODE_USED";

    /// <summary>兑换码已过期（ExpireAtUtc 已过——顺手惰性翻转 Status=2）。</summary>
    public const string CodeExpired = "REDEMPTION_CODE_EXPIRED";

    /// <summary>兑换尝试频控超限（窗口内无效码尝试超上限）。</summary>
    public const string TooManyAttempts = "REDEMPTION_TOO_MANY_ATTEMPTS";
}
