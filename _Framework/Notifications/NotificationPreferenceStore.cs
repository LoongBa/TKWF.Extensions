using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Notifications;

/// <summary>
/// V0.3.0：通知偏好管理实现（命名对齐订阅先例 <see cref="NotificationSubscriptionStore"/>——
/// <see cref="INotificationPreferenceManager"/> 的实现类后缀 Store）——经
/// <see cref="NotificationPreferenceEntityDataService"/>（SG1 DataService）委托持久化，
/// 遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql。
/// <para>ChannelsJson 序列化/反序列化：<c>System.Text.Json</c>（对齐 DataJson 模式，Oracle P2-3）。
/// 异常静默对齐既有扩展（查询失败返回 null/空，写入失败记录 Warning）。</para>
/// </summary>
internal sealed class NotificationPreferenceStore : INotificationPreferenceManager
{
    private readonly NotificationPreferenceEntityDataService _dataService;
    private readonly ILogger<NotificationPreferenceStore> _logger;

    public NotificationPreferenceStore(
        NotificationPreferenceEntityDataService dataService,
        ILogger<NotificationPreferenceStore> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>?> GetChannelsAsync(
        long userId, string notificationName, CancellationToken ct = default)
    {
        try
        {
            var preference = await _dataService.GetByUserAndNameAsync(userId, notificationName, ct);
            return DeserializeChannels(preference?.ChannelsJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知偏好查询失败: GetChannelsAsync ({UserId}/{Name})", userId, notificationName);
            return null;   // 回退定义级
        }
    }

    public async Task SetAsync(
        long userId, string notificationName, IReadOnlyList<string> channels, CancellationToken ct = default)
    {
        try
        {
            var json = SerializeChannels(channels);
            var preference = await _dataService.GetByUserAndNameAsync(userId, notificationName, ct);
            if (preference == null)
            {
                await _dataService.CreateAsync(new NotificationPreferenceEntity
                {
                    UserId = userId,
                    NotificationName = notificationName,
                    ChannelsJson = json,
                    CreateTime = DateTime.UtcNow,
                    UpdateTime = DateTime.UtcNow
                }, ct);
            }
            else
            {
                preference.ChannelsJson = json;
                preference.UpdateTime = DateTime.UtcNow;
                await _dataService.UpdateAsync(preference, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知偏好设置失败: SetAsync ({UserId}/{Name})", userId, notificationName);
        }
    }

    public async Task ClearAsync(long userId, string notificationName, CancellationToken ct = default)
    {
        try
        {
            await _dataService.DeleteByUserAndNameAsync(userId, notificationName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知偏好清除失败: ClearAsync ({UserId}/{Name})", userId, notificationName);
        }
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>?>> GetChannelsBatchAsync(
        IReadOnlyList<long> userIds, string notificationName, CancellationToken ct = default)
    {
        var result = new Dictionary<long, IReadOnlyList<string>?>();
        try
        {
            var map = await _dataService.GetChannelsBatchAsync(userIds, notificationName, ct);
            foreach (var (uid, json) in map)
                result[uid] = DeserializeChannels(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知偏好批量查询失败: GetChannelsBatchAsync ({Name})", notificationName);
        }
        return result;
    }

    /// <summary>通道列表 → JSON（null 输入 = null = 回退定义级；非空列表 = 用户偏好）。</summary>
    private static string? SerializeChannels(IReadOnlyList<string>? channels)
        => channels == null ? null : JsonSerializer.Serialize(channels);

    /// <summary>JSON → 通道列表（null/损坏输入 = null 回退定义级；[] = 空列表表示"不接收该通知"）。</summary>
    private static IReadOnlyList<string>? DeserializeChannels(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return null;   // 损坏数据回退定义级
        }
    }
}