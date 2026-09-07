using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.Interception.Filters;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity;

/// <summary>
/// 身份认证服务（V0.3.0）——明文注册/登录 API 层（框架 AuthController 仅支持 SecurePassword 模式注册，
/// 本服务补充明文场景）。消费方 SG1b 经 <c>[GenerateController]</c> 生成 REST + GraphQL 端点
/// （白名单 <c>[TKWFEnabledExtension(typeof(IdentityExtensionInitializer&lt;&gt;))]</c> 聚合，ADR47）。
/// <para>非 DataService 命名（避免 CRUD 白名单逻辑）——所有 public async 方法自动纳入控制器。</para>
/// <para>匿名守卫（Oracle P2-3）：<c>[ApiExpose(AllowAnonymous=true)]</c>（HTTP 层）+ <c>[AllowAnonymousFlag]</c>（AOP 层）双控。</para>
/// </summary>
[GenerateController]
public partial class IdentityAuthService : DomainServiceBase
{
    private readonly IUserManager _userManager;

    public IdentityAuthService(IDomainUser user, IUserManager userManager) : base(user)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    /// <summary>明文注册（服务端 PasswordHasher 散列）——用户名已存在或密码不满足策略返回 false。</summary>
    [ApiExpose(RestRoute = "/auth/register", RestMethod = "POST", AllowAnonymous = true)]
    [AllowAnonymousFlag]
    public async Task<RegisterResult> RegisterAsync(
        string userName, string password, string? displayName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return new RegisterResult(false, "用户名或密码不能为空");

        // 重名预检（CreateUserAsync 不检查重名——UserStore 静默插入）
        if (await _userManager.FindByNameAsync(userName, ct) is not null)
            return new RegisterResult(false, "用户名已存在");

        var created = await _userManager.CreateUserAsync(userName, password, displayName ?? userName, ct);
        return created is null
            ? new RegisterResult(false, "密码不满足策略")
            : new RegisterResult(true);
    }

    /// <summary>明文登录（凭据验证 + 角色加载）——返回会话信息，失败返回 null。
    /// <para>独立端点：验证凭据并返回用户信息（Roles 经 Extensions 承载）；会话建立（LoginAsUserAsync）由消费方
    /// 按需调用——本服务是"凭据验证 API"，不隐式建立会话（区别于框架 AuthController 的登录端点）。</para></summary>
    [ApiExpose(RestRoute = "/auth/login", RestMethod = "POST", AllowAnonymous = true)]
    [AllowAnonymousFlag]
    public async Task<LoginPayload?> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var idUser = await _userManager.VerifyCredentialsAsync(userName, password, ct);
        if (idUser is null) return null;                     // 凭据无效（用户不存在/密码错/禁用）
        var roles = await _userManager.GetUserRolesAsync(idUser.Id, ct);
        return new LoginPayload(true, idUser.UserName, idUser.DisplayName, SessionKey: null,
            Extensions: roles.Select(r => new ExtensionEntry("Role", r.Name)).ToList());
    }
}
