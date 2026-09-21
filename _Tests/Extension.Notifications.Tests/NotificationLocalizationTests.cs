using System.Globalization;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Localization;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// V0.5.0 通知本地化测试（对接 ADR31 i18n）——覆盖验收 R1-R3.1/R5/R5.1。
/// <para>测试宿主注册 <see cref="FakeFrameworkLocalizer"/>（内存字典，模拟 ADR31 契约：
/// 命中返回译文 / 未命中返回 key 本身 / 可配空串）+ 带 <see cref="NotificationDefinition.DisplayNameKey"/>
/// 的定义 Provider。四条本地化路径：命中 / 未命中 / 空串（Oracle C1 回归）/ null（未注册）。
/// 默认 InvariantCulture 断言（xunit 默认）——测试无文化敏感 flaky。</para>
/// </summary>
public class NotificationLocalizationTests
{
    // ─── 本地化显示名：发布快照解析（R1-R3.1） ───

    [Fact]
    public async Task Publish_NoDisplayNameKey_KeepsRawDisplayNameSnapshot()
    {
        // R1：未设置 DisplayNameKey → 快照与 v0.4.0 完全一致（零回归）
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });

        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal("订单已发货", notification.DisplayName); // 原始文本快照，key 未设置不受影响
    }

    [Fact]
    public async Task Publish_DisplayNameKey_LocalizerHit_UsesLocalizedSnapshot()
    {
        // R2：设置 DisplayNameKey + localizer 命中 → 发布快照为本地化译文
        var localizer = FakeFrameworkLocalizer.With(
            ("Notifications/Definition_OrderShipped", "Order Shipped (zh)"));
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, s => RegisterLocalizedDefinitions(s, localizer));
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync("OrderShippedLocalized", userIds: new long[] { 1 });

        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal("Order Shipped (zh)", notification.DisplayName);
    }

    [Fact]
    public async Task Publish_DisplayNameKey_LocalizerMiss_FallsBackToRawDisplayName()
    {
        // R3：设置 DisplayNameKey + localizer 未命中（契约返回 key 本身）→ 回退原始 DisplayName，key 不得落库
        var localizer = FakeFrameworkLocalizer.Empty; // 字典空——任何 key 解析返回 key 本身
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, s => RegisterLocalizedDefinitions(s, localizer));
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync("OrderShippedLocalized", userIds: new long[] { 1 });

        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal("订单已发货（本地化）", notification.DisplayName); // 回退原文
        Assert.DoesNotContain("Notifications/Definition_", notification.DisplayName); // key 绝不落库
    }

    [Fact]
    public async Task Publish_DisplayNameKey_LocalizerEmptyString_FallsBackToRawDisplayName()
    {
        // R3.1（Oracle C1 回归）：localizer 返回空串（贡献者配空值/占位符未填）→ 防空串落库，回退原始 DisplayName
        var localizer = FakeFrameworkLocalizer.With(
            ("Notifications/Definition_OrderShipped", "")); // 空串译文
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, s => RegisterLocalizedDefinitions(s, localizer));
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync("OrderShippedLocalized", userIds: new long[] { 1 });

        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal("订单已发货（本地化）", notification.DisplayName); // 空串 → 回退原文，不落空串
    }

    [Fact]
    public async Task Publish_DisplayNameKey_NoLocalizerRegistered_ZeroChange()
    {
        // R4：消费方未注册 IFrameworkLocalizer（null）→ 零行为变化——定义 key 被忽略，快照 = 原文
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql, s => RegisterLocalizedDefinitions(s, localizer: null));
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync("OrderShippedLocalized", userIds: new long[] { 1 });

        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal("订单已发货（本地化）", notification.DisplayName); // localizer null → 原文快照，与未设置 key 等价
    }

    // ─── 严重级别显示名（R5） ───

    [Fact]
    public void Severity_GetDisplayName_NoLocalizer_ReturnsEnumName()
    {
        Assert.Equal(nameof(NotificationSeverity.Info), NotificationSeverity.Info.GetDisplayName(null));
        Assert.Equal(nameof(NotificationSeverity.Success), NotificationSeverity.Success.GetDisplayName(null));
        Assert.Equal(nameof(NotificationSeverity.Warning), NotificationSeverity.Warning.GetDisplayName(null));
        Assert.Equal(nameof(NotificationSeverity.Error), NotificationSeverity.Error.GetDisplayName(null));
    }

    [Fact]
    public void Severity_GetDisplayName_LocalizerHit_ReturnsLocalized()
    {
        var localizer = FakeFrameworkLocalizer.With(
            ("Notifications/Severity_Warning", "警告"));
        Assert.Equal("警告", NotificationSeverity.Warning.GetDisplayName(localizer));
    }

    [Fact]
    public void Severity_GetDisplayName_LocalizerMiss_ReturnsEnumName()
    {
        // 未命中（契约返回 key 本身）→ 回退英文枚举名
        var localizer = FakeFrameworkLocalizer.Empty;
        Assert.Equal(nameof(NotificationSeverity.Warning), NotificationSeverity.Warning.GetDisplayName(localizer));
    }

    [Fact]
    public void Severity_GetDisplayName_LocalizerEmptyString_ReturnsEnumName()
    {
        // 空串译文（Oracle C1 规则一致）→ 回退英文枚举名
        var localizer = FakeFrameworkLocalizer.With(("Notifications/Severity_Info", ""));
        Assert.Equal(nameof(NotificationSeverity.Info), NotificationSeverity.Info.GetDisplayName(localizer));
    }

    // ─── 基础设施 ───

    /// <summary>注册带 DisplayNameKey 的本地化定义 Provider + 模拟 localizer（configure 回调内，初始化器之前）。</summary>
    private static void RegisterLocalizedDefinitions(IServiceCollection services, IFrameworkLocalizer? localizer)
    {
        if (localizer != null)
            services.AddSingleton<IFrameworkLocalizer>(localizer);
        services.AddSingleton<INotificationDefinitionProvider, LocalizedNotificationDefinitions>();
    }

    /// <summary>本地化定义 Provider——OrderShippedLocalized 设置 DisplayNameKey（约定 Notifications/Definition_{Name}）。</summary>
    private sealed class LocalizedNotificationDefinitions : INotificationDefinitionProvider
    {
        public void Define(INotificationDefinitionContext context)
        {
            context.Add(new NotificationDefinition(
                "OrderShippedLocalized",
                "订单已发货（本地化）",
                "Notifications/Definition_OrderShipped",
                NotificationSeverity.Info));
        }
    }

    /// <summary>
    /// 模拟 IFrameworkLocalizer（内存字典）——忠实模拟 ADR31 契约：
    /// 命中返回译文（可配空串）/ 未命中返回 key 本身 / Culture = 当前 UI 文化。
    /// </summary>
    private sealed class FakeFrameworkLocalizer : IFrameworkLocalizer
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        private FakeFrameworkLocalizer(IReadOnlyDictionary<string, string> values) => _values = values;

        public static FakeFrameworkLocalizer Empty { get; } = new FakeFrameworkLocalizer(new Dictionary<string, string>());

        public static FakeFrameworkLocalizer With(params (string Key, string Value)[] entries)
            => new FakeFrameworkLocalizer(entries.ToDictionary(e => e.Key, e => e.Value));

        public CultureInfo Culture => CultureInfo.CurrentUICulture;

        public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

        public string this[string key, params object[] args]
        {
            get
            {
                var resolved = this[key];
                return args is null || args.Length == 0 ? resolved : string.Format(resolved, args);
            }
        }
    }
}