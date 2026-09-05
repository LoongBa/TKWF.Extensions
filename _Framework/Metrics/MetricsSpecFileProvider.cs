using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 规格文件提供者——按 <c>{SpecRoot}/{Domain}/{specKey}/metric-definitions.json</c> 解析并加载指标定义。
    /// <para>结构校验由 <see cref="MetricDefinitionLoader"/> 完成；D20 manifest 状态校验为 v0.2.0 占位（C2，不依赖 D20）。</para>
    /// </summary>
    public sealed class MetricsSpecFileProvider
    {
        private readonly MetricsOptions _options;

        /// <summary>构造规格文件提供者。</summary>
        public MetricsSpecFileProvider(IOptions<MetricsOptions> options)
        {
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 按域 + specKey 加载指标定义（文件不存在 → <see cref="MetricDefinitionException"/>）。
        /// </summary>
        /// <param name="domain">业务域（如 "merchant"）</param>
        /// <param name="specKey">规格 key（如 "PaymentLogStatView--daily-sales-trend"）</param>
        public IReadOnlyList<MetricDefinition> Load(string domain, string specKey)
        {
            var path = Path.Combine(_options.SpecRoot, domain, specKey, "metric-definitions.json");
            if (!File.Exists(path))
                throw new MetricDefinitionException(specKey, null, $"规格文件不存在：{path}");
            return MetricDefinitionLoader.Load(path);
        }
    }
}
