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
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// SealedTicket service (Face 1: sealed ticket / secure data delivery - split rename 2026-10-16, ADR109 D4).
/// Implements ISealedTicketService{T} DB CAS backend: Issue = INSERT / Redeem = CAS / Cleanup = DELETE.
/// V4.10.79 (ADR110 有界配额 2026-10-17): MaxRedeems=1 单次 / &gt;1 多次——Redeem 原子 UPDATE
/// (SET UsedCount=UsedCount+1 WHERE UsedCount&lt;MaxRedeems)，并发恰 N 次成功，耗尽后返回 null；
/// Cleanup 判据含耗尽（UsedCount&gt;=MaxRedeems）。
/// Data access: SealedTicketEntityDataService internal forwarders (Entity*).
/// PeekAsync is public but NOT on the contract (Oracle security ruling - not sealed ticket model compliant).
/// </summary>
[DiContractIgnore]
internal sealed class SealedTicketService<T> : DomainServiceBase, ISealedTicketService<T>
    where T : class
{
    private const int MaxPayloadBytes = 4096 - 12 - 16;

    private SealedTicketEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;
    private readonly IOptions<TrustCenterOptions> _options;
    private readonly ILogger<SealedTicketService<T>> _logger;

    private SealedTicketEntityDataService DataService => _dataService ??= User.Use<SealedTicketEntityDataService>();

    public SealedTicketService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider keys,
        IOptions<TrustCenterOptions> options,
        ILogger<SealedTicketService<T>> logger)
        : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SealedTicketResult> IssueAsync(
        T payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        // V4.10.79 ADR110 守卫：MaxRedeems < 1 抛（0 不解释为无限，无限 = 显式大值——对齐 InMemory 后端语义）
        if (options?.MaxRedeems is < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRedeems must be >= 1 (0 is not infinite; use an explicit large value, ADR110)");
        var ticket = NewTicketCode();
        var config = _options.Value;
        var expiresIn = ttl <= TimeSpan.Zero
            ? Math.Max(config.SealedTicketExpirationSeconds, 1)
            : Math.Max((int)ttl.TotalSeconds, 1);
        var now = DateTime.UtcNow;
        var payloadJson = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
            throw new AuthenticationException("PAYLOAD_TOO_LARGE");
        var payloadEncrypted = _keys.Encrypt(payloadJson);
        var entity = new SealedTicketEntity
        {
            CodeHash = Sha256Hex(ticket),
            ExpectedClaimant = !string.IsNullOrWhiteSpace(options?.ExpectedClaimant) ? options.ExpectedClaimant : null,
            PayloadEncrypted = payloadEncrypted,
            ExpiresAt = now.AddSeconds(expiresIn),
            UsedCount = 0,
            MaxRedeems = options?.MaxRedeems ?? 1,
            CreateTime = now,
            UpdateTime = now,
        };
        await DataService.EntityCreateAsync(entity, ct);
        return new SealedTicketResult(ticket, expiresIn);
    }

    public async Task<T?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
    {
        // 契约语义（ISealedTicketService<T> XML）：成功返回载荷，失败/过期/耗尽/核销人不符返回 null——不向票据消费方泄露失败原因码。
        if (string.IsNullOrWhiteSpace(ticket))
            return default;
        var claimant = context?.ExpectedClaimant;
        var now = DateTime.UtcNow;
        var row = await DataService.EntityGetAsync(m => m.CodeHash == Sha256Hex(ticket), ct);
        if (row == null)
            return default;
        if (row.ExpiresAt <= now)
            return default;
        // V4.10.79 ADR110 原子逐次消费：SET UsedCount=UsedCount+1 WHERE UsedCount<MaxRedeems（并发恰 N 次成功，TOCTOU 消除）
        var affected = await DataService.EntityUpdateWhereAsync(
            m => m.CodeHash == row.CodeHash && m.UsedCount < m.MaxRedeems && (m.ExpectedClaimant == null || m.ExpectedClaimant == claimant),
            m => new { UsedCount = m.UsedCount + 1, UpdateTime = DateTime.UtcNow },
            ct);
        if (affected == 0)
        {
            var fresh = await DataService.EntityGetAsync(m => m.CodeHash == row.CodeHash, ct);
            if (fresh == null)
                return default;
            if (fresh.UsedCount >= fresh.MaxRedeems)
                return default;   // 配额耗尽（重放或并发竞争败者）
            if (!string.IsNullOrEmpty(fresh.ExpectedClaimant) && fresh.ExpectedClaimant != claimant)
                return default;   // CAS compare 失败不 swap——票据保留，合法核销人仍可兑换（对齐 InMemory 后端语义）
            if (fresh.ExpiresAt <= DateTime.UtcNow)
                return default;
            _logger.LogWarning("Sealed ticket CAS fail no-cause (codeHash={CodeHash}) - fallback null", row.CodeHash);
            return default;
        }
        return DecryptPayload<T>(row);
    }

    public async Task<T?> PeekAsync(string ticket, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        var row = await DataService.EntityGetAsync(m => m.CodeHash == Sha256Hex(ticket), ct);
        if (row == null)
            throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        if (row.ExpiresAt <= DateTime.UtcNow)
            throw new AuthenticationException("TICKET_EXPIRED");
        // V4.10.79 ADR110：配额耗尽票据不可再兑换——Peek 判据与 Redeem 对齐（耗尽 = 已用尽）
        if (row.UsedCount >= row.MaxRedeems)
            throw new AuthenticationException("TICKET_CONSUMED");
        return DecryptPayload<T>(row);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var days = _options.Value.SealedTicketRetentionDays;
        if (days <= 0)
        {
            _logger.LogDebug("Sealed ticket cleanup skip - retention <= 0 (retentionDays={RetentionDays})", days);
            return 0;
        }
        var cutoff = DateTime.UtcNow.AddDays(-days);
        // V4.10.79 ADR110：清理判据 = 过期 OR 已耗尽（UsedCount>=MaxRedeems）——统一清理不可再兑换条目
        var stale = await DataService.EntitySelectAsync(
            m => m.ExpiresAt < cutoff || m.UsedCount >= m.MaxRedeems, 0, 100_000, null, ct);
        if (stale.Count == 0)
        {
            _logger.LogDebug("Sealed ticket cleanup - no stale rows (cutoff={Cutoff:O})", cutoff);
            return 0;
        }
        var deleted = await DataService.EntityDeleteBatchAsync(stale.Select(s => s.Id), ct);
        _logger.LogInformation("Sealed ticket cleanup - deleted {Deleted} stale rows (cutoff={Cutoff:O})", deleted, cutoff);
        return deleted;
    }

    private TPayload? DecryptPayload<TPayload>(SealedTicketEntity row)
        => string.IsNullOrEmpty(row.PayloadEncrypted) ? default : JsonSerializer.Deserialize<TPayload>(_keys.Decrypt(row.PayloadEncrypted));

    private static string NewTicketCode()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Sha256Hex(string input)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}