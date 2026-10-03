using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

    /// <summary>
    /// Notifications V0.5.0 测试——UserNotificationViewQueryService REST 直接暴露（方案 03）：
    /// VEntity DTO 一等公民（含 Name/Severity/DisplayName JOIN 字段不再丢弃）、仅本人防护（C5/N5）、
    /// name 可空（C7/N2b——null 查全部收件箱）、DI 注册可解析（N4/C6-med）。
    /// </summary>
    public class UserNotificationViewQueryServiceTests
    {
        private static NotificationTestHost BuildWithUser(long userId, IFreeSql fsql)
            // ⚠️ IDomainUser 测试内部桩——UserNotificationViewQueryService（[GenerateController] 管理 API 服务，V0.5.0
            // 未整改）直接解析 IDomainUser；生产 DataService/领域服务经 User.Use<T>() AOP/NoAop 路径，IDomainUser 永不注册 DI（D01）。
            // T3 候选：管理 API 服务（[GenerateController]）的 IDomainUser 供给形态待框架统一（对齐 FeatureManagementApiService）。
            => NotificationTestHost.Build(fsql, s =>
                s.AddScoped<IDomainUser>(sp => { var u = new AuthenticatedStubUser(userId.ToString()); u.ServiceProvider = sp; return u; }));

    // ── N2：REST 直接暴露——VEntity DTO 完整字段（JOIN 三列不再丢弃）+ 仅本人 ──

    [Fact]
    public async Task GetMyInbox_Paged_ReturnsFullViewDto_OnlySelf()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = BuildWithUser(userId: 1, fsql);
        var publisher = host.Publisher;
        // N4：DI 注册可解析——测试宿主镜像生产自动注册，容器直接解析 Service
        var svc = host.GetRequiredService<UserNotificationViewQueryService>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert,
            severity: NotificationSeverity.Warning, userIds: new long[] { 1 });

        var inbox = await svc.GetMyInboxAsync(page: 1, pageSize: 10);

        Assert.Equal(2, inbox.Count);
        var shipped = Assert.Single(inbox, d => d.Name == TestNotificationDefinitions.OrderShipped);
        Assert.Equal("订单已发货", shipped.DisplayName);               // 视图 JOIN 字段不再丢弃
        Assert.Equal((int)NotificationSeverity.Info, shipped.Severity);
        Assert.Equal(1, shipped.UserId);                               // 仅本人
        var alert = Assert.Single(inbox, d => d.Name == TestNotificationDefinitions.SystemAlert);
        Assert.Equal("系统公告", alert.DisplayName);
        Assert.Equal((int)NotificationSeverity.Warning, alert.Severity);
    }

    [Fact]
    public async Task GetMyInbox_OtherUser_Inbox_NotLeaked()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = BuildWithUser(userId: 1, fsql);
        var publisher = host.Publisher;
        var svc = host.GetRequiredService<UserNotificationViewQueryService>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1, 2 });

        // 以用户 1 身份调用只返回用户 1 的收件箱——不泄露用户 2 的（防 IDOR）
        var inbox = await svc.GetMyInboxAsync(page: 1, pageSize: 10);
        Assert.Single(inbox);
        Assert.Equal(1, inbox[0].UserId);
    }

    // ── N2b：name 可空——null 查全部收件箱（C4-med/C7 语义割裂修复）──

    [Fact]
    public async Task GetMyInbox_NameNull_ReturnsAllInbox_WithFullFields()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = BuildWithUser(userId: 1, fsql);
        var publisher = host.Publisher;
        var svc = host.GetRequiredService<UserNotificationViewQueryService>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });

        // name 可空——null = 查全部收件箱（REST 面 ≥ 门面面）；且 JOIN 列完整保留（REST 面 > 门面面）
        var all = await svc.GetMyInboxAsync(page: 1, pageSize: 10, name: null);
        Assert.Equal(3, all.Count);
        Assert.All(all, d => Assert.False(string.IsNullOrEmpty(d.Name)));
        Assert.All(all, d => Assert.Equal(1, d.UserId));
    }

    [Fact]
    public async Task GetMyInbox_FilterByName()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = BuildWithUser(userId: 1, fsql);
        var publisher = host.Publisher;
        var svc = host.GetRequiredService<UserNotificationViewQueryService>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });

        var shipped = await svc.GetMyInboxAsync(page: 1, pageSize: 10, name: TestNotificationDefinitions.OrderShipped);
        Assert.Equal(2, shipped.Count);
        Assert.All(shipped, d => Assert.Equal(TestNotificationDefinitions.OrderShipped, d.Name));

        var alert = await svc.GetMyInboxAsync(page: 1, pageSize: 10, name: TestNotificationDefinitions.SystemAlert);
        Assert.Single(alert);
    }

    // ── N5：越权防护——无用户上下文抛 UnauthorizedAccessException ──

    [Fact]
    public async Task GetMyInbox_NoUserContext_ThrowsUnauthorizedAccess()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = NotificationTestHost.Build(fsql);   // StubDomainUser——无 userId
        var svc = host.GetRequiredService<UserNotificationViewQueryService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.GetMyInboxAsync(page: 1, pageSize: 10));
    }
}
