using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.SmsCode;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// 验证码记录数据服务测试（方案 §4.1 只增语义 + §4.2 单次消费/作废机制落点）——
/// 生产 NoAop 路径：<c>user.Use&lt;SmsCodeRecordEntityDataService&gt;()</c> 经真实 DI + SQLite 文件库驱动。
/// <para>覆盖：CreateAsync（落库 + CodeHash 单向散列语义由调用方保证——列承载）、GetLatestUnverifiedAsync
/// （subject+scene 精确 + CreateTime 降序取最新 + 不过滤过期——过期由门面区分 NotFound/Expired）、
/// CountSentAsync（只增窗口 COUNT）、CountActiveAsync（未消费未过期）、MarkVerifiedAsync（CAS 单次消费）、
/// IncrementFailedAttemptsAsync（失败计数 CAS 自增，null=0 语义）、InvalidateActiveAsync（重发作废旧码 CAS）。
/// ⚠️[FACADE-M3]：这些原子方法即 M3 门面的频控/作废数据源——门面未落地前直测原子语义。</para>
/// </summary>
public class SmsCodeRecordDataServiceTests
{
    private static DateTime UtcNow => DateTime.UtcNow;

    private static SmsCodeRecordEntity NewRecord(string subject, string scene, string codeHash = "hash", DateTime? expireAt = null, string? ip = null)
        => new()
        {
            Subject = subject,
            DeliveryTarget = subject,          // v1 投递恒=主体（手机号）
            Scene = scene,
            CodeHash = codeHash,
            ExpireAt = expireAt ?? UtcNow.AddMinutes(5),
            IpAddress = ip ?? "127.0.0.1",
        };

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsRecord_WithPopulatedId()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000001";
        var entity = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login), ct: TestContext.Current.CancellationToken);

        Assert.True(entity.Id > 0);
        Assert.Equal(subject, entity.Subject);
        Assert.Equal(SmsScenes.Login, entity.Scene);
        Assert.Equal("hash", entity.CodeHash);
        Assert.False(entity.IsVerified);
        Assert.Null(entity.FailedAttempts);

        // 真实落库读回（共享单 UoW 同库——WeChatChannels 断言先例）
        var row = host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == entity.Id).First();
        Assert.Equal(subject, row.Subject);
    }

    // ── GetLatestUnverifiedAsync ────────────────────────────────────

    [Fact]
    public async Task GetLatestUnverified_FiltersSubjectScene_ReturnsLatestByCreateTime()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000002";
        var otherSubject = "13800000099";
        var ct = TestContext.Current.CancellationToken;

        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-1"), ct: ct);
        await host.Codes.CreateAsync(NewRecord(subject, "other-scene", "hash-x"), ct: ct);   // 场景隔离
        await host.Codes.CreateAsync(NewRecord(otherSubject, SmsScenes.Login, "hash-y"), ct: ct); // 主体隔离
        var newest = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-2"), ct: ct);

        var latest = await host.Codes.GetLatestUnverifiedAsync(subject, SmsScenes.Login, ct);
        Assert.NotNull(latest);
        Assert.Equal(newest.Id, latest.Id);
        Assert.Equal("hash-2", latest.CodeHash);
    }

    [Fact]
    public async Task GetLatestUnverified_ExcludesVerified_ButNotExpired()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000003";
        var ct = TestContext.Current.CancellationToken;

        // 已消费旧码
        var consumed = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-consumed"), ct: ct);
        await host.Codes.MarkVerifiedAsync(consumed.Id, ct);
        // 过期但未消费——GetLatestUnverified 不过滤过期（过期与否由门面判定区分 NotFound/Expired 语义）
        var expired = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-expired", expireAt: UtcNow.AddSeconds(-1)), ct: ct);

        var latest = await host.Codes.GetLatestUnverifiedAsync(subject, SmsScenes.Login, ct);
        Assert.NotNull(latest);
        Assert.Equal(expired.Id, latest.Id);   // 未消费（含过期）中取最新
        Assert.Equal("hash-expired", latest.CodeHash);
    }

    // ── CountSentAsync（只增窗口 COUNT——subject 维日/小时频控数据源） ──

    [Fact]
    public async Task CountSent_WindowCounting_IncludesOnlyWindowRows()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000004";
        var ct = TestContext.Current.CancellationToken;

        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-1"), ct: ct);           // 窗口内（now-5min）
        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-2"), ct: ct);
        // 窗口外（now-2h）
        host.Fsql.Insert(new SmsCodeRecordEntity
        {
            Subject = subject,
            Scene = SmsScenes.Login,
            CodeHash = "hash-old",
            ExpireAt = UtcNow.AddMinutes(5),
            CreateTime = UtcNow.AddHours(-2),
        }).ExecuteAffrows();

        Assert.Equal(2, await host.Codes.CountSentAsync(subject, SmsScenes.Login, UtcNow.AddHours(-1), ct));
        Assert.Equal(3, await host.Codes.CountSentAsync(subject, SmsScenes.Login, UtcNow.AddDays(-1), ct));
    }

    // ── CountActiveAsync（未消费未过期——MaxActiveCodesPerSubject 数据源） ──

    [Fact]
    public async Task CountActive_OnlyUnverifiedAndUnexpired()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000005";
        var ct = TestContext.Current.CancellationToken;

        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-active"), ct: ct);
        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-expired", expireAt: UtcNow.AddSeconds(-1)), ct: ct);
        var consumed = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-consumed"), ct: ct);
        await host.Codes.MarkVerifiedAsync(consumed.Id, ct);

        Assert.Equal(1, await host.Codes.CountActiveAsync(subject, SmsScenes.Login, ct));
        Assert.Equal(0, await host.Codes.CountActiveAsync(subject, "other-scene", ct));
    }

    // ── MarkVerifiedAsync（单次消费 CAS） ─────────────────────────────

    [Fact]
    public async Task MarkVerified_SingleConsumeCas_SecondCallFails()
    {
        using var host = SmsCodeTestHost.Create();
        var ct = TestContext.Current.CancellationToken;
        var entity = await host.Codes.CreateAsync(NewRecord("13800000006", SmsScenes.Login), ct: ct);

        Assert.True(await host.Codes.MarkVerifiedAsync(entity.Id, ct));
        // 已消费——CAS 失败（单次消费）
        Assert.False(await host.Codes.MarkVerifiedAsync(entity.Id, ct));

        var row = host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == entity.Id).First();
        Assert.True(row.IsVerified);
    }

    [Fact]
    public async Task MarkVerified_NonexistentId_ReturnsFalse()
    {
        using var host = SmsCodeTestHost.Create();
        Assert.False(await host.Codes.MarkVerifiedAsync(999_999, TestContext.Current.CancellationToken));
    }

    // ── IncrementFailedAttemptsAsync（失败计数 CAS 自增，null=0 语义） ──

    [Fact]
    public async Task IncrementFailedAttempts_CasIncrement_FromNull()
    {
        using var host = SmsCodeTestHost.Create();
        var ct = TestContext.Current.CancellationToken;
        var entity = await host.Codes.CreateAsync(NewRecord("13800000007", SmsScenes.Login), ct: ct);

        Assert.Equal(1, await host.Codes.IncrementFailedAttemptsAsync(entity.Id, ct));
        Assert.Equal(1, await host.Codes.IncrementFailedAttemptsAsync(entity.Id, ct));

        var row = host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == entity.Id).First();
        Assert.Equal(2, row.FailedAttempts);
    }

    [Fact]
    public async Task IncrementFailedAttempts_ConsumedRecord_NoOp()
    {
        using var host = SmsCodeTestHost.Create();
        var ct = TestContext.Current.CancellationToken;
        var entity = await host.Codes.CreateAsync(NewRecord("13800000008", SmsScenes.Login), ct: ct);
        await host.Codes.MarkVerifiedAsync(entity.Id, ct);

        Assert.Equal(0, await host.Codes.IncrementFailedAttemptsAsync(entity.Id, ct));

        var row = host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == entity.Id).First();
        Assert.Null(row.FailedAttempts);
    }

    // ── InvalidateActiveAsync（重发作废旧码 CAS——方案 §4.1 M3 闭环） ──

    [Fact]
    public async Task InvalidateActive_InvalidatesActiveUnverified_KeepsConsumed()
    {
        using var host = SmsCodeTestHost.Create();
        var subject = "13800000009";
        var ct = TestContext.Current.CancellationToken;

        var a = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-a"), ct: ct);
        var b = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-b"), ct: ct);
        await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-expired", expireAt: UtcNow.AddSeconds(-1)), ct: ct);
        var consumed = await host.Codes.CreateAsync(NewRecord(subject, SmsScenes.Login, "hash-consumed"), ct: ct);
        await host.Codes.MarkVerifiedAsync(consumed.Id, ct);

        var invalidated = await host.Codes.InvalidateActiveAsync(subject, SmsScenes.Login, ct);
        Assert.Equal(2, invalidated);   // a + b（过期/已消费不参与）

        // 全部旧码失效 → CountActive 归零（活跃码上限防膨胀）
        Assert.Equal(0, await host.Codes.CountActiveAsync(subject, SmsScenes.Login, ct));
        Assert.True(host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == a.Id).First().ExpireAt <= UtcNow);
        Assert.True(host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == b.Id).First().ExpireAt <= UtcNow);
        Assert.True(host.Fsql.Select<SmsCodeRecordEntity>().Where(e => e.Id == consumed.Id).First().ExpireAt > UtcNow.AddMinutes(4)); // 已消费记录不受影响
    }
}