using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// 授权码服务（accesscode，安全数据投递通道）——<see cref="IAccessCodeService"/> 实现。
/// <para>设计文档 §6.1 + Oracle P2-1/P2-2：CSPRNG 32 字节 base64url / TTL 120s / 单次原子 CAS /
/// 只存 SHA256(code) 索引（防库泄露后 code 被盗用）/ PKCE 可选（签发时存 code_verifier SHA256 hash，
/// 消费时恒定时间比对——defense in depth）。OpenId 仅审计语义（不出 token2/URL）——实体无 OpenId 列，不落库。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部经 <see cref="AccessCodeEntityDataService"/>
/// 内部转发访问器（Entity*，同程序集）委托查询/写入；原子消费经 <c>EntityUpdateWhereAsync</c>（ADR89，
/// 单语句引擎级条件 UPDATE——WHERE used=false 守卫，防重放恰一成功）。</para>
/// <para>方案 §5.5（<b>安全数据投递增强</b>）：新增 <c>PayloadEncrypted</c>（附带信息 AES-GCM 密文——keyed
/// <see cref="ISymmetricKeyProvider"/> 键 <see cref="SymmetricKeyProviderKeys.TrustCenter"/>，明文不落库）+
/// <c>ExpectedClaimant</c>（可核销人——进原子 CAS 条件无 TOCTOU）三方法：<c>IssueAsync(payloadJson,...)</c> /
/// <c>PeekAsync&lt;T&gt;</c>（只读快照非锁定）/ <c>RedeemAsync&lt;T&gt;</c>（CAS 核销 + 销毁取回）；容量校验
/// PAYLOAD_TOO_LARGE（明文 ≤ ~4068 字节）；清理任务 <c>CleanupExpiredAsync</c>（消费方经 IRecurringBackgroundJobManager
/// 周期调度——对齐 Metrics 定时重算范式）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IAccessCodeService, AccessCodeService&gt;</c>（Initializer 负责）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation 迁入——<c>SsoAccessCodeService</c> → <see cref="AccessCodeService"/>
/// （去 Sso 前缀，方案 §5.8）；契约 <c>ISsoAccessCodeService</c> → <see cref="IAccessCodeService"/>；
/// 实体 <c>SsoAccessCodeEntity</c> → <see cref="AccessCodeEntity"/>（表 TKWF_AccessCode）；命名空间 TKWF.Ext.TrustCenter。</para>
/// </summary>
[DiContractIgnore]
internal sealed class AccessCodeService : DomainServiceBase, IAccessCodeService
{
    /// <summary>附带信息明文容量上限（字节）——列表 <c>PayloadEncrypted</c> MaxLength(4096) 为 AES-GCM 密文
    /// base64：明文 = 4096 - 12(nonce) - 16(tag) = 4068（方案 §5.5 P7 fail-hard）。</summary>
    private const int MaxPayloadBytes = 4096 - 12 - 16;

    private AccessCodeEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;
    private readonly IOptions<TrustCenterOptions> _options;
    private readonly ILogger<AccessCodeService> _logger;

    private AccessCodeEntityDataService DataService => _dataService ??= User.Use<AccessCodeEntityDataService>();

    public AccessCodeService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider keys,
        IOptions<TrustCenterOptions> options,
        ILogger<AccessCodeService> logger)
        : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 签发 accesscode——CSPRNG 32 字节 base64url（原文唯一返回给调用方，库内只存 SHA256 hash）。
    /// <para>TTL = <c>TrustCenterOptions.AccessCodeExpirationSeconds</c>（默认 120）；关联字段
    /// ChannelId/UId/TargetAppId/Scope + 审计 IpAddress + 可选 CodeVerifierHash（PKCE，Oracle P2-1——
    /// 通道 B 非 public client 非强制，defense in depth）。</para>
    /// </summary>
    public async Task<AccessCodeIssueResult> IssueAsync(AccessCodeIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = NewAccessCode();
        var options = _options.Value;
        var expiresIn = Math.Max(options.AccessCodeExpirationSeconds, 1); // 配置异常钳制（默认 120）
        var now = DateTime.UtcNow;

        var entity = new AccessCodeEntity
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

        return new AccessCodeIssueResult(code, expiresIn);
    }

    /// <summary>
    /// 消费 accesscode——SHA256(code) 查行 → 过期拒（TICKET_EXPIRED）→ PKCE verifier 校验
    /// （签发时 CodeVerifierHash 非空 → 必传且 SHA256 恒定时间比对，不匹配 TICKET_STATE_MISMATCH）→
    /// <b>原子 CAS</b>（<c>EntityUpdateWhereAsync(Id 匹配 &amp;&amp; Used=false, set Used=true)</c>——ADR89）：
    /// 影响 0 行 = 已被并发消费（重放）→ TICKET_CONSUMED + Warning；1 行 → 成功返回消费结果。
    /// </summary>
    public async Task<AccessCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default)
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
            _logger.LogWarning("accesscode 重放检测（codeHash={CodeHash}，uid={UId}）——已消费", row.CodeHash, row.UId);
            throw new AuthenticationException("TICKET_CONSUMED");
        }

        return new AccessCodeConsumeResult(row.UId, row.TargetAppId, row.Scope);
    }

    /// <summary>
    /// 签发 accesscode（安全数据投递通道——方案 §5.5）：附带信息 <paramref name="payloadJson"/>（可空 = 纯核销通道）+
    /// 可核销人 <paramref name="expectedClaimant"/>（可空 = 可转让）。
    /// <para><paramref name="ttl"/> ≤ 0 → 钳制为 <c>TrustCenterOptions.AccessCodeExpirationSeconds</c>（默认 120）。</para>
    /// <para>附带信息：非空时校验 UTF8 明文 ≤ <see cref="MaxPayloadBytes"/>（4068——AES-GCM 开销），超限抛
    /// <c>AuthenticationException("PAYLOAD_TOO_LARGE")</c> → keyed <see cref="ISymmetricKeyProvider"/>
    /// AES-GCM 加密（单段 base64(nonce[12]‖cipher‖tag[16])）落 <c>PayloadEncrypted</c>（明文不落库）。</para>
    /// </summary>
    public async Task<AccessCodeIssueResult> IssueAsync(string? payloadJson, TimeSpan ttl, string? expectedClaimant, CancellationToken ct = default)
    {
        var code = NewAccessCode();
        var options = _options.Value;
        // ttl ≤ 0 → 钳制为配置默认（AccessCodeExpirationSeconds，默认 120——对齐 request 重载语义）
        var expiresIn = ttl <= TimeSpan.Zero
            ? Math.Max(options.AccessCodeExpirationSeconds, 1)
            : Math.Max((int)ttl.TotalSeconds, 1);
        var now = DateTime.UtcNow;

        // 附带信息：容量校验（fail-hard P7）→ AES-GCM 加密落库（明文不落库）
        string? payloadEncrypted = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            if (Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
                throw new AuthenticationException("PAYLOAD_TOO_LARGE");
            payloadEncrypted = _keys.Encrypt(payloadJson);
        }

        var entity = new AccessCodeEntity
        {
            CodeHash = Sha256Hex(code),
            ChannelId = "",
            TargetAppId = "",
            UId = "",
            Scope = null,
            ExpiresAt = now.AddSeconds(expiresIn),
            Used = false,
            IpAddress = null,
            CodeVerifierHash = null,
            PayloadEncrypted = payloadEncrypted,
            ExpectedClaimant = !string.IsNullOrWhiteSpace(expectedClaimant) ? expectedClaimant : null,
            CreateTime = now,
            UpdateTime = now,
        };
        await DataService.EntityCreateAsync(entity, ct);

        return new AccessCodeIssueResult(code, expiresIn);
    }

    /// <summary>
    /// 预读附带信息（<b>只读快照非锁定</b>）：查行（SHA256(code)）→ 过期拒（TICKET_EXPIRED）→
    /// 有密文解密 + JSON 反序列化为 <typeparamref name="T"/>；无附带信息返回 default。
    /// <para>⚠️ 并发语义（显式声明）：Peek <b>不锁定/不消费</b>——Peek 后行可能被并发 Redeem；
    /// 业务层不得基于 Peek 结果做不可逆决策（不可逆动作一律经 <see cref="RedeemAsync{T}"/> 原子 CAS）。</para>
    /// </summary>
    public async Task<T?> PeekAsync<T>(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");

        var row = await DataService.EntityGetAsync(m => m.CodeHash == Sha256Hex(code), ct);
        if (row == null)
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        if (row.ExpiresAt <= DateTime.UtcNow)
            throw new AuthenticationException("TICKET_EXPIRED");

        return DecryptPayload<T>(row);
    }

    /// <summary>
    /// 核销取回附带信息 + 销毁（单次原子 CAS）：查行 → 过期拒（TICKET_EXPIRED）→
    /// <b>原子 CAS 带 ExpectedClaimant 条件</b>（<c>EntityUpdateWhereAsync</c>：WHERE CodeHash 匹配 AND used=false
    /// AND (ExpectedClaimant IS NULL OR ExpectedClaimant = claimant)，set Used=true——ADR89 单语句引擎级条件 UPDATE）→
    /// 影响 0 → <b>重查判因</b>（无 TOCTOU——CAS 原子性保证恰一成功）：行不存在 → ACCESS_CODE_NOT_FOUND；
    /// Used=true → TICKET_CONSUMED；ExpectedClaimant 不匹配 → CLAIMANT_MISMATCH；过期 → TICKET_EXPIRED；
    /// 其余 → TICKET_CONSUMED 兜底。成功 → 解密 payload + JSON 反序列化为 <typeparamref name="T"/> + 销毁（Used=true）。
    /// </summary>
    public async Task<T?> RedeemAsync<T>(string code, string? claimant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");

        var now = DateTime.UtcNow;
        var row = await DataService.EntityGetAsync(m => m.CodeHash == Sha256Hex(code), ct);
        if (row == null)
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        if (row.ExpiresAt <= now)
            throw new AuthenticationException("TICKET_EXPIRED");

        // 原子 CAS 带 ExpectedClaimant 条件（核销人 + 并发核销联合判定——无 TOCTOU）
        var affected = await DataService.EntityUpdateWhereAsync(
            m => m.CodeHash == row.CodeHash && !m.Used && (m.ExpectedClaimant == null || m.ExpectedClaimant == claimant),
            m => new { Used = true },
            ct);
        if (affected == 0)
        {
            // 重查判因——区分：不存在 / 已消费（重放）/ 核销人不符 / 过期
            var fresh = await DataService.EntityGetAsync(m => m.CodeHash == row.CodeHash, ct);
            if (fresh == null)
                throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
            if (fresh.Used)
                throw new AuthenticationException("TICKET_CONSUMED");
            if (!string.IsNullOrEmpty(fresh.ExpectedClaimant) && fresh.ExpectedClaimant != claimant)
                throw new AuthenticationException("CLAIMANT_MISMATCH");
            if (fresh.ExpiresAt <= DateTime.UtcNow)
                throw new AuthenticationException("TICKET_EXPIRED");

            _logger.LogWarning("accesscode 核销 CAS 失败且重查无因（codeHash={CodeHash}）——兜底拒绝", row.CodeHash);
            throw new AuthenticationException("TICKET_CONSUMED");
        }

        // 成功——销毁语义 = Used=true（CAS 已置位）；解密 payload 取回
        return DecryptPayload<T>(row);
    }

    /// <summary>
    /// 清理过期 accesscode 行（含 Payload 密文——防业务数据残留）：删除 <c>ExpiresAt &lt; UtcNow - retentionDays</c> 的行。
    /// <para><paramref name="retentionDays"/> 为空 → 取 <c>TrustCenterOptions.AccessCodeRetentionDays</c>（默认 7）；
    /// ≤ 0 → 跳过清理（返回 0）。</para>
    /// <para>⚠️ 调度约定：消费方经 <c>IRecurringBackgroundJobManager</c> 周期调度（对齐 Metrics 定时重算范式）。</para>
    /// </summary>
    public async Task<int> CleanupExpiredAsync(int? retentionDays = null, CancellationToken ct = default)
    {
        var days = retentionDays ?? _options.Value.AccessCodeRetentionDays;
        if (days <= 0)
        {
            _logger.LogDebug("accesscode 清理跳过——保留天数 <= 0（retentionDays={RetentionDays}）", days);
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var stale = await DataService.EntitySelectAsync(m => m.ExpiresAt < cutoff, 0, 100_000, null, ct);
        if (stale.Count == 0)
        {
            _logger.LogDebug("accesscode 清理——无过期行（cutoff={Cutoff:O}）", cutoff);
            return 0;
        }

        var deleted = await DataService.EntityDeleteBatchAsync(stale.Select(s => s.Id), ct);
        _logger.LogInformation("accesscode 清理——删除 {Deleted} 行过期记录（cutoff={Cutoff:O}）", deleted, cutoff);
        return deleted;
    }

    // ── 私有实现（对齐 TokenService 静态辅助模式） ──

    /// <summary>解密附带信息 + JSON 反序列化为 <typeparamref name="T"/>；无密文返回 default。
    /// 密文被篡改/密钥不匹配 → <see cref="CryptographicException"/> / <see cref="FormatException"/>
    /// （GCM 认证失败——fail-closed 拒信任，不静默降级）。</summary>
    private T? DecryptPayload<T>(AccessCodeEntity row)
        => string.IsNullOrEmpty(row.PayloadEncrypted) ? default : JsonSerializer.Deserialize<T>(_keys.Decrypt(row.PayloadEncrypted));

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
