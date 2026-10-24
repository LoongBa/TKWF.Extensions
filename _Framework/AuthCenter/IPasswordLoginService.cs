using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 密码登录编排门面（V0.9.0 B.9——镜像 <see cref="ISmsLoginService"/> 表现层零编排范式；ADR-密码策略与口令协议 决策 1
/// SecurePassword 协议——<paramref name="clientHash"/>+<paramref name="salt"/> 为客户端算的 PBKDF2 产物，服务端零明文）。
/// <para>门面内完成 认证（PasswordAuthenticationProvider）→ 取账号 → 签发令牌 全编排，端点只调一个门面；
/// B3b 升级返回 <see cref="TokenResponse"/>——失败抛类型化异常（频控 <see cref="TKW.Framework.Domain.Exceptions.RateLimitedException"/> 429 /
/// 业务 <see cref="TKW.Framework.Domain.Exceptions.DomainException"/> 400）。</para>
/// <para>选区：内部按 <see cref="AuthTypes.Password"/> 从 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> 集合挑选
/// Provider（门面 ctor 注入集合——帧内枚举，守卫工厂经 CurrentAopUser 供给）。</para>
/// <para>凭据标识：<paramref name="identifier"/> 为 UId 或 Phone（凭据角色——登录锚点，Password Provider 内部解析）。</para>
/// <para>⚠️ 不实现 <c>IAccountPasswordManager</c> 第二实现（ADR B.10——userName/IdentityUser 契约平行不互认）。</para>
/// </summary>
public interface IPasswordLoginService : IDomainService
{
    /// <summary>密码登录（凭据校验 → 查账号 → 签发令牌）——返回 <see cref="TokenResponse"/>；
    /// 前置频控命中抛 <see cref="TKW.Framework.Domain.Exceptions.RateLimitedException"/>（PASSWORD_RATE_LIMITED）。
    /// <paramref name="request"/>：<c>{identifier, clientHash, salt}</c>（body 绑定——SecurePassword 协议）。</summary>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/login/password", RestMethod = "POST", AllowAnonymous = true)]
    Task<TokenResponse> LoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
}