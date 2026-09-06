using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Notifications;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="INotificationPublisher"/> 发布器测试——经 DI 容器解析真实实现（内存 fsql + 默认定义 Provider + InboxNotifier）。
/// <para>覆盖：显式收件人 / 空数组空操作 / 去重 / 排除 / 订阅者派发 / 实体级订阅匹配 / 未定义异常 /
/// 发布方权限门控（mock IPermissionChecker）/ NotificationData JSON 往返。</para>
/// </summary>
public class NotificationPublisherTests
{
    // ─── 显式收件人 ───

    [Fact]
    public async Task Publish_ExplicitUsers_CreatesNotificationAndInboxRows()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1, 2 });

        // Notification 发布态 1 行（含定义快照 + 严重级别）——无业务方法覆盖（发布态直读），保留直查
        var notifications = fsql.Select<NotificationEntity>().ToList();
        Assert.Single(notifications);
        Assert.Equal(TestNotificationDefinitions.OrderShipped, notifications[0].Name);
        Assert.Equal("订单已发货", notifications[0].DisplayName);
        Assert.Equal((int)NotificationSeverity.Info, notifications[0].Severity);

        // 收件箱每收件人 1 行（未读，指向同一 Notification）——经 Store 业务方法
        var user1Inbox = await store.GetUnreadAsync(1);
        var user2Inbox = await store.GetUnreadAsync(2);
        Assert.Single(user1Inbox);
        Assert.Single(user2Inbox);
        Assert.All(user1Inbox.Concat(user2Inbox), r => Assert.Equal(notifications[0].Id, r.NotificationId));
        Assert.Equal(new[] { 1L, 2L }, user1Inbox.Concat(user2Inbox).Select(r => r.UserId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Publish_EmptyUserIds_NoOp()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: Array.Empty<long>());

        Assert.Equal(0, fsql.Select<NotificationEntity>().Count()); // 无业务方法覆盖（发布态直读），保留直查
        Assert.Empty(await store.GetUnreadAsync(1));
    }

    [Fact]
    public async Task Publish_DuplicateUserIds_Deduplicated()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1, 1, 2 });

        Assert.Single(fsql.Select<NotificationEntity>().ToList()); // 无业务方法覆盖（发布态直读），保留直查
        var user1Inbox = await store.GetUnreadAsync(1);
        var user2Inbox = await store.GetUnreadAsync(2);
        Assert.Equal(2, user1Inbox.Count + user2Inbox.Count); // 1,1,2 → 去重 → 1,2
        Assert.Equal(new[] { 1L, 2L }, user1Inbox.Concat(user2Inbox).Select(r => r.UserId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Publish_ExcludedUserIds_Removed()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(
            TestNotificationDefinitions.OrderShipped,
            userIds: new long[] { 1, 2, 3 },
            excludedUserIds: new long[] { 2 });

        var user1Inbox = await store.GetUnreadAsync(1);
        var user3Inbox = await store.GetUnreadAsync(3);
        Assert.Equal(2, user1Inbox.Count + user3Inbox.Count);
        Assert.Equal(new[] { 1L, 3L }, user1Inbox.Concat(user3Inbox).Select(r => r.UserId).OrderBy(id => id).ToArray());
    }

    // ─── 订阅者派发 ───

    [Fact]
    public async Task Publish_SubscriptionUsers_ResolvesRecipients()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        // 用户 1、2 订阅定义级通知；用户 3 未订阅
        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);
        await subManager.SubscribeAsync(2, TestNotificationDefinitions.OrderShipped);

        // userIds=null → 按订阅解析收件人
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped);

        var user1Inbox = await store.GetUnreadAsync(1);
        var user2Inbox = await store.GetUnreadAsync(2);
        Assert.Equal(2, user1Inbox.Count + user2Inbox.Count);
        Assert.Equal(new[] { 1L, 2L }, user1Inbox.Concat(user2Inbox).Select(r => r.UserId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Publish_EntitySubscription_MatchesEntity()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        // 用户 1 订阅实体 o-100；用户 2 订阅实体 o-200
        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped, "Order", "o-100");
        await subManager.SubscribeAsync(2, TestNotificationDefinitions.OrderShipped, "Order", "o-200");

        // 发布实体级通知（o-100）→ 仅订阅该实体的用户 1 收到
        await publisher.PublishAsync(
            TestNotificationDefinitions.OrderShipped,
            entityTypeName: "Order",
            entityId: "o-100");

        var user1Inbox = await store.GetUnreadAsync(1);
        Assert.Single(user1Inbox);
        Assert.Equal(1L, user1Inbox[0].UserId);

        // Notification 行填充实体关联——无业务方法覆盖（发布态直读），保留直查
        var notif = fsql.Select<NotificationEntity>().First();
        Assert.Equal("Order", notif.EntityTypeName);
        Assert.Equal("o-100", notif.EntityId);
    }

    // ─── 定义校验 ───

    [Fact]
    public async Task Publish_UndefinedNotification_Throws()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => publisher.PublishAsync("NoSuchNotification", userIds: new long[] { 1 }));

        // 未定义 → 不落库
        Assert.Equal(0, fsql.Select<NotificationEntity>().Count()); // 无业务方法覆盖（发布态直读），保留直查
        Assert.Empty(await store.GetUnreadAsync(1));
    }

    // ─── 发布方权限门控（RequirePermission）───

    [Fact]
    public async Task Publish_RequirePermission_PublisherLacksPermission_Throws()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, PermissionGatedDefinitions>();
            services.AddSingleton<IPermissionChecker>(new FakePermissionChecker(
                new Dictionary<string, bool> { ["Notifications.Restricted"] = false }));
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 发布方无权限 → InvalidOperationException（实现约定：发布方权限门控失败）
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(PermissionGatedDefinitions.RestrictedAlert, userIds: new long[] { 1 }));

        Assert.Equal(0, fsql.Select<NotificationEntity>().Count()); // 无业务方法覆盖（发布态直读），保留直查
    }

    [Fact]
    public async Task Publish_RequirePermission_PublisherHasPermission_Succeeds()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, PermissionGatedDefinitions>();
            services.AddSingleton<IPermissionChecker>(new FakePermissionChecker(
                new Dictionary<string, bool> { ["Notifications.Restricted"] = true }));
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(PermissionGatedDefinitions.RestrictedAlert, userIds: new long[] { 1 });

        Assert.Single(fsql.Select<NotificationEntity>().ToList()); // 无业务方法覆盖（发布态直读），保留直查
        Assert.Single(await store.GetUnreadAsync(1));
    }

    // ─── 通知数据（NotificationData JSON 往返）───

    [Fact]
    public async Task Publish_NotificationData_PersistsJsonRoundTrip()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        var data = new NotificationData(new Dictionary<string, object?>
        {
            ["OrderId"] = "100",
            ["TrackingNo"] = "SF1234567890"
        });
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, data: data, userIds: new long[] { 1 });

        // DataJson 落库后可反序列化回 NotificationData
        var notif = fsql.Select<NotificationEntity>().First(); // 无业务方法覆盖（发布态直读），保留直查
        Assert.NotNull(notif.DataJson);

        var restored = NotificationData.FromJson(notif.DataJson);
        Assert.NotNull(restored);
        Assert.Equal("100", restored!.Values["OrderId"]);
        Assert.Equal("SF1234567890", restored.Values["TrackingNo"]);
    }

    // ─── M2 修订：定义级 + 实体级订阅并集 ───

    [Fact]
    public async Task Publish_EntityNotification_IncludesDefinitionLevelSubscribers()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        // 用户 1：定义级订阅（收全部）；用户 2：实体级订阅 o-100；用户 3：实体级订阅 o-200
        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);
        await subManager.SubscribeAsync(2, TestNotificationDefinitions.OrderShipped, "Order", "o-100");
        await subManager.SubscribeAsync(3, TestNotificationDefinitions.OrderShipped, "Order", "o-200");

        // 发布实体级通知（o-100）→ 并集：定义级用户 1 + 实体级用户 2（用户 3 不收）
        await publisher.PublishAsync(
            TestNotificationDefinitions.OrderShipped,
            entityTypeName: "Order",
            entityId: "o-100");

        var user1Inbox = await store.GetUnreadAsync(1);
        var user2Inbox = await store.GetUnreadAsync(2);
        Assert.Equal(2, user1Inbox.Count + user2Inbox.Count);
        Assert.Equal(new[] { 1L, 2L }, user1Inbox.Concat(user2Inbox).Select(r => r.UserId).OrderBy(id => id).ToArray());
    }

    // ─── M5 修订：inbox 写入失败 → 事务回滚（C4 原子性）───

    [Fact]
    public async Task Publish_InboxWriteFails_RollsBackNotification()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);

        var recordingTx = new RecordingTransactionManager();

        // 注入：记录型事务管理器 + 首次 inbox 写入抛异常的 Notifier 桩（替换 InboxNotifier）
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<ITransactionManager>(recordingTx);
            services.RemoveAll<INotificationNotifier>();
            services.AddScoped<INotificationNotifier, ThrowingInboxNotifier>();
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // Inbox 写入抛异常 → 异常传播 → 发布器 catch → 事务回滚被调用（C4）
        // 注：测试事务管理器为桩（不包物理事务），DB 行无法真正撤销——只验证"异常传播触发 RollbackAsync 调用"。
        // 真实 FreeSqlTransactionManager 下 Rollback 会撤销全部写入（框架侧已验证）。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 }));

        Assert.True(recordingTx.RollbackCount > 0, "事务应回滚（inbox 写入失败）");
        Assert.Equal(0, recordingTx.CommitCount);
    }
}

/// <summary>权限门控通知定义 Provider——仅用于发布方权限门控测试（RequirePermission 定义）。</summary>
internal sealed class PermissionGatedDefinitions : INotificationDefinitionProvider
{
    public const string RestrictedAlert = "RestrictedAlert";

    public void Define(INotificationDefinitionContext context)
        => context.Add(new NotificationDefinition(RestrictedAlert, "受限公告").RequirePermission("Notifications.Restricted"));
}

/// <summary>inbox 写入即抛异常的 Notifier 桩——模拟收件箱写入失败（M5 事务回滚测试）。</summary>
internal sealed class ThrowingInboxNotifier : INotificationNotifier
{
    public string Name => "Inbox";

    public Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("模拟 inbox 写入失败");
}

/// <summary>记录 Commit/Rollback 调用的事务管理器——验证 C4 原子性（M5）。</summary>
internal sealed class RecordingTransactionManager : ITransactionManager
{
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }
    public bool IsActive => false;

    public ITransactionScope Begin(IsolationLevel isolationLevel = IsolationLevel.Serializable)
        => new RecordingScope(this);

    public Task<ITransactionScope> BeginAsync(IsolationLevel isolationLevel = IsolationLevel.Serializable, CancellationToken ct = default)
        => Task.FromResult<ITransactionScope>(new RecordingScope(this));

    private sealed class RecordingScope(RecordingTransactionManager owner) : ITransactionScope
    {
        public bool IsActive => false;
        public Task CommitAsync(CancellationToken ct = default) { owner.CommitCount++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct = default) { owner.RollbackCount++; return Task.CompletedTask; }
        public void Commit() => owner.CommitCount++;
        public void Rollback() => owner.RollbackCount++;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
