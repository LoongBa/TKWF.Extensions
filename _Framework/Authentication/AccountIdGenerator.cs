using System;

namespace TKWF.Ext.Authentication;

/// <summary>账号平台内部 id 生成器（MemberUId 风格 `u-{unixMs}-{rand}`——JWT sub 引用，方案 §5.3）。</summary>
internal static class AccountIdGenerator
{
    /// <summary>生成唯一平台内部 id：u-{unixMs}-{4 位随机}（高并发下 unixMs+随机足够——唯一约束兜底）。</summary>
    public static string NewUId()
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rand = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        return $"u-{unixMs}-{rand}";
    }
}
