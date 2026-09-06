using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知数据——键值对 payload，随通知一起发布并持久化为 JSON。
/// <para>值类型：string/number/bool/可 JSON 序列化对象。取值时按字符串语义简化（索引器返回 string?）。</para>
/// </summary>
public sealed class NotificationData
{
    /// <summary>键值对存储（键不区分大小写）。</summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>创建空通知数据。</summary>
    public NotificationData() { }

    /// <summary>用初始键值对创建通知数据。</summary>
    public NotificationData(IDictionary<string, object?>? values = null)
    {
        if (values != null)
        {
            foreach (var kv in values)
                Values[kv.Key] = kv.Value;
        }
    }

    /// <summary>按键取值（字符串化；不存在返回 null）。</summary>
    public string? this[string key]
    {
        get => Values.TryGetValue(key, out var value) ? value?.ToString() : null;
        set => Values[key] = value;
    }

    /// <summary>序列化为 JSON（用于 DataJson 落库）。</summary>
    public string? ToJson()
        => Values.Count == 0 ? null : JsonSerializer.Serialize(Values);

    /// <summary>从 JSON 反序列化（失败返回 null，容忍脏数据）。</summary>
    public static NotificationData? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict == null) return null;
            var data = new NotificationData();
            foreach (var kv in dict)
                data.Values[kv.Key] = kv.Value.ValueKind == JsonValueKind.String
                    ? kv.Value.GetString()
                    : kv.Value.GetRawText();
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
