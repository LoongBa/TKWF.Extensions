using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TKWF.Federation.WeChat;

/// <summary>
/// <see cref="WeChatMenuButton.SubButton"/> 双形状归一 converter（方案 docs/Federation.WeChat/v0.17.1-微信当前菜单sub_button形状修复-开发方案.md §二）。
/// <para><c>get_current_selfmenu_info</c>（查询当前菜单<b>配置</b>）官方返回 <c>sub_button</c> 为<b>对象</b>
/// <c>{"list":[...]}</c>；<c>menu/get</c>（查询<b>已发布菜单</b>）为<b>数组</b> <c>[...]</c>——本地建模保持
/// <c>WeChatMenuButton[]?</c>（数组），属性级 converter 在反序列化边界归一两种形状，消费方零感知。</para>
/// <para>Write 方向保持数组序列化（本地快照 <c>MenuSnapshot.MenuJson</c> 存储形状不变——
    /// <c>UpsertSnapshotAsync</c>（WeChatOperations）无影响）。</para>
    /// <para>属性级 converter 挂 <c>SubButton</c> 属性 → STJ 自动<b>递归</b>处理深层
    /// <c>{"list":[{...sub_button:{"list":[...]}}]}</c>（Read 内 <see cref="JsonDocument.ParseValue(ref Utf8JsonReader)"/>
    /// 消费子树 → <c>JsonElement.Deserialize&lt;T&gt;(options)</c> 新解析上下文，元素属性级 converter 继续生效）。</para>
/// </summary>
internal sealed class WeChatSubButtonListConverter : JsonConverter<WeChatMenuButton[]?>
{
    /// <summary>Read 方向归一（方案 §二 归一规则表）：
    /// <list type="bullet">
    /// <item><c>Null</c> → null（<c>HandleNull</c> 默认 false，STJ 先投递 Null token——本分支必需）</item>
    /// <item><c>StartArray</c> → 直接反序列化数组（<c>menu/get</c> 形状）</item>
    /// <item><c>StartObject</c> → 逐属性扫描找 <c>list</c>（<c>get_current_selfmenu_info</c> 官方形状）：
    /// 值 StartArray → 反序列化 / Null → null / 找不到 list（如空对象 <c>{}</c>）→ null / 其它属性（如 <c>count</c>）→ Skip</item>
    /// <item>其它 token（String/Number/True/False）→ 抛 <see cref="JsonException"/>（数据错误不吞——非"空"语义）</item>
    /// </list></summary>
    public override WeChatMenuButton[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartArray:
                return ReadArrayRaw(ref reader, options);
            case JsonTokenType.StartObject:
                return ReadObjectShape(ref reader, options);
            default:
                throw new JsonException($"sub_button 形状不支持：{reader.TokenType}");
        }
    }

    /// <summary>Write 方向保持数组（快照存储形状不变）。</summary>
    public override void Write(Utf8JsonWriter writer, WeChatMenuButton[]? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);

    /// <summary>
    /// 数组形状读取——<c>JsonDocument.ParseValue(ref reader)</c> 消费当前 StartArray 子树（reader 自动推进到值之后；
    /// converter 内 <c>Deserialize&lt;T&gt;(ref reader, options)</c> 嵌套递归在 STJ VerifyRead 有 reader 位置记账冲突
    /// ——"read too much or not enough"；ParseValue 为 converter 标准安全模式，元素递归经
    /// <c>JsonElement.Deserialize&lt;T&gt;(options)</c> 新解析上下文，属性级 converter 继续生效）。
    /// </summary>
    private static WeChatMenuButton[]? ReadArrayRaw(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Deserialize<WeChatMenuButton[]>(options);
    }

    /// <summary>对象形状扫描（<c>{"list":[...]}</c> / <c>{"list":null}</c> / <c>{}</c> / 附 <c>count</c> 等未识别属性）。</summary>
    private static WeChatMenuButton[]? ReadObjectShape(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return null;   // {} / 无 list 属性 → 归一"无子菜单"
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"sub_button 对象形状不合法：{reader.TokenType}");

            var propertyName = reader.GetString();
            if (!reader.Read())
                throw new JsonException("sub_button 对象读取提前结束");

            if (string.Equals(propertyName, "list", StringComparison.Ordinal))
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartArray:
                    {
                        var result = ReadArrayRaw(ref reader, options);
                        ConsumeToEndObject(ref reader);
                        return result;
                    }
                    case JsonTokenType.Null:
                        ConsumeToEndObject(ref reader);   // list:null → 归一"无子菜单"（消费剩余属性至 EndObject）
                        return null;
                    default:
                        throw new JsonException($"sub_button.list 值形状不支持：{reader.TokenType}");
                }
            }

            reader.Skip();   // 其它属性（如 count）→ 跳过
        }

        throw new JsonException("sub_button 对象读取提前结束（缺结束符）");
    }

    /// <summary>消费当前对象内剩余属性至 EndObject（converter 协议：Read 返回时 reader 须在值结束之后——
    /// list 值（数组/Null）解析后仍在对象内部，须继续推进到 EndObject 方可返回）。</summary>
    private static void ConsumeToEndObject(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"sub_button 对象形状不合法：{reader.TokenType}");
            reader.Skip();
        }

        throw new JsonException("sub_button 对象读取提前结束（缺结束符）");
    }
}