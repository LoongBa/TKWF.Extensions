using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKWF.Ext.Notifications;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// V0.3.0 逐用户权限门控测试——发布器委托 <see cref="IPermissionBatchChecker"/> 过滤无权限收件人。
/// <para>覆盖：部分有权限 → 仅授权收件人投递 / 全部无权限 → 不落库 / 批量检查器未注册 → 跳过门控（降级 C1）/
/// 权限过滤 + 偏好覆盖组合（先权限过滤，再偏好覆盖——Oracle P1-3）。</para>
/// <para>角色回退 / Admin.All / fail-closed 归 Permissions 侧 <c>PermissionBatchCheckerTests</c>——此处仅验证"有权限保留、无权限过滤"。</para>
/// </summary>
public class NotificationPermissionGateTests
{
    private static Action<IServiceCollection> PublisherGranted()
        => services => services.AddSingleton<IPermissionChecker>(
            new FakePermissionChecker(new Dictionary<string, bool> { ["Notifications.Restricted"] = true }));

    [Fact]
    public async Task Publish_RecipientGate_FiltersUnauthorizedRecipients()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, PermissionGatedDefinitions>();
            PublisherGranted()(services);
            services.AddSingleton<IPermissionBatchChecker>(new FakePermissionBatchChecker(1)); // 仅用户 1 有权限
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(PermissionGatedDefinitions.RestrictedAlert, userIds: new long[] { 1, 2 });

        Assert.Single(await store.GetUnreadAsync(1));
        Assert.Empty(await store.GetUnreadAsync(2)); // 用户 2 无权限 → 过滤
    }

    [Fact]
    public async Task Publish_RecipientGate_AllUnauthorized_NoNotificationRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, PermissionGatedDefinitions>();
            PublisherGranted()(services);
            services.AddSingleton<IPermissionBatchChecker>(new FakePermissionBatchChecker()); // 无任何用户有权限
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(PermissionGatedDefinitions.RestrictedAlert, userIds: new long[] { 1, 2 });

        // 全部无权限 → 收件人为空 → 不进入事务（不落库）
        Assert.Equal(0, fsql.Select<NotificationEntity>().Count());
        Assert.Empty(await store.GetUnreadAsync(1));
        Assert.Empty(await store.GetUnreadAsync(2));
    }

    [Fact]
    public async Task Publish_BatchCheckerNotRegistered_SkipsGate()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, PermissionGatedDefinitions>();
            PublisherGranted()(services);
            // 不注册 IPermissionBatchChecker（Permissions 未启用 batch API 场景）→ 跳过门控（降级仅 C1）+ Warning
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(PermissionGatedDefinitions.RestrictedAlert, userIds: new long[] { 1, 2 });

        // 批量检查器缺失 → 跳过逐用户门控（P2-2）→ 全部收件人投递
        Assert.Single(await store.GetUnreadAsync(1));
        Assert.Single(await store.GetUnreadAsync(2));
    }

    [Fact]
    public async Task Publish_RecipientGate_CombinedWithPreferenceOverride()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        var fakeEmail = new FakeRecorderNotifier("Email");
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
            services.AddSingleton<INotificationNotifier>(fakeEmail);
            services.AddSingleton<INotificationDefinitionProvider, GatedMultiChannelDefinitions>();
            PublisherGranted()(services);
            services.AddSingleton<IPermissionBatchChecker>(new FakePermissionBatchChecker(1)); // 仅用户 1 有权限
        });
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 定义级 UseChannels("Inbox","Email")；用户 1 有权限 + 偏好 ["Email"]；用户 2 无权限
        await manager.SetAsync(1, GatedMultiChannelDefinitions.SecureChannelNotice, new[] { "Email" });

        await publisher.PublishAsync(GatedMultiChannelDefinitions.SecureChannelNotice, userIds: new long[] { 1, 2 });

        // 先权限过滤（用户 2 出局）→ 再偏好覆盖（用户 1 仅 Email）
        Assert.Single(fakeEmail.Requests, r => r.UserId == 1);
        Assert.DoesNotContain(fakeInbox.Requests, r => r.UserId == 1);
        Assert.DoesNotContain(fakeInbox.Requests, r => r.UserId == 2);
        Assert.DoesNotContain(fakeEmail.Requests, r => r.UserId == 2);
    }
}

/// <summary>权限门控 + 多通道通知定义 Provider——权限过滤 + 偏好覆盖组合测试（Oracle P1-3）。</summary>
internal sealed class GatedMultiChannelDefinitions : INotificationDefinitionProvider
{
    public const string SecureChannelNotice = "SecureChannelNotice";

    public void Define(INotificationDefinitionContext context)
        => context.Add(new NotificationDefinition(SecureChannelNotice, "受限多通道通知")
            .RequirePermission("Notifications.Restricted")
            .UseChannels("Inbox", "Email"));
}