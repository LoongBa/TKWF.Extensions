using System;
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

namespace TKWF.Ext.SSO;

/// <summary>
/// SSO 授权码服务（联邦 accesscode，通道 B 票据）——<see cref="ISsoAccessCodeService"/> 实现。
/// <para>设计文档 §6.1 + Oracle P2-1/P2-2：CSPRNG 32 字节 base64url / TTL 120s / 单次原子 CAS /
/// 只存 SHA256(code) 索引（防库泄露后 code 被盗用）/ PKCE 可选（签发时存 code_verifier SHA256 hash，
/// 消费时恒定时间比对——defense in depth）。OpenId 仅审计语义（不出 token2/URL）——实体无 OpenId 列，不落库。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部经 <see cref="SsoAccessCodeEntityDataService"/>
/// 内部转发访问器（Entity*，同程序集）委托查询/写入；原子消费经 <c>EntityUpdateWhereAsync</c>（ADR89，
/// 单语句引擎级条件 UPDATE——WHERE used=false 守卫，防重放恰一成功）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ISsoAccessCodeService, SsoAccessCodeService&gt;</c>（Initializer 负责）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SsoAccessCodeService : DomainServiceBase, ISsoAccessCodeService
{
    private SsoAccessCodeEntityDataService? _dataService;
    private readonly IOptions<SsoOptions> _options;
    private readonly ILogger<SsoAccessCodeService> _logger;

    private SsoAccessCodeEntityDataService DataService => _dataService ??= User.Use<SsoAccessCodeEntityDataService>();

    public SsoAccessCodeService(
        IDomainUser user,
        IOptions<SsoOptions> options,
        ILogger<SsoAccessCodeService> logger)
        : base(user)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 签发 accesscode——CSPRNG 32 字节 base64url（原文唯一返回给调用方，库内只存 SHA256 hash）。
    /// <para>TTL = <c>SsoOptions.AccessCodeExpirationSeconds</c>（默认 120）；关联字段
    /// ChannelId/UId/TargetAppId/Scope + 审计 IpAddress + 可选 CodeVerifierHash（PKCE，Oracle P2-1——
    /// 通道 B 非 public client 非强制，defense in depth）。</para>
    /// </summary>
    public async Task<SsoAccessCodeIssueResult> IssueAsync(SsoAccessCodeIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = NewAccessCode();
        var options = _options.Value;
        var expiresIn = Math.Max(options.AccessCodeExpirationSeconds, 1); // 配置异常钳制（默认 120）
        var now = DateTime.UtcNow;

        var entity = new SsoAccessCodeEntity
        {
            CodeHash = Sha256Hex(code),
            ChannelId = request.ChannelId,
            TargetAppId = request.TargetAppId,
            UId = request.UId,
            Scope = request.Scope,
            ExpiresAt = now.AddSeconds(expiresIn),
            Used = false,
            IpAddress = request.IpAddress,
            // PKCE 可选：签发时只存 code_verifier SHA256 hash（明文不落库）；为空 → 不要求验证
            CodeVerifierHash = !string.IsNullOrWhiteSpace(request.CodeVerifier) ? Sha256Hex(request.CodeVerifier) : null,
            CreateTime = now,
            UpdateTime = now,
        };
        await DataService.EntityCreateAsync(entity, ct);

        return new SsoAccessCodeIssueResult(code, expiresIn);
    }

    /// <summary>
    /// 消费 accesscode——SHA256(code) 查行 → 过期拒（TICKET_EXPIRED）→ PKCE verifier 校验
    /// （签发时 CodeVerifierHash 非空 → 必传且 SHA256 恒定时间比对，不匹配 TICKET_STATE_MISMATCH）→
    /// <b>原子 CAS</b>（<c>EntityUpdateWhereAsync(Id 匹配 &amp;&amp; Used=false, set Used=true)</c>——ADR89）：
    /// 影响 0 行 = 已被并发消费（重放）→ TICKET_CONSUMED + Warning；1 行 → 成功返回消费结果。
    /// </summary>
    public async Task<SsoAccessCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");

        var now = DateTime.UtcNow;
        var row = await DataService.EntityGetAsync(m => m.CodeHash == Sha256Hex(code), ct);
        if (row == null)
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");

        if (row.ExpiresAt <= now)
            throw new AuthenticationException("TICKET_EXPIRED");

        // PKCE verifier 校验（签发时 CodeVerifierHash 非空 → 必传 codeVerifier 且 SHA256 恒定时间比对）
        if (!string.IsNullOrEmpty(row.CodeVerifierHash))
        {
            if (string.IsNullOrEmpty(codeVerifier))
                throw new AuthenticationException("TICKET_STATE_MISMATCH");
            var verifierHash = Sha256Hex(codeVerifier);
            if (!FixedTimeEquals(verifierHash, row.CodeVerifierHash))
                throw new AuthenticationException("TICKET_STATE_MISMATCH");
        }

        // 原子 CAS：WHERE Id 匹配 AND used=false → set used=true（单语句引擎级条件 UPDATE——ADR89）
        // 影响 0 行 = 已被并发消费（重放）；1 行 = 本请求获得独占消费
        var affected = await DataService.EntityUpdateWhereAsync(
            m => m.Id == row.Id && !m.Used,
            m => new { Used = true },
            ct);
        if (affected == 0)
        {
            _logger.LogWarning("SSO accesscode 重放检测（codeHash={CodeHash}，uid={UId}）——已消费", row.CodeHash, row.UId);
            throw new AuthenticationException("TICKET_CONSUMED");
        }

        return new SsoAccessCodeConsumeResult(row.UId, row.TargetAppId, row.Scope);
    }

    // ── 私有实现（对齐 TokenService 静态辅助模式——SSO 独立扩展不复用 Authentication 主包） ──

    /// <summary>CSPRNG 32 字节 → Base64Url（授权码原文）。</summary>
    private static string NewAccessCode()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA256 hex（小写）——code / code_verifier 落库统一约定。</summary>
    private static string Sha256Hex(string input)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <summary>Base64Url 编码（RFC 7515——trim padding，+/ 替换 -_）。</summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>恒定时间字符串比对（防时序攻击——PKCE verifier hash 比对）。</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}