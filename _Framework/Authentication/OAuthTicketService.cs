using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 一次性票据服务实现——签发/消费（TTL 5min 单次 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。
/// <para>方案 §5.7——回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；</para>
/// <para>纯前端静态站走公网 /oauth/exchange + PKCE code_verifier；白名单经 <c>AuthCenterOptions.RedirectUriWhitelist</c>。</para>
/// </summary>
internal sealed class OAuthTicketService : IOAuthTicketService
{
    private readonly AuthCenterOptions _options;
    private readonly OAuthTicketEntityDataService _dataService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<OAuthTicketService> _logger;

    /// <summary>构造——注入认证中心配置、票据数据服务、令牌服务与日志。</summary>
    public OAuthTicketService(
        IOptions<AuthCenterOptions> options,
        OAuthTicketEntityDataService dataService,
        ITokenService tokenService,
        ILogger<OAuthTicketService> logger)
    {
        _options = options.Value;
        _dataService = dataService;
        _tokenService = tokenService;
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
            ExpiresAt = DateTime.UtcNow.AddMinutes(_options.TicketExpirationMinutes),
            CreateTime = DateTime.UtcNow
        };

        await _dataService.CreateAsync(entity, ct);
        return ticket;
    }

    /// <summary>换取——校验 TTL/单次/state/verifier/app_id/redirect_uri → 签发 JWT 对；失败抛 AuthenticationException（错误码见 OAuthTicketErrorCodes）。</summary>
    public async Task<OAuthTicketExchangeResult> ExchangeAsync(OAuthTicketExchangeRequest request, CancellationToken ct = default)
    {
        // (a) 票据存在性。
        var entity = await _dataService.GetByTicketAsync(request.Ticket, ct);
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

        // (h) 标记已消费（单次消费防重放）。
        await _dataService.MarkConsumedAsync(entity.Id, DateTime.UtcNow, ct);

        // (i) 签发 JWT 对。
        // 说明：票据换取是认证完成路径，调用方在签发时已绑定用户；此处认证方式取默认登录方式 sms，
        //      认证强度取手机号级（AuthLevel.Phone），教师核实声明默认 false（身份声明，非业务角色）。
        var tokenResult = await _tokenService.IssueTokenAsync(
            new TokenIssueRequest(
                entity.UserId,
                AuthTypes.Sms,
                (int)AuthLevel.Phone,
                TeacherVerified: false),
            ct);

        return new OAuthTicketExchangeResult(tokenResult.AccessToken, tokenResult.RefreshToken, tokenResult.ExpiresIn);
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
