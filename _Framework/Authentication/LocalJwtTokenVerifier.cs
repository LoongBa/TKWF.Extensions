using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 本地公钥令牌验证器——默认 <see cref="ITokenVerifier"/> 实现（VerifyMode.LocalPublicKey）。
/// <para>方案 §5.8——委托 <see cref="ITokenService.ValidateTokenAsync"/>（本实例签发 + 黑名单语义，验签/exp/kid/iss 全链）；
/// 共享公钥跨实例验签 / 远程 introspection（VerifyMode.RemoteIntrospection）由装配层替换实现。</para>
/// </summary>
internal sealed class LocalJwtTokenVerifier : ITokenVerifier
{
    private readonly ITokenService _tokenService;

    public LocalJwtTokenVerifier(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public Task<TokenValidationResult> VerifyAsync(string token, CancellationToken ct = default)
        => _tokenService.ValidateTokenAsync(token, ct);
}
