using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 本地公钥令牌验证器——默认 <see cref="ITokenVerifier"/> 实现（VerifyMode.LocalPublicKey）。
/// <para>方案 §5.8——委托 <see cref="ITokenService.ValidateTokenAsync"/>（本实例签发 + 黑名单语义，验签/exp/kid/iss 全链）；
/// 共享公钥跨实例验签 / 远程 introspection（VerifyMode.RemoteIntrospection）由装配层替换实现。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；<see cref="ITokenService"/> 经 <c>User.Use&lt;接口&gt;()</c> AOP 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ITokenVerifier, LocalJwtTokenVerifier&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class LocalJwtTokenVerifier : DomainServiceBase, ITokenVerifier
{
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();

    public LocalJwtTokenVerifier(IDomainUser user) : base(user)
    {
    }

    public Task<TokenValidationResult> VerifyAsync(string token, CancellationToken ct = default)
        => TokenService.ValidateTokenAsync(token, ct);
}
