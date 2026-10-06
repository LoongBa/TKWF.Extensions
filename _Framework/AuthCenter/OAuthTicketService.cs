using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 一次性票据服务实现——签发/消费（TTL 5min 单次 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。
/// <para>方案 §5.7——回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；</para>
/// <para>纯前端静态站走公网 /oauth/exchange + PKCE code_verifier；白名单经 <c>AuthCenterOptions.RedirectUriWhitelist</c>。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService/<see cref="ITokenService"/> 经 <c>User.Use&lt;T&gt;()</c> 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IOAuthTicketService, OAuthTicketService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class OAuthTicketService : DomainServiceBase, IOAuthTicketService
{
    private readonly AuthCenterOptions _options;
    private OAuthTicketEntityDataService? _dataService;
    private ITokenService? _tokenService;
    private IAuthGrantCommandService? _grantCommandService;
    private readonly ILogger<OAuthTicketService> _logger;

    private OAuthTicketEntityDataService DataService => _dataService ??= User.Use<OAuthTicketEntityDataService>();
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();
    private IAuthGrantCommandService GrantCommandService => _grantCommandService ??= User.Use<IAuthGrantCommandService>();

    /// <summary>构造——注入认证中心配置、用户上下文与日志（DataService/TokenService 经 User.Use&lt;T&gt;() 懒加载）。</summary>
    public OAuthTicketService(
        IDomainUser user,
        IOptions<AuthCenterOptions> options,
        ILogger<OAuthTicketService> logger)
        : base(user)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>签发一次性票据——高熵 ticket + TTL 5min + state +（可选）PKCE code_verifier hash 落库。</summary>
    public async Task<string> IssueAsync(OAuthTicketIssueRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AppId))
        {
            throw new ArgumentException("AppId 不能为空。", nameof(request));
        }

        var whitelist = _options.RedirectUriWhitelist;
        if (whitelist is null || whitelist.Length == 0)
        {
            throw new InvalidOperationException("AuthCenterOptions.RedirectUriWhitelist 未配置——请先配置票据换取 redirect_uri 白名单。");
        }
        if (!whitelist.Contains(request.RedirectUri, StringComparer.Ordinal))
        {
            throw new ArgumentException($"RedirectUri 不在白名单内：{request.RedirectUri}", nameof(request));
        }

        // 高熵一次性票据：32 随机字节 → Base64Url（防枚举）。
        var ticketBytes = RandomNumberGenerator.GetBytes(32);
        var ticket = Base64UrlEncode(ticketBytes);

        string? codeVerifierHash = null;
        if (request.CodeVerifier is not null)
        {
            ValidateCodeVerifier(request.CodeVerifier);
            codeVerifierHash = Sha256Hex(request.CodeVerifier);
        }

        var entity = new OAuthTicketEntity
        {
            Ticket = ticket,
            TicketType = request.TicketType,
            AppId = request.AppId,
            RedirectUri = request.RedirectUri,
            State = request.State,
            CodeVerifierHash = codeVerifierHash,
            UserId = request.UserId, // V0.8.0 B5：签发即绑定（authorize 时用户已登录带 UId——非空则落库）
            ExpiresAt = DateTime.UtcNow.AddMinutes(_options.TicketExpirationMinutes),
            CreateTime = DateTime.UtcNow
        };

        await DataService.CreateAsync(entity, ct);
        return ticket;
    }

    /// <summary>换取——校验 TTL/单次/state/verifier/app_id/redirect_uri → 签发 JWT 对；失败抛 AuthenticationException（错误码见 OAuthTicketErrorCodes）。</summary>
    public async Task<OAuthTicketExchangeResult> ExchangeAsync(OAuthTicketExchangeRequest request, CancellationToken ct = default)
    {
        // (a) 票据存在性。
        var entity = await DataService.GetByTicketAsync(request.Ticket, ct);
        if (entity is null)
        {
            throw new AuthenticationException("TICKET_NOT_FOUND");
        }

        // (b) 单次消费防重放。
        if (entity.IsConsumed)
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketConsumed);
        }

        // (c) TTL 过期。
        if (entity.ExpiresAt < DateTime.UtcNow)
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketExpired);
        }

        // (d) 跨应用抢先消费防御。
        if (!string.Equals(request.AppId, entity.AppId, StringComparison.Ordinal))
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketStateMismatch);
        }

        // (e) CSRF state 比对（签发时带 state 则必比对）。
        if (entity.State is not null && !string.Equals(request.State, entity.State, StringComparison.Ordinal))
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketStateMismatch);
        }

        // (f) PKCE 矩阵 C4：
        //   hash null + verifier null  → 允许（服务端/trust 签发路径——依赖 AppId 白名单 + 可信网络）
        //   hash 非 null + verifier null → 拒绝（缺 verifier）
        //   hash 非 null + verifier 非 null → SHA256(verifier) 必须等于 hash
        if (entity.CodeVerifierHash is not null)
        {
            if (request.CodeVerifier is null)
            {
                throw new AuthenticationException(OAuthTicketErrorCodes.TicketStateMismatch);
            }
            if (!string.Equals(Sha256Hex(request.CodeVerifier), entity.CodeVerifierHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new AuthenticationException(OAuthTicketErrorCodes.TicketStateMismatch);
            }
        }

        // (g) 票据必须已绑定用户（签发时由调用方绑定）。
        if (entity.UserId is null)
        {
            throw new AuthenticationException("TICKET_NOT_BOUND");
        }

        // (h) 条件标记已消费（单次消费防重放——Oracle M2：输者抛 TICKET_CONSUMED）
        if (!await DataService.MarkConsumedAsync(entity.Id, DateTime.UtcNow, ct))
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketConsumed);
        }

        // (i) 签发 JWT 对。
        // 说明：票据换取是认证完成路径，调用方在签发时已绑定用户；此处认证方式取默认登录方式 sms，
        //      认证强度取手机号级（AuthLevel.Phone），教师核实声明默认 false（身份声明，非业务角色）。
        var tokenResult = await TokenService.IssueTokenAsync(
            new TokenIssueRequest(
                entity.UserId,
                AuthTypes.Sms,
                (int)AuthLevel.Phone,
                TeacherVerified: false),
            ct);

        // (i') 落登录授权（V0.8.0 写入点——Oracle P1-1 经 IAuthGrantCommandService 门面，OAuthTicketService
        //      不直触 AuthGrantEntityDataService）。best-effort：写入失败不阻断 JWT 签发——token 独立有效，
        //      grant 仅服务 /grants 查询；可接受降级（README 标注）。
        try
        {
            await GrantCommandService.RecordLoginGrantAsync(entity.UserId, entity.AppId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "登录授权记录写入失败（UserId={UserId} AppId={AppId}）——不影响 JWT 签发（grant 降级，/grants 暂缺该授权）",
                entity.UserId, entity.AppId);
        }

        return new OAuthTicketExchangeResult(tokenResult.AccessToken, tokenResult.RefreshToken, tokenResult.ExpiresIn);
    }

    /// <summary>
    /// 登录后补绑（V0.8.0 B5——Oracle P0-1 安全模型）：<b>已认证帧</b>调用，userId 取
    /// <see cref="DomainServiceBase.User"/>（当前认证用户），<b>不接受请求体 userId 参数</b>——
    /// 防票据泄漏后攻击者绑定任意 userId 实现账号接管。CAS 条件更新（Oracle P1-2）防并发双绑竞态。
    /// </summary>
    public async Task BindTicketAsync(string ticket, CancellationToken ct = default)
    {
        // 已认证帧守卫：无认证用户上下文（匿名游客帧/System）→ 拒绝（对齐"无帧抛守卫"语义——InvalidOperationException）
        var userId = User.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException(
                "BindTicketAsync 须在已认证帧内调用（userId 取当前认证用户，不可为空）——拒绝匿名绑定。");

        // (a) 票据存在性。
        var entity = await DataService.GetByTicketAsync(ticket, ct);
        if (entity is null)
        {
            throw new AuthenticationException("TICKET_NOT_FOUND");
        }

        // (b) 已消费（单次消费防重放）。
        if (entity.IsConsumed)
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketConsumed);
        }

        // (c) TTL 过期。
        if (entity.ExpiresAt < DateTime.UtcNow)
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketExpired);
        }

        // (d) CAS 条件绑定（Oracle P1-2）：WHERE Id = ? AND UserId IS NULL → SET UserId。
        //     先查仅用于友好错误码（a-c）；最终一致性由条件更新保证——并发双绑败者返回 false → TICKET_ALREADY_BOUND。
        if (!await DataService.BindUserAsync(entity.Id, userId, ct))
        {
            throw new AuthenticationException(OAuthTicketErrorCodes.TicketAlreadyBound);
        }
    }

    /// <summary>RFC 7636 code_verifier 校验——43~128 位 unreserved 字符（A-Z / a-z / 0-9 / - / . / _ / ~）。</summary>
    private static void ValidateCodeVerifier(string codeVerifier)
    {
        if (codeVerifier.Length is < 43 or > 128)
        {
            throw new ArgumentException("code_verifier 长度必须为 43~128 位（RFC 7636）。", nameof(codeVerifier));
        }
        foreach (var c in codeVerifier)
        {
            var isUnreserved =
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c is '-' or '.' or '_' or '~';
            if (!isUnreserved)
            {
                throw new ArgumentException("code_verifier 只能包含 unreserved 字符（A-Z / a-z / 0-9 / - / . / _ / ~，RFC 7636）。", nameof(codeVerifier));
            }
        }
    }

    /// <summary>SHA256 小写 hex（与项目其他服务散列约定一致）。</summary>
    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Base64Url 编码（RFC 4648 §5——无填充，URL 安全）。</summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
