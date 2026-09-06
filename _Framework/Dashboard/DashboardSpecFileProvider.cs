using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 规格文件提供者——双路径加载（Oracle C1/C8）：
    /// <list type="bullet">
    /// <item><see cref="LoadDashboard"/>——Dashboard 定义 <c>{TKWF:Dashboard:SpecRoot}/{group}/{dashKey}.json</c>（group 缺省扁平）；</item>
    /// <item><see cref="LoadMetricsSpec"/>——Metrics 指标规格 <c>{TKWF:Metrics:SpecRoot}/{domain}/{specKey}/metric-definitions.json</c>，
    /// 经 <see cref="IConfiguration"/> 直读 <c>TKWF:Metrics:SpecRoot</c>（不引 TKWF.Ext.Metrics 扩展项目）。</item>
    /// </list>
    /// </summary>
    public sealed class DashboardSpecFileProvider
    {
        private const string DefaultMetricsSpecRoot = "docs/analytics-specs";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            // WidgetType 枚举用 camelCase（"numberContainer"/"chart"/"list"——对齐 ABP Low-Code 命名）
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private readonly DashboardOptions _options;
        private readonly IConfiguration _configuration;

        /// <summary>构造规格文件提供者。</summary>
        public DashboardSpecFileProvider(IOptions<DashboardOptions> options, IConfiguration configuration)
        {
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 按 dashKey 加载 Dashboard 定义（JSON 描述符反序列化 + 契约校验）。
        /// <para>路径：<c>{SpecRoot}/{group}/{dashKey}.json</c>；dashKey 含 '/' 且 group 缺省时按路径子目录解析。</para>
        /// </summary>
        /// <exception cref="DashboardDefinitionException">文件缺失 / JSON 损坏 / widgets 缺失 / Widget 名重复 / dataSource 缺失。</exception>
        public DashboardDefinition LoadDashboard(string dashKey)
        {
            if (string.IsNullOrWhiteSpace(dashKey))
                throw new DashboardDefinitionException(null, null, "dashKey 不能为空");

            var path = ResolveDashboardPath(dashKey);
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DashboardDefinitionException(dashKey, null, $"Dashboard 定义文件读取失败（{path}）：{ex.Message}");
            }

            DashboardDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<DashboardDefinition>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new DashboardDefinitionException(dashKey, null, $"Dashboard 定义 JSON 解析失败（{path}）：{ex.Message}");
            }

            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                throw new DashboardDefinitionException(dashKey, null, $"Dashboard 定义缺少 name（{path}）");

            ValidateWidgets(definition);
            return definition;
        }

        /// <summary>
        /// 按域 + specKey 加载 Metrics 指标定义（<c>{TKWF:Metrics:SpecRoot}/{domain}/{specKey}/metric-definitions.json</c>）。
        /// <para>结构校验由 <see cref="MetricDefinitionLoader"/> 完成；SpecRoot 经 <see cref="IConfiguration"/> 直读。</para>
        /// </summary>
        /// <exception cref="MetricDefinitionException">规格文件缺失/损坏（MetricDefinitionLoader 语义）。</exception>
        public IReadOnlyList<MetricDefinition> LoadMetricsSpec(string domain, string specKey)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new DashboardDefinitionException(null, null, "Metrics 规格 domain 不能为空");
            if (string.IsNullOrWhiteSpace(specKey))
                throw new DashboardDefinitionException(null, null, "Metrics 规格 specKey 不能为空");

            var metricsSpecRoot = _configuration["TKWF:Metrics:SpecRoot"];
            if (string.IsNullOrWhiteSpace(metricsSpecRoot))
                metricsSpecRoot = DefaultMetricsSpecRoot;

            var path = Path.Combine(metricsSpecRoot, domain, specKey, "metric-definitions.json");
            if (!File.Exists(path))
                throw new MetricDefinitionException(specKey, null, $"Metrics 规格文件不存在：{path}");
            return MetricDefinitionLoader.Load(path);
        }

        /// <summary>解析 Dashboard 定义文件路径（dashKey 含 '/' 时视为 group/dashKey 子路径，防路径穿越）。</summary>
        private string ResolveDashboardPath(string dashKey)
        {
            var safeKey = dashKey.Replace('\\', '/');
            var segments = safeKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new DashboardDefinitionException(dashKey, null, "dashKey 非法");

            var fileName = $"{segments[^1]}.json";
            if (Path.GetFileName(fileName) != fileName || fileName.Contains(':'))
                throw new DashboardDefinitionException(dashKey, null, $"dashKey 非法字符");

            var relative = segments.Length > 1
                ? Path.Combine(Path.Combine(segments[..^1]), fileName)
                : fileName;
            return Path.Combine(_options.SpecRoot, relative);
        }

        private static void ValidateWidgets(DashboardDefinition definition)
        {
            if (definition.Widgets == null || definition.Widgets.Count == 0)
                throw new DashboardDefinitionException(definition.Name, null, "Dashboard 定义缺少 widgets 列表");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var widget in definition.Widgets)
            {
                if (string.IsNullOrWhiteSpace(widget.Name))
                    throw new DashboardDefinitionException(definition.Name, null, "Widget 缺少 name");
                if (!seen.Add(widget.Name))
                    throw new DashboardDefinitionException(definition.Name, widget.Name, "Widget 名重复");
                if (string.IsNullOrWhiteSpace(widget.DataSource))
                    throw new DashboardDefinitionException(definition.Name, widget.Name, "Widget 缺少 dataSource（必选，Oracle C5）");
                if (widget.Width is < 1 or > 6)
                    throw new DashboardDefinitionException(definition.Name, widget.Name, $"Widget 宽度越界（{widget.Width}，须 1-6）");
            }
        }
    }
}
