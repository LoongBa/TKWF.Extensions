using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 默认脱敏策略（store 层统一应用）。按字段名匹配（大小写不敏感），命中值替换为掩码；
    /// 默认敏感字段集 + 消费方 <see cref="AuditLoggingOptions.AdditionalSensitiveFields"/> 叠加。
    /// </summary>
    internal sealed class DefaultAuditMaskingPolicy : IAuditMaskingPolicy
    {
        private const string MaskValue = "***";

        private static readonly string[] DefaultSensitiveFields =
        {
            "password", "clienthash", "credential", "salt", "token", "secret",
            "oldchallengeresponse", "newclienthash", "newsalt", "sessionkey",
            "phone", "mobile", "appsecret", "aeskey", "encodingaeskey", "privatekey",
        };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HashSet<string> _sensitive;

        public DefaultAuditMaskingPolicy(IEnumerable<string>? additionalSensitiveFields)
        {
            _sensitive = new HashSet<string>(DefaultSensitiveFields, StringComparer.OrdinalIgnoreCase);
            if (additionalSensitiveFields is not null)
            {
                foreach (var field in additionalSensitiveFields)
                {
                    if (!string.IsNullOrWhiteSpace(field))
                    {
                        _sensitive.Add(field);
                    }
                }
            }
        }

        public object? Mask(object? value)
        {
            if (value is null) return null;
            if (value is string text) return MaskText(text);

            var node = JsonSerializer.SerializeToNode(value, JsonOptions);
            return MaskNode(node);
        }

        public string? MaskText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    var node = JsonNode.Parse(text);
                    return MaskNode(node)?.ToJsonString(JsonOptions) ?? text;
                }
                catch (JsonException)
                {
                    return text;
                }
            }

            return text;
        }

        private JsonNode? MaskNode(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(pair => pair.Key).ToList())
                    {
                        if (_sensitive.Contains(key))
                        {
                            obj[key] = MaskValue;
                        }
                        else
                        {
                            MaskNode(obj[key]);
                        }
                    }
                    return obj;

                case JsonArray array:
                    foreach (var item in array)
                    {
                        MaskNode(item);
                    }
                    return array;

                default:
                    return node;
            }
        }
    }
}
