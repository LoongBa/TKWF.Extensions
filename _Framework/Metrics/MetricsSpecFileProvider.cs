using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 规格文件提供者——按 <c>{SpecRoot}/{Domain}/{specKey}/metric-definitions.json</c> 解析并加载指标定义。
    /// <para>结构校验由 <see cref="MetricDefinitionLoader"/> 完成；D20 manifest 状态校验为占位（C2，依赖 D20 落地、尚未实施——v0.2.0 未兑现）。</para>
    /// </summary>
    public sealed class MetricsSpecFileProvider
    {
        private readonly MetricsOptions _options;

        // ---------- 命名校验（B 档 M-01，对齐 Analytics 扩展 AnalyticsSpecConstants 同型正则——防路径遍历） ----------

        /// <summary>
        /// specKey 命名规范：<c>{VEntityName}--{intent}</c>——PascalCase--kebab-case。
        /// 正则：PascalCase（大写字母开头 + 字母数字）+ 双连字符 + kebab-case（小写字母开头 + 小写/数字/连字符）。
        /// 白名单字符集天然拒绝 <c>..</c>/<c>/</c>/<c>\</c>/冒号等路径穿越字符。
        /// </summary>
        private static readonly Regex SpecKeyPattern = new(
            @"^[A-Z][a-zA-Z0-9]*--[a-z][a-z0-9-]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// domain 命名规范：kebab-case 小写（目录名约定）。
        /// 正则：小写字母开头 + 小写/数字/连字符——同样拒绝路径分隔符/穿越段/冒号。
        /// </summary>
        private static readonly Regex DomainPattern = new(
            @"^[a-z][a-z0-9-]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>构造规格文件提供者。</summary>
        public MetricsSpecFileProvider(IOptions<MetricsOptions> options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
        }

        /// <summary>
        /// 按域 + specKey 加载指标定义（文件不存在 → <see cref="MetricDefinitionException"/>）。
        /// <para>domain/specKey 先经命名正则校验（B 档 M-01，对齐 Analytics 先例）——非法标识符抛
        /// <see cref="MetricDefinitionException"/>，拒绝路径拼接（防穿越）。</para>
        /// </summary>
        /// <param name="domain">业务域（kebab-case 小写，如 "merchant"）</param>
        /// <param name="specKey">规格 key（PascalCase--kebab-case，如 "PaymentLogStatView--daily-sales-trend"）</param>
        public IReadOnlyList<MetricDefinition> Load(string domain, string specKey)
        {
            if (string.IsNullOrWhiteSpace(domain) || !DomainPattern.IsMatch(domain))
                throw new MetricDefinitionException(specKey, null,
                    $"domain 命名非法：'{domain}'——必须为 kebab-case 小写（如 merchant；拒绝路径分隔符/穿越段/冒号等）");
            if (string.IsNullOrWhiteSpace(specKey) || !SpecKeyPattern.IsMatch(specKey))
                throw new MetricDefinitionException(specKey, null,
                    $"specKey 命名非法：'{specKey}'——必须匹配 {{PascalCase}}--{{kebab-case}}（如 PaymentLogStatView--daily-sales-trend）");

            var path = Path.Combine(_options.SpecRoot, domain, specKey, "metric-definitions.json");
            if (!File.Exists(path))
                throw new MetricDefinitionException(specKey, null, $"规格文件不存在：{path}");
            return MetricDefinitionLoader.Load(path);
        }
    }
}
