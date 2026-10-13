using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// accesscode 服务测试（T3——授权码：签发/消费/重放/PKCE；+ 方案 §5.5 安全数据投递：Payload/ExpectedClaimant/Peek/Redeem/清理）。
/// <para>原子 CAS 经 EntityUpdateWhereAsync（ADR89）——同 code 二次消费须拒（TICKET_CONSUMED）。</para>
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation.Tests SsoAccessCodeServiceTests 迁入——
/// <c>SsoAccessCodeService</c> → <see cref="AccessCodeService"/>，<c>ISsoAccessCodeService</c> → <see cref="IAccessCodeService"/>，
/// <c>SsoAccessCodeIssueRequest/... </c> → <c>AccessCodeIssueRequest/...</c>（去 Sso 前缀，方案 §5.8），
/// 命名空间 TKWF.Ext.TrustCenter.Tests。</para>
/// <para><b>Phase 4（2026-10-10，方案 §5.5 T6）</b>：<see cref="AccessCodeService"/> ctor 增 keyed
/// <see cref="ISymmetricKeyProvider"/>（键 <see cref="SymmetricKeyProviderKeys.TrustCenter"/>——测试宿主直构
/// <see cref="FileSymmetricKeyProvider"/>（对齐 SsoClientServiceTests 既有 keyed 形态，生产模式密钥文件已预置）。</para>
/// </summary>
public class AccessCodeServiceTests
{
    /// <summary>附带信息 JSON 测试载荷（安全数据投递通道）——无 PII 断言、无用途约定。</summary>
    private sealed record PayloadTest(int OrderId, string Sku);

    private static (AccessCodeService Service, AccessCodeEntityDataService Ds, ISymmetricKeyProvider Keys) CreateService(
        int? accessCodeRetentionDays = null)
    {
        var fsql = TrustCenterTestHost.CreateInMemoryFreeSql();
        var stub = TrustCenterTestHost.CreateStub(fsql);
        var options = TrustCenterTestHost.CreateOptions();
        if (accessCodeRetentionDays is { } days)
            options.AccessCodeRetentionDays = days;
        // 直构 FileSymmetricKeyProvider（对齐 DI 注册语义——生产模式密钥文件已预置，构造即加载前 32 字节）
        var keys = new FileSymmetricKeyProvider(options.SecretEncryptionKeyPath, options.IsProduction);
        var service = new AccessCodeService(stub, keys, Options.Create(options), NullLogger<AccessCodeService>.Instance);
        return (service, stub.Use<AccessCodeEntityDataService>(), keys);
    }

    [Fact]
    public async Task Issue_StoresHashOnly_ReturnsCode()
    {
        var (service, ds, _) = CreateService();
        var issued = await service.IssueAsync(new AccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", OpenId: "openid-xxx", IpAddress: "1.2.3.4"), default);

        Assert.NotNull(issued.Code);
        Assert.Equal(120, issued.ExpiresIn);

        // 落库只存 SHA256 hash——原文绝不出现在库中
        var row = await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default);
        Assert.NotNull(row);
        Assert.Equal("u-100", row!.UId);
        Assert.Equal("app-1", row.TargetAppId);
        Assert.Equal("mp-1", row.ChannelId);
        Assert.Null(row.CodeVerifierHash);   // 未传 verifier
    }

    [Fact]
    public async Task Consume_SingleUse_ReplayRejected()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(new AccessCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);

        var consumed = await service.ConsumeAsync(issued.Code, default);
        Assert.Equal("u-100", consumed.UId);
        Assert.Equal("app-1", consumed.TargetAppId);

        // 同 code 二次消费 → TICKET_CONSUMED（原子 CAS 重放拒）
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
    }

    [Fact]
    public async Task Consume_UnknownCode_Throws()
    {
        var (service, _, _) = CreateService();
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync("not-a-real-code", default));
    }

    [Fact]
    public async Task Consume_WithPkce_MissingVerifier_Rejected()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(new AccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        // 签发时有 verifier → 消费必传，缺失拒
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
    }

    [Fact]
    public async Task Consume_WithPkce_WrongVerifier_Rejected()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(new AccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, "wrong-verifier", default));
    }

    [Fact]
    public async Task Consume_WithPkce_CorrectVerifier_Succeeds()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(new AccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        var consumed = await service.ConsumeAsync(issued.Code, "verifier-abc", default);
        Assert.Equal("u-100", consumed.UId);
    }

    // ── Phase 4：安全数据投递通道（方案 §5.5——Issue(payload)/Peek/Redeem/ExpectedClaimant/容量/清理） ──

    /// <summary>附带信息签发 → Redeem 取回 payload + 销毁；二次 Redeem 拒（TICKET_CONSUMED——原子 CAS 销毁语义）。</summary>
    [Fact]
    public async Task IssueWithPayload_Redeem_ReturnsPayload_AndDestroys()
    {
        var (service, ds, _) = CreateService();
        var payload = new PayloadTest(12345, "sku-ABC");
        var payloadJson = JsonSerializer.Serialize(payload);

        var issued = await service.IssueAsync(payloadJson, TimeSpan.FromSeconds(120), expectedClaimant: null, default);
        Assert.NotNull(issued.Code);

        // 落库只存密文——明文绝不出现（AES-GCM 加密）
        var row = await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default);
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty(row!.PayloadEncrypted));
        Assert.DoesNotContain("sku-ABC", row.PayloadEncrypted);   // 明文不落库
        Assert.Null(row.ExpectedClaimant);                        // expectedClaimant=null → 可转让

        var redeemed = await service.RedeemAsync<PayloadTest>(issued.Code, claimant: null, default);
        Assert.Equal(new PayloadTest(12345, "sku-ABC"), redeemed);

        // 销毁语义——二次核销拒（重放）
        await Assert.ThrowsAsync<AuthenticationException>(() => service.RedeemAsync<PayloadTest>(issued.Code, null, default));
    }

    /// <summary>核销人不符拒（ExpectedClaimant="alice"，claimant="bob" → CLAIMANT_MISMATCH——原子 CAS 带条件断言）。</summary>
    [Fact]
    public async Task Redeem_ClaimantMismatch_Rejected()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(1, "s1")), TimeSpan.FromSeconds(120), "alice", default);

        // bob 核销 → CAS 条件（ExpectedClaimant=alice）不满足 → 重查判因 CLAIMANT_MISMATCH
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RedeemAsync<PayloadTest>(issued.Code, "bob", default));
        Assert.Contains("CLAIMANT_MISMATCH", ex.Message);

        // 行未被消费——正确核销人 alice 仍可核销（CAS 失败未置 Used）
        var redeemed = await service.RedeemAsync<PayloadTest>(issued.Code, "alice", default);
        Assert.Equal(new PayloadTest(1, "s1"), redeemed);
    }

    /// <summary>可转让语义：ExpectedClaimant=null → 任意核销人可核销（可转让）。</summary>
    [Fact]
    public async Task Redeem_Transferable_NullClaimant_Ok()
    {
        var (service, _, _) = CreateService();
        var issued = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(2, "s2")), TimeSpan.FromSeconds(120), expectedClaimant: null, default);

        // 未指定 expectedClaimant（可转让）——指定核销人可核销
        var redeemed = await service.RedeemAsync<PayloadTest>(issued.Code, "any-claimant", default);
        Assert.Equal(new PayloadTest(2, "s2"), redeemed);
    }

    /// <summary>附带信息超限拒（明文 > 4068 字节 → PAYLOAD_TOO_LARGE fail-hard P7）。</summary>
    [Fact]
    public async Task Issue_PayloadTooLarge_Throws()
    {
        var (service, _, _) = CreateService();
        var big = new string('x', 4069);   // ASCII 1 byte/char → 4069 字节 > 4068

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.IssueAsync(big, TimeSpan.FromSeconds(120), null, default));
        Assert.Contains("PAYLOAD_TOO_LARGE", ex.Message);
    }

    /// <summary>Peek 只读快照非锁定：Peek 取回 payload 后行仍可 Redeem（不消费）；并发下 Peek 后行可能已用（业务层不可基于 Peek 做不可逆决策）。</summary>
    [Fact]
    public async Task Peek_Snapshot_DoesNotConsume()
    {
        var (service, ds, _) = CreateService();
        var issued = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(3, "s3")), TimeSpan.FromSeconds(120), null, default);

        var peeked = await service.PeekAsync<PayloadTest>(issued.Code, default);
        Assert.Equal(new PayloadTest(3, "s3"), peeked);

        // 快照非锁定——Peek 后行仍未被消费（Used=false）且可 Redeem
        var row = await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default);
        Assert.NotNull(row);
        Assert.False(row!.Used);

        var redeemed = await service.RedeemAsync<PayloadTest>(issued.Code, null, default);
        Assert.Equal(new PayloadTest(3, "s3"), redeemed);
    }

    /// <summary>清理任务：过期行（含 Payload 密文）按 RetentionDays 删除；未过期不删；≤0 跳过。</summary>
    [Fact]
    public async Task CleanupExpired_RemovesStaleRows_KeepsActive()
    {
        var (service, ds, _) = CreateService();
        var issued1 = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(4, "s4")), TimeSpan.FromSeconds(1), null, default);
        var issued2 = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(5, "s5")), TimeSpan.FromSeconds(1), null, default);

        // 直接落库把两行 ExpiresAt 改为过期（模拟老化——Redeem 无副作用，清理任务只查 expired 行）
        var old = DateTime.UtcNow.AddDays(-30);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued1.Code), m => new { ExpiresAt = old }, default);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued2.Code), m => new { ExpiresAt = old }, default);

        // 未过期行（新签发仍有效）不受影响
        var issued3 = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(6, "s6")), TimeSpan.FromSeconds(300), null, default);

        var deleted = await service.CleanupExpiredAsync(7, default);
        Assert.Equal(2, deleted);

        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued1.Code), default));
        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued2.Code), default));
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued3.Code), default));
    }

    /// <summary>清理默认保留天数取 Options（AccessCodeRetentionDays）——无参数调用经配置。</summary>
    [Fact]
    public async Task CleanupExpired_UsesConfiguredRetentionDays()
    {
        var (service, ds, _) = CreateService(accessCodeRetentionDays: 0);
        var issued = await service.IssueAsync(JsonSerializer.Serialize(new PayloadTest(7, "s7")), TimeSpan.FromSeconds(1), null, default);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), m => new { ExpiresAt = DateTime.UtcNow.AddDays(-2) }, default);

        // retention=0 → 跳过清理（返回 0，不删除）
        Assert.Equal(0, await service.CleanupExpiredAsync(retentionDays: null, default));
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default));
    }

    // ── E2：ISealedTicketService<FederatedIdentity> DB 后端适配（外部身份联邦与会话绑定守卫-开发方案 §五 E2 + ADR-Federation E2） ──

    /// <summary>E2 测试用联邦身份（全字段——含 Extensions 字典；FederatedIdentity = 框架 Core record）。</summary>
    private static FederatedIdentity FedIdentity()
        => new(
            UId: "u-fed-100",
            ChannelId: "ch-wechat",
            ExternalUserId: "openid-fed-100",
            ChannelType: "wechat_oauth",
            AuthLevel: 2,
            Extensions: new Dictionary<string, string?>
            {
                ["role"] = "admin",
                ["nickname"] = "alice",
            });

    /// <summary>密封票据往返——FederatedIdentity → JSON → AES-GCM 落库 → Redeem 解密反序列化，全字段一致。
    /// Extensions 字典逐项断言（record 等值比较对字典按引用比较，不做整对象 Equal）。</summary>
    [Fact]
    public async Task SealedTicket_IssueRedeem_RoundTrip_ReturnsFullFederatedIdentity()
    {
        var (service, _, _) = CreateService();
        ISealedTicketService<FederatedIdentity> sealedService = service;
        var identity = FedIdentity();

        var issued = await sealedService.IssueAsync(identity, TimeSpan.FromSeconds(120));
        Assert.False(string.IsNullOrWhiteSpace(issued.Ticket));
        Assert.Equal(120, issued.ExpiresIn);

        var redeemed = await sealedService.RedeemAsync(issued.Ticket);
        Assert.NotNull(redeemed);
        Assert.Equal(identity.UId, redeemed!.UId);
        Assert.Equal(identity.ChannelId, redeemed.ChannelId);
        Assert.Equal(identity.ExternalUserId, redeemed.ExternalUserId);
        Assert.Equal(identity.ChannelType, redeemed.ChannelType);
        Assert.Equal(identity.AuthLevel, redeemed.AuthLevel);
        Assert.NotNull(redeemed.Extensions);
        Assert.Equal(identity.Extensions!.Count, redeemed.Extensions.Count);
        Assert.Equal("admin", redeemed.Extensions["role"]);
        Assert.Equal("alice", redeemed.Extensions["nickname"]);
    }

    /// <summary>防重放——同票二次兑换 null（密封契约"失败/已用返回 null"：底层 DB CAS TICKET_CONSUMED → 适配层映射 null）。</summary>
    [Fact]
    public async Task SealedTicket_SecondRedeem_ReturnsNull_ReplayProtected()
    {
        var (service, _, _) = CreateService();
        ISealedTicketService<FederatedIdentity> sealedService = service;

        var issued = await sealedService.IssueAsync(FedIdentity(), TimeSpan.FromSeconds(120));

        var first = await sealedService.RedeemAsync(issued.Ticket);
        Assert.NotNull(first);

        var second = await sealedService.RedeemAsync(issued.Ticket);
        Assert.Null(second);
    }

    /// <summary>ttl 过期——过期票据兑换 null（对齐 CleanupExpired 测试既有确定性老化模式：短 TTL 落库后直接老化 ExpiresAt）。</summary>
    [Fact]
    public async Task SealedTicket_Expired_ReturnsNull()
    {
        var (service, ds, _) = CreateService();
        ISealedTicketService<FederatedIdentity> sealedService = service;

        // 短 TTL（1s——底层钳制 Math.Max(TotalSeconds, 1)）
        var issued = await sealedService.IssueAsync(FedIdentity(), TimeSpan.FromSeconds(1));
        // 确定性老化：直接落库把 ExpiresAt 改为过去（避免真实睡眠——对齐 CleanupExpired_RemovesStaleRows 模式）
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == Token2Service.Sha256Hex(issued.Ticket),
            m => new { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
            default);

        Assert.Null(await sealedService.RedeemAsync(issued.Ticket));
    }

    /// <summary>ExpectedClaimant 未用（options null）——票据可转让：未指定核销人，任意核销人可兑换。</summary>
    [Fact]
    public async Task SealedTicket_OptionsNull_Transferable()
    {
        var (service, _, _) = CreateService();
        ISealedTicketService<FederatedIdentity> sealedService = service;

        var issued = await sealedService.IssueAsync(FedIdentity(), TimeSpan.FromSeconds(120), options: null);
        Assert.False(string.IsNullOrWhiteSpace(issued.Ticket));

        var redeemed = await sealedService.RedeemAsync(issued.Ticket);
        Assert.NotNull(redeemed);
        Assert.Equal("u-fed-100", redeemed!.UId);
    }

    /// <summary>清理适配——ISealedTicketService.CleanupExpiredAsync() 委托既有默认保留天数（retentionDays: null）。</summary>
    [Fact]
    public async Task SealedTicket_CleanupExpired_UsesDefaultRetention()
    {
        var (service, ds, _) = CreateService();
        ISealedTicketService<FederatedIdentity> sealedService = service;

        var issued = await sealedService.IssueAsync(FedIdentity(), TimeSpan.FromSeconds(120));
        // 老化（30 天前——默认 RetentionDays=7 删除范围）
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == Token2Service.Sha256Hex(issued.Ticket),
            m => new { ExpiresAt = DateTime.UtcNow.AddDays(-30) },
            default);

        Assert.Equal(1, await sealedService.CleanupExpiredAsync());
        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Ticket), default));
    }
}
