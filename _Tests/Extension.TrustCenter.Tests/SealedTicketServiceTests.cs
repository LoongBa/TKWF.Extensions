using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// SealedTicket 专项测试（面①——2026-10-16 拆分命名 ADR109 D4：<see cref="SealedTicketService{T}"/> DB CAS 后端，
/// 表 TKWF_SealedTicket）。
/// <para>契约语义对齐（ISealedTicketService{T} XML + InMemory 后端）：成功返回载荷，失败/过期/已用/核销人不符
/// 返回 null（不向票据消费方泄露失败原因码）；核销人不符 = CAS compare 失败不 swap——票据保留，合法核销人仍可兑换。</para>
/// </summary>
public class SealedTicketServiceTests
{
    /// <summary>密封票据载荷测试 record（任意业务载荷形态）。</summary>
    private sealed record PayloadTest(int OrderId, string Sku, string? Note = null);

    private static (SealedTicketService<PayloadTest> Service, SealedTicketEntityDataService Ds) CreateService(
        int? sealedTicketRetentionDays = null)
    {
        var fsql = TrustCenterTestHost.CreateInMemoryFreeSql();
        var stub = TrustCenterTestHost.CreateStub(fsql);
        var options = TrustCenterTestHost.CreateOptions();
        if (sealedTicketRetentionDays is { } days)
            options.SealedTicketRetentionDays = days;
        var keys = new FileSymmetricKeyProvider(options.SecretEncryptionKeyPath, options.IsProduction);
        var service = new SealedTicketService<PayloadTest>(
            stub, keys, Options.Create(options), NullLogger<SealedTicketService<PayloadTest>>.Instance);
        return (service, stub.Use<SealedTicketEntityDataService>());
    }

    [Fact]
    public async Task Issue_Redeem_RoundTrip_ReturnsFullPayload()
    {
        var (service, _) = CreateService();
        var payload = new PayloadTest(12345, "sku-ABC", "note-1");

        var issued = await service.IssueAsync(payload, TimeSpan.FromSeconds(120));
        Assert.False(string.IsNullOrWhiteSpace(issued.Ticket));
        Assert.Equal(120, issued.ExpiresIn);

        var redeemed = await service.RedeemAsync(issued.Ticket);
        Assert.Equal(payload, redeemed);
    }

    [Fact]
    public async Task Issue_StoresHashOnly_AndEncryptedPayload()
    {
        var (service, ds) = CreateService();
        var payload = new PayloadTest(7, "sku-SECRET");

        var issued = await service.IssueAsync(payload, TimeSpan.FromSeconds(120));

        // 落库只存 SHA256(ticket)——原文绝不出现；载荷 AES-GCM 密文——明文不落库
        var row = await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket), default);
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty(row!.PayloadEncrypted));
        Assert.DoesNotContain("sku-SECRET", row.PayloadEncrypted);
        Assert.DoesNotContain(issued.Ticket, row.CodeHash);
    }

    [Fact]
    public async Task Redeem_SingleUse_SecondReturnsNull_ReplayProtected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new PayloadTest(1, "s1"), TimeSpan.FromSeconds(120));

        Assert.NotNull(await service.RedeemAsync(issued.Ticket));

        // 重放——契约语义返回 null（防重放，原子 CAS 恰一成功）
        Assert.Null(await service.RedeemAsync(issued.Ticket));
    }

    [Fact]
    public async Task Redeem_UnknownTicket_ReturnsNull()
    {
        var (service, _) = CreateService();
        Assert.Null(await service.RedeemAsync("not-a-real-ticket"));
    }

    [Fact]
    public async Task Redeem_Expired_ReturnsNull()
    {
        var (service, ds) = CreateService();
        var issued = await service.IssueAsync(new PayloadTest(2, "s2"), TimeSpan.FromSeconds(1));

        // 确定性老化：直接落库把 ExpiresAt 改为过去（对齐 SealedTicket 清理/老化既有模式）
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket),
            m => new { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
            default);

        Assert.Null(await service.RedeemAsync(issued.Ticket));
    }

    [Fact]
    public async Task Redeem_ClaimantMismatch_ReturnsNull_AndTicketPreserved()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(
            new PayloadTest(3, "s3"),
            TimeSpan.FromSeconds(120),
            new SealedTicketOptions(ExpectedClaimant: "alice"));

        // bob 兑换（核销人不符）→ null；CAS compare 失败不 swap——票据保留
        Assert.Null(await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "bob")));

        // 合法核销人 alice 仍可兑换（票据未被错误消费）
        var redeemed = await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "alice"));
        Assert.Equal(new PayloadTest(3, "s3"), redeemed);
    }

    [Fact]
    public async Task Redeem_Transferable_NullClaimant_Ok()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new PayloadTest(4, "s4"), TimeSpan.FromSeconds(120));

        // 未指定 ExpectedClaimant（可转让）——任意核销人可兑换；context 可传可不传
        Assert.NotNull(await service.RedeemAsync(issued.Ticket));
    }

    [Fact]
    public async Task Issue_MaxRedeemsZeroOrNegative_Throws()
    {
        var (service, _) = CreateService();
        var payload = new PayloadTest(41, "s41");

        // V4.10.79 ADR110 守卫：MaxRedeems < 1 抛（0 不解释为无限——对齐 InMemory 后端语义）
        foreach (var bad in new[] { 0, -1, int.MinValue })
        {
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.IssueAsync(payload, TimeSpan.FromSeconds(120), new SealedTicketOptions(MaxRedeems: bad)));
            Assert.Equal("options", ex.ParamName);
        }
    }

    [Fact]
    public async Task Redeem_MaxRedeemsMulti_BoundedConsumption_UntilExhausted()
    {
        var (service, ds) = CreateService();
        var payload = new PayloadTest(42, "s42");
        var issued = await service.IssueAsync(
            payload, TimeSpan.FromSeconds(120), new SealedTicketOptions(MaxRedeems: 3));

        // 有界配额 3：并发/顺序消费恰 3 次成功，第 4 次耗尽返回 null（对齐 InMemory 后端语义）
        Assert.Equal(payload, await service.RedeemAsync(issued.Ticket));
        Assert.Equal(payload, await service.RedeemAsync(issued.Ticket));
        Assert.Equal(payload, await service.RedeemAsync(issued.Ticket));

        Assert.Null(await service.RedeemAsync(issued.Ticket));   // 耗尽

        var row = await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket), default);
        Assert.NotNull(row);
        Assert.Equal(3, row!.UsedCount);
        Assert.Equal(3, row.MaxRedeems);
    }

    [Fact]
    public async Task Redeem_MaxRedeemsDefaultOne_SingleConsumption()
    {
        var (service, ds) = CreateService();
        var issued = await service.IssueAsync(new PayloadTest(43, "s43"), TimeSpan.FromSeconds(120));

        // 默认 MaxRedeems=1：单次消费（ADR107/109 语义零变更）——第二次耗尽返回 null
        Assert.NotNull(await service.RedeemAsync(issued.Ticket));
        Assert.Null(await service.RedeemAsync(issued.Ticket));

        var row = await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket), default);
        Assert.NotNull(row);
        Assert.Equal(1, row!.MaxRedeems);
        Assert.Equal(1, row.UsedCount);
    }

    [Fact]
    public async Task Redeem_MaxRedeemsMulti_ClaimantMismatchDoesNotBurnQuota()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(
            new PayloadTest(44, "s44"),
            TimeSpan.FromSeconds(120),
            new SealedTicketOptions(ExpectedClaimant: "alice", MaxRedeems: 2));

        // 错误核销人不烧槽位（CAS compare 失败不 swap——对齐 InMemory 后端 claimant 前置 CAS 语义）
        Assert.Null(await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "bob")));

        // 合法核销人 2 次均成功（配额 2 未被 bob 消耗）
        Assert.Equal(new PayloadTest(44, "s44"), await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "alice")));
        Assert.Equal(new PayloadTest(44, "s44"), await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "alice")));
        Assert.Null(await service.RedeemAsync(issued.Ticket, new TicketRedeemContext(ExpectedClaimant: "alice")));
    }

    [Fact]
    public async Task CleanupExpired_RemovesExhaustedRows_KeepsUsable()
    {
        var (service, ds) = CreateService();
        // 耗尽行（配额 1 消费完）——新 TTL 未过期也应收割（V4.10.79 ADR110：Cleanup 判据 = 过期 OR 耗尽）
        var exhausted = await service.IssueAsync(new PayloadTest(45, "s45"), TimeSpan.FromSeconds(300), new SealedTicketOptions(MaxRedeems: 1));
        Assert.NotNull(await service.RedeemAsync(exhausted.Ticket));

        // 未耗尽未过期行不受影响
        var usable = await service.IssueAsync(new PayloadTest(46, "s46"), TimeSpan.FromSeconds(300), new SealedTicketOptions(MaxRedeems: 2));

        var deleted = await service.CleanupExpiredAsync();
        Assert.Equal(1, deleted);

        // 耗尽行已删，可用行保留
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => service.PeekAsync(exhausted.Ticket));
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(usable.Ticket), default));
    }

    [Fact]
    public async Task Issue_PayloadTooLarge_Throws()
    {
        var (service, _) = CreateService();
        var big = new PayloadTest(int.MaxValue, new string('x', 4069));   // ASCII 1 byte/char → 明文 > 4068

        var ex = await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => service.IssueAsync(big, TimeSpan.FromSeconds(120)));
        Assert.Contains("PAYLOAD_TOO_LARGE", ex.Message);
    }

    [Fact]
    public async Task CleanupExpired_RemovesStaleRows_KeepsActive()
    {
        var (service, ds) = CreateService();
        var issued1 = await service.IssueAsync(new PayloadTest(5, "s5"), TimeSpan.FromSeconds(1));
        var issued2 = await service.IssueAsync(new PayloadTest(6, "s6"), TimeSpan.FromSeconds(1));

        var old = DateTime.UtcNow.AddDays(-30);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued1.Ticket), m => new { ExpiresAt = old }, default);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued2.Ticket), m => new { ExpiresAt = old }, default);

        // 未过期行（新签发仍有效）不受影响
        var issued3 = await service.IssueAsync(new PayloadTest(7, "s7"), TimeSpan.FromSeconds(300));

        var deleted = await service.CleanupExpiredAsync();
        Assert.Equal(2, deleted);

        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued1.Ticket), default));
        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued2.Ticket), default));
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued3.Ticket), default));
    }

    [Fact]
    public async Task CleanupExpired_UsesConfiguredRetentionDays_ZeroSkips()
    {
        var (service, ds) = CreateService(sealedTicketRetentionDays: 0);
        var issued = await service.IssueAsync(new PayloadTest(8, "s8"), TimeSpan.FromSeconds(1));
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket),
            m => new { ExpiresAt = DateTime.UtcNow.AddDays(-2) },
            default);

        // retention=0 → 跳过清理（返回 0，不删除）
        Assert.Equal(0, await service.CleanupExpiredAsync());
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == SealedTicketServiceTests.Sha256Hex(issued.Ticket), default));
    }

    /// <summary>SHA256 hex（对齐 Token2Service.Sha256Hex 语义——服务内部私有，测试侧独立实现）。</summary>
    internal static string Sha256Hex(string input)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
