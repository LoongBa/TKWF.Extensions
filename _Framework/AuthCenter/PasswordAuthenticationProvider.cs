using System;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 密码认证 Provider（V0.9.0 ADR-密码策略与口令协议 决策 1——SecurePassword 协议：客户端算 clientHash+salt
/// （PBKDF2 600000，DomainOptions.Auth.Pbkdf2Iterations 单一来源），服务端<b>解保护存储 + 组装解析 + FixedTimeEquals 比对</b>，
/// 服务端零明文（<see cref="ICredentialProtector"/> AES-GCM——DB 泄露不可解密）。镜像 SmsAuthenticationProvider 范式）。
/// <para>流程：Identifier（UId 或 Phone——凭据标识）→ 查账号 → <c>IsEnabled</c> + <c>IFreezeStateProvider.IsFrozenAsync</c> 状态检查 →
/// <c>Unprotect(PasswordHash)</c> 解保护 → 解析组装格式 → <c>FixedTimeEquals(clientHash)</c> 比对 → 成功返回 UId。
/// 失败语义：PASSWORD_REQUIRED / ACCOUNT_NOT_FOUND / ACCOUNT_DISABLED / ACCOUNT_FROZEN / PASSWORD_NOT_SET /
/// PASSWORD_MISMATCH / PASSWORD_DECRYPT_FAILED / PASSWORD_FORMAT_INVALID（调用方负责 RecordAttemptAsync 落库）。</para>
/// <para>⚠️ 不实现 <c>IAccountPasswordManager</c> 第二实现（ADR B.10——userName/IdentityUser 契约平行不互认）。</para>
/// <para>V0.9.0 铁律：继承 <see cref="DomainServiceBase"/>（IDomainUser 永不注册 DI）；DataService/契约经
/// <c>User.Use&lt;T&gt;()</c> 懒加载；<see cref="ICredentialProtector"/> 构造注入（DomainHostInitializerBase 自动注册的
/// AesGcmCredentialProtector——非域服务，DI 正常解析）；注册 <c>TryAddEnumerableConstructible</c>（ADR92 集合版守卫工厂）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class PasswordAuthenticationProvider : DomainServiceBase, IAuthenticationProvider
{
    private readonly ICredentialProtector _credentialProtector;
    private readonly ILogger<PasswordAuthenticationProvider> _logger;
    private AuthAccountEntityDataService? _accountDataService;

    // V0.11.0（方案 §6.4/T3）：冻结判定经 IFreezeStateProvider（多原因冻结行集合）。
    private IFreezeStateProvider? _freezeState;
    private IFreezeStateProvider FreezeState => _freezeState ??= User.Use<IFreezeStateProvider>();

    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();

    // V0.11.0（方案 §6.5/T7）：手机号查询经 AuthFactor（账号不再落 Phone 列）。
    private IAuthFactorService? _factors;
    private IAuthFactorService Factors => _factors ??= User.Use<IAuthFactorService>();

    public PasswordAuthenticationProvider(
        IDomainUser user,
        ICredentialProtector credentialProtector,
        ILogger<PasswordAuthenticationProvider> logger)
        : base(user)
    {
        _credentialProtector = credentialProtector ?? throw new ArgumentNullException(nameof(credentialProtector));
        _logger = logger;
    }

    public string AuthType => AuthTypes.Password;

    public async Task<ProviderAuthenticateResult> AuthenticateAsync(ProviderAuthenticateContext context, CancellationToken ct = default)
    {
        // ① SecurePassword 验证输入：ClientHash + Salt（客户端算的 PBKDF2 产物）——服务端零明文（Password 字段不再使用，保留兼容）
        if (string.IsNullOrWhiteSpace(context.Identifier)
            || string.IsNullOrWhiteSpace(context.ClientHash) || string.IsNullOrWhiteSpace(context.Salt))
            return new ProviderAuthenticateResult(false, null, "PASSWORD_REQUIRED");

        // 凭据标识解析：Identifier 可为 UId 或 Phone（凭据角色——登录锚点）
        var account = await FindByIdentifierAsync(context.Identifier, ct);
        if (account is null)
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_NOT_FOUND");
        if (!account.IsEnabled)
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_DISABLED");
        if (await FreezeState.IsFrozenAsync(account.UId, ct: ct))   // V0.11.0 冻结判定（多原因冻结行集合——临时处置拦截新签发）
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_FROZEN");

        // 未设密（Password 因素缺失或 SecretHash 空）→ 缺省失败（不静默——防撞库无意义尝试）
        var passwordFactor = await FindPasswordFactorAsync(account.UId, ct);
        var secretHash = passwordFactor?.SecretHash;
        if (string.IsNullOrEmpty(secretHash))
            return new ProviderAuthenticateResult(false, null, "PASSWORD_NOT_SET");

        // ① SecurePassword 验证：解保护存储 → 解析组装格式 → FixedTimeEquals 比对 clientHash（服务端零明文）
        try
        {
            var assembledBytes = _credentialProtector.Unprotect(secretHash);
            var assembled = System.Text.Encoding.UTF8.GetString(assembledBytes);
            var parts = assembled.Split('.', 3);
            if (parts.Length != 3) return new ProviderAuthenticateResult(false, null, "PASSWORD_FORMAT_INVALID");

            var storedHash = Convert.FromBase64String(parts[2]);   // b64hash 段 = 客户端算的 clientHash
            var submittedHash = Convert.FromHexString(context.ClientHash);
            if (submittedHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(submittedHash, storedHash))
            {
                _logger.LogDebug("密码登录认证失败——PASSWORD_MISMATCH（UId={UId}）", account.UId);
                return new ProviderAuthenticateResult(false, null, "PASSWORD_MISMATCH");
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new ProviderAuthenticateResult(false, null, "PASSWORD_DECRYPT_FAILED");   // 密钥变更/非法 blob
        }
        catch (FormatException)
        {
            return new ProviderAuthenticateResult(false, null, "PASSWORD_FORMAT_INVALID");    // 非法 hex
        }

        // ⑥ 初始密码强制改密：认证成功但 MustChange=true → 门面/装配层据此编排改密（返回信号，登录仍成功——首次登录）
        _logger.LogDebug("密码登录成功——UserId={UserId}（MustChange={MustChange}）", account.UId, passwordFactor!.MustChange);
        return new ProviderAuthenticateResult(true, account.UId, null, AuthLevel.Phone);
    }

    /// <summary>按凭据标识查账号——UId 精确 / Phone（经 Sms 因素反查——V0.11.0 T7 账号不再落 Phone 列）。</summary>
    private async Task<AuthAccountEntity?> FindByIdentifierAsync(string identifier, CancellationToken ct)
    {
        if (identifier.StartsWith("u-", StringComparison.Ordinal))
            return await AccountDataService.GetByUIdAsync(identifier, ct);

        var factor = await Factors.GetByPhoneAsync(identifier, ct);
        return factor is null ? null : await AccountDataService.GetByUIdAsync(factor.UId, ct);
    }

    /// <summary>取账号的 Password 因素——Password 标识恒为 UId（一账号一密码，方案 §4 因素表）。</summary>
    private async Task<AuthFactorEntityDto?> FindPasswordFactorAsync(string uid, CancellationToken ct)
    {
        foreach (var factor in await Factors.ListByUIdAsync(uid, ct))
            if (factor.FactorType == (int)AuthFactorTypes.Password) return factor;
        return null;
    }
}