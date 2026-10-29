using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// D03 1.10 运营配置面测试——管理门面（5 域 GET/PUT + 乐观并发 R7）+ 只读查询（FA 契约）。
/// <para>生产路径：<c>user.Use&lt;IOperationsConfigService&gt;()</c> / <c>user.Use&lt;IOperationsConfigQuery&gt;()</c> AOP</para>
/// <para>单行配置表约定：Id=1；无行 = 未配置 → Get/Query null；保存后 Version 从 1 起递增。</para>
/// </summary>
public class OperationsConfigServiceTests : WeChatOperationsTestHost
{
    // ═══════════════════ 未配置（无行）→ null ═══════════════════

    [Fact]
    public async Task Get_AllDomains_NoRow_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        Assert.Null(await service.GetArticleReplyAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.GetReplyTextAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.GetDegradeAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.GetWeakIdentityAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.GetTicketAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_All_NoRow_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var query = user.Use<IOperationsConfigQuery>();

        Assert.Null(await query.GetArticleReplyUrlTemplateAsync(TestContext.Current.CancellationToken));
        Assert.Null(await query.GetReplyTextConfigAsync(TestContext.Current.CancellationToken));
        Assert.Null(await query.GetUnknownChannelReplyEnabledAsync(TestContext.Current.CancellationToken));
        Assert.Null(await query.GetTicketConfigAsync(TestContext.Current.CancellationToken));
    }

    // ═══════════════════ 保存创建 + 回读 ═══════════════════

    [Fact]
    public async Task Save_ArticleReply_Create_ThenGet_AndQueryMatch()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        var version = await service.SaveArticleReplyAsync(new SaveOpsConfigArticleReplyRequest(
            UrlTemplate: "https://m.loongba.cn/{mid}/auth?t={ticket}", ExpectedVersion: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, version);

        var dto = await service.GetArticleReplyAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Equal("https://m.loongba.cn/{mid}/auth?t={ticket}", dto!.UrlTemplate);
        Assert.Equal(1, dto.Version);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
        Assert.Null(dto.UpdatedBy); // 宿主未认证（IsAuthenticated=false）→ 系统初始化语义：UpdatedBy 为空

        var query = user.Use<IOperationsConfigQuery>();
        Assert.Equal("https://m.loongba.cn/{mid}/auth?t={ticket}",
            await query.GetArticleReplyUrlTemplateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_Ticket_Create_ThenGet_AndQueryMatch()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        var version = await service.SaveTicketAsync(new SaveOpsConfigTicketRequest(
            Mode: "Sealed", TtlSeconds: 600, ExpectedVersion: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, version);

        var dto = await service.GetTicketAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Equal("Sealed", dto!.Mode);
        Assert.Equal(600, dto.TtlSeconds);

        var query = user.Use<IOperationsConfigQuery>();
        var cfg = await query.GetTicketConfigAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(cfg);
        Assert.Equal("Sealed", cfg!.Mode);
        Assert.Equal(600, cfg.TtlSeconds);
    }

    [Fact]
    public async Task Save_ReplyText_And_Degrade_Create()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        var v1 = await service.SaveReplyTextAsync(new SaveOpsConfigReplyTextRequest(
            DisabledChannelReplyTemplate: "服务暂停中", DefaultReplyText: "感谢关注", ExpectedVersion: null),
            TestContext.Current.CancellationToken);
        var v2 = await service.SaveDegradeAsync(new SaveOpsConfigDegradeRequest(
            UnknownChannelReplyEnabled: true, ExpectedVersion: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, v1);
        Assert.Equal(1, v2);

        var reply = await service.GetReplyTextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("服务暂停中", reply!.DisabledChannelReplyTemplate);
        Assert.Equal("感谢关注", reply.DefaultReplyText);

        var degrade = await service.GetDegradeAsync(TestContext.Current.CancellationToken);
        Assert.True(degrade!.UnknownChannelReplyEnabled);
    }

    [Fact]
    public async Task Save_WeakIdentity_Create_AllColumns()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        var version = await service.SaveWeakIdentityAsync(new SaveOpsConfigWeakIdentityRequest(
            Enabled: true, InactiveDays: 90, Action: "SoftDelete", RecoveryWindowDays: 30, ExcludeBound: true,
            ExpectedVersion: null), TestContext.Current.CancellationToken);

        Assert.Equal(1, version);

        var dto = await service.GetWeakIdentityAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.True(dto!.Enabled);
        Assert.Equal(90, dto.InactiveDays);
        Assert.Equal("SoftDelete", dto.Action);
        Assert.Equal(30, dto.RecoveryWindowDays);
        Assert.True(dto.ExcludeBound);
    }

    // ═══════════════════ 乐观并发（R7）═══════════════════

    [Fact]
    public async Task Save_OptimisticConcurrency_VersionIncrementsAndConflict()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        // 创建（ExpectedVersion null = 强制）→ Version=1
        var v1 = await service.SaveArticleReplyAsync(new SaveOpsConfigArticleReplyRequest(
            UrlTemplate: "https://a.example.com/{ticket}", ExpectedVersion: null),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, v1);

        // 带正确 ExpectedVersion=1 → 更新 → Version=2
        var v2 = await service.SaveArticleReplyAsync(new SaveOpsConfigArticleReplyRequest(
            UrlTemplate: "https://b.example.com/{ticket}", ExpectedVersion: 1),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, v2);

        // 陈旧 ExpectedVersion=1（当前已是 2）→ OPS_CONFIG_VERSION_CONFLICT
        var ex = await Assert.ThrowsAsync<DomainException>(() => service.SaveArticleReplyAsync(
            new SaveOpsConfigArticleReplyRequest(UrlTemplate: "https://c.example.com/{ticket}", ExpectedVersion: 1),
            TestContext.Current.CancellationToken));
        Assert.Equal("OPS_CONFIG_VERSION_CONFLICT", ex.ErrorCode);

        // 冲突未落库（回读仍为 b.example.com / Version=2）
        var dto = await service.GetArticleReplyAsync(TestContext.Current.CancellationToken);
        Assert.Equal("https://b.example.com/{ticket}", dto!.UrlTemplate);
        Assert.Equal(2, dto.Version);
    }

    /// <summary>P1-1 回归锁（2026-10-30 审核）：**真并发**两路 PUT 携同一 ExpectedVersion → CAS 保证恰一路成功、
    /// 另一路 `OPS_CONFIG_VERSION_CONFLICT`（修复前读改写双通过 → 丢更新；SQLite 单连接串行下仍验证 CAS 语义）。</summary>
    [Fact]
    public async Task Save_Concurrent_SameExpectedVersion_ExactlyOneSucceeds()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        await service.SaveTicketAsync(new SaveOpsConfigTicketRequest("Sealed", 300, ExpectedVersion: null),
            TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(
            RunAsync(() => service.SaveTicketAsync(new SaveOpsConfigTicketRequest("Sealed", 600, ExpectedVersion: 1), TestContext.Current.CancellationToken)),
            RunAsync(() => service.SaveTicketAsync(new SaveOpsConfigTicketRequest("Sealed", 900, ExpectedVersion: 1), TestContext.Current.CancellationToken)));

        var successes = outcomes.Count(o => o is null);
        var conflicts = outcomes.Count(o => o == "OPS_CONFIG_VERSION_CONFLICT");
        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        var final = await service.GetTicketAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, final!.Version);          // 仅一路递增
        Assert.Contains(final.TtlSeconds, new int?[] { 600, 900 });
    }

    private static async Task<string?> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.ErrorCode;
        }
    }

    [Fact]
    public async Task Save_Update_UpdatedAtAndByOverwritten()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IOperationsConfigService>();

        await service.SaveTicketAsync(new SaveOpsConfigTicketRequest(Mode: "Sealed", TtlSeconds: 300, ExpectedVersion: null),
            TestContext.Current.CancellationToken);
        var before = await service.GetTicketAsync(TestContext.Current.CancellationToken);

        await service.SaveTicketAsync(new SaveOpsConfigTicketRequest(Mode: "Sealed", TtlSeconds: 900, ExpectedVersion: 1),
            TestContext.Current.CancellationToken);
        var after = await service.GetTicketAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(2, after!.Version);
        Assert.Equal(900, after.TtlSeconds);
        Assert.Null(after.UpdatedBy); // 宿主未认证 → UpdatedBy 为空（认证写路径 UId 填充见使用指南）
        Assert.True(after.UpdatedAt >= before!.UpdatedAt,
            $"UpdatedAt 应递增：before={before.UpdatedAt:O} after={after.UpdatedAt:O}");
    }
}