using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// JWT → DomainUser 载荷解析器（方案 §5.8——Parse 内部强制 Verify，Oracle I5：杜绝「只 Parse 不验签」遗漏漏洞）。
/// <para>验签经 <see cref="ITokenVerifier"/>（默认 <see cref="LocalJwtTokenVerifier"/> 本地公钥；装配层可换远程 introspection）。</para>
/// </summary>
public static class JwtDomainUserParser
{
    /// <summary>验签 + 令牌 → 载荷（内部强制 Verify——失败抛 AuthenticationException）。</summary>
    public static Task<TokenValidationResult> ParseAndVerifyAsync(string token, ITokenVerifier verifier, CancellationToken ct = default)
    {
        // 强制验签（Oracle I5——不提供"仅解析"入口，防止调用方遗漏验签）
        return verifier.VerifyAsync(token, ct);
    }
}
