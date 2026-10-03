using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 本地公钥令牌验证器——默认 <see cref="ITokenVerifier"/> 实现（VerifyMode.LocalPublicKey）。
/// <para>方案 §5.8——委托 <see cref="ITokenService.ValidateTokenAsync"/>（本实例签发 + 黑名单语义，验签/exp/kid/iss 全链）；
/// 共享公钥跨实例验签 / 远程 introspection（VerifyMode.RemoteIntrospection）由装配层替换实现。</para>
/// </summary>
internal sealed class LocalJwtTokenVerifier : ITokenVerifier
{
    private ITokenService? _tokenService;
    private readonly IDomainUser _user;
    private ITokenService TokenService => _tokenService ??= _user.Use<ITokenService>();

    public LocalJwtTokenVerifier(IDomainUser user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    public Task<TokenValidationResult> VerifyAsync(string token, CancellationToken ct = default)
        => TokenService.ValidateTokenAsync(token, ct);
}
