# TKWF.Ext.Dashboard 仪表盘数据服务扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (Metrics 展示层数据服务) | **框架**: .NET 10

**核心约束**: 消费 `TKW.Framework.Utility.Metrics` 复合指标（不定义/不计算/不存储指标）、JSON 描述符驱动（git-tracked）、**不引入图表库**（UI 渲染归消费方）、数据源抽象（`IDashboardDataProvider`）、严格失败

---

## 一、需求分析 (Demand Analysis)

Metrics v0.1.0 已提供复合业务指标计算能力（复购率/留存/漏斗/同期群/时段桶/比率），但消费方要展示这些指标时缺少"按业务语义组织指标 + 按 Widget 查询"的数据层——每个消费方各自拼装定义/取数/计算/组装逻辑，重复且易错。ABP/Orchard Core 均无此能力（ABP Low-Code Dashboard 是实体字段聚合，非指标语义；Orchard AdminDashboard 是 CMS 内容项快捷方式）。

- **消费场景**：DMP-Lite 经营指标看板——把多个指标（今日营业额、复购率、漏斗转化、留存同期群）组织成 KPI 卡片与图表布局，按统一入口查询展示数据。
- **差异化定位**：TKWF Dashboard = **Metrics 复合指标语义层 + 展示数据服务**——ABP/Orchard 都不解决"如何展示复购率/留存/漏斗"。
- **ADR 裁定**：v0.1.0 只做**服务端数据服务**（定义 + Widget 数据查询），**不引入图表库/不做 UI 渲染/不做布局持久化**——图表渲染与布局交互归消费方（UI 框架中立 + 规避 Blazor Server 图表库限制 + 无必要勿增）。

## 二、设计原理 (Design Principles)

### 1. 分层结构

- **定义模型**：`DashboardDefinition`（name/title/group/widgets[]）+ `DashboardWidgetDefinition`（name/type/row/order/width/dataSource/metricRef?）——JSON 描述符（git-tracked）反序列化。
- **数据源抽象**：`IDashboardDataProvider`——消费方实现取数，返回 `(IReadOnlyList<object> Rows, Func<object,string,object?> Accessor)` 元组（Oracle C2：accessor 随行返回，规避 Metrics 泛型 T 丢失）。
- **Metrics 消费**：`metricRef`（`"domain/specKey:metricName"` 或 `"domain/specKey"` 全量）→ `DashboardSpecFileProvider.LoadMetricsSpec`（经 `IConfiguration` 直读 `TKWF:Metrics:SpecRoot`，Oracle C1）→ `IMetricCalculatorFactory` → `IMetricCalculator.Calculate`（**不经 `CalculateAsync<T>`**——Oracle C2：直接构造 `MetricRow` 注入 accessor，绕过引擎按 T 编译访问器的限制）。
- **响应 DTO**：`WidgetDataResult`——统一三类输出形态（Oracle C3）：numberContainer→Value、chart→Slices（多切片展开）、list→Rows。

### 2. 关键设计

- **取数 → 计算链式**（Oracle 建议 2）：Widget 必须声明 `dataSource`（必选）；`metricRef` 可选——声明则对 dataSource 返回的数据行做 Metrics 计算。非互斥二选一，是链式组合。
- **双规格根目录**（Oracle C1）：Dashboard 定义走 `TKWF:Dashboard:SpecRoot`（`{SpecRoot}/{group}/{dashKey}.json`）；Metrics 指标规格走 `TKWF:Metrics:SpecRoot`（IConfiguration 直读，不引 Metrics 扩展项目——ADR50 L2 门控零违规）。
- **运行期依赖 fail-fast**（Oracle C9/建议 1）：metricRef Widget 依赖 `IMetricCalculatorFactory`（Metrics 扩展注册）——未启用时首次查询抛 `DashboardDefinitionException`（清晰消息注明启用方式）。
- **严格失败**：定义缺失/JSON 损坏/Widget 名重复/dataSource 缺失/metricRef 格式非法 → `DashboardDefinitionException`（对齐 Metrics 失败哲学）。

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`DashboardDefinition`** | Dashboard 定义（name/title/group/widgets[]） | 内置 record |
| **`DashboardWidgetDefinition`** | Widget 定义（type/row/order/width/dataSource/metricRef?） | 内置 record |
| **`DashboardWidgetType`** | Widget 类型枚举（NumberContainer/Chart/List） | 内置 |
| **`WidgetDataResult`** | Widget 数据响应 DTO（Value/Slices/Rows 统一三形态） | 内置 record |
| **`IDashboardDataProvider`** | 数据源抽象（消费方实现，返回 rows + accessor） | 消费方实现 |
| **`IDashboardDataService`** | 门面（定义查询 + Widget 数据查询） | `DashboardDataService`（Scoped） |
| **`DashboardSpecFileProvider`** | 规格加载（Dashboard 定义 + Metrics 指标规格双路径） | 内置（Singleton） |
| **`DashboardOptions`** | 配置（`TKWF:Dashboard` 节：SpecRoot） | 内置 |
| **`DashboardDefinitionException`** | 定义/配置异常（dashKey + widgetName + reason） | 内置 |
| **`DashboardExtensionInitializer<TUserInfo>`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子 DI 接线） | 本扩展 |

## 四、数据流 (Data Flow)

```
消费方 UI ──GetWidgetDataAsync(dashKey, widgetName, filters)──▶ DashboardDataService
     │
     ├─① 定义加载：DashboardSpecFileProvider.LoadDashboard(dashKey)
     │     └─ {TKWF:Dashboard:SpecRoot}/{group}/{dashKey}.json（git-tracked 描述符）
     │
     ├─② 数据源取数：IDashboardDataProvider.GetDataAsync(widgetName, filters)
     │     └─ 返回 (IReadOnlyList<object> Rows, Func<object,string,object?> Accessor)
     │
     ├─③ （可选）Metrics 计算：metricRef = "domain/specKey:metricName"
     │     ├─ DashboardSpecFileProvider.LoadMetricsSpec（IConfiguration 直读 TKWF:Metrics:SpecRoot）
     │     ├─ MetricDefinitionLoader 解析 → 按 metricName 筛选
     │     ├─ IMetricCalculatorFactory.TryCreate(calculator) → IMetricCalculator
     │     └─ calculator.Calculate(MetricRow 列表, definition) → MetricResult（多切片展开）
     │
     └─④ 组装 WidgetDataResult 响应（numberContainer→Value / chart→Slices / list→Rows）
```

**Widget 数据组装规则**：
- `dataSource`（必选）：消费方 `IDashboardDataProvider` 名——取数源。
- `metricRef`（可选）：`"domain/specKey:metricName"`（特定指标）或 `"domain/specKey"`（全量——chart 多指标展示）。
- `filters`（全局日期范围等）：透传给数据源取数（消费方在 GetDataAsync 内按 filters 过滤）；指标计算作用于过滤后的数据行。

## 五、配置 (Configuration)

`DashboardOptions` 绑定 `TKWF:Dashboard` 配置节（SG1 `[Options]` 自动绑定 + 本扩展 AddOptions 兜底默认值）：

```json
{
  "TKWF": {
    "Dashboard": {
      "SpecRoot": "docs/dashboard-specs"
    },
    "Metrics": {
      "SpecRoot": "docs/analytics-specs"
    }
  }
}
```

- **`TKWF:Dashboard:SpecRoot`**：Dashboard 定义 JSON 根目录（默认 `docs/dashboard-specs`）。
- **`TKWF:Metrics:SpecRoot`**：指标规格根目录（Metrics 扩展配置；Dashboard 经 `IConfiguration` 直读，**不重复配置**——零冗余）。

## 六、Dashboard 定义文件 (Definition Files)

目录：`{SpecRoot}/{group}/{dashKey}.json`（group 缺省 = 扁平 `{SpecRoot}/{dashKey}.json`）。

```json
{
  "name": "business-overview",
  "title": "经营总览",
  "group": "operations",
  "widgets": [
    {
      "name": "today-revenue",
      "type": "numberContainer",
      "row": 0, "order": 0, "width": 1,
      "dataSource": "payment-ds",
      "metricRef": "merchant/payment-metrics:total-revenue"
    },
    {
      "name": "daily-trend",
      "type": "chart",
      "row": 1, "order": 0, "width": 2,
      "dataSource": "payment-ds",
      "metricRef": "merchant/payment-metrics:daily-trend"
    },
    {
      "name": "payment-list",
      "type": "list",
      "row": 2, "order": 0, "width": 2,
      "dataSource": "payment-ds"
    }
  ]
}
```

**契约校验**（加载时一次）：name 非空 / widgets 非空 / Widget 名唯一 / `dataSource` 非空（必选）/ `width` ∈ [1,6] / `metricRef` 格式合法。

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 定义模型 + `IDashboardDataService` + `IDashboardDataProvider` + `WidgetDataResult`
- Metrics 消费接线（`IMetricCalculator` 直连，规避泛型摩擦）
- 双规格路径（Dashboard 定义 + Metrics 指标规格，IConfiguration 直读）
- 运行期依赖 fail-fast + 严格失败
- 25/25 测试全绿
- **不引入图表库/不做 UI/不做布局持久化**（ADR 裁定）

### V0.2.0（候选，能力完善）
- 布局持久化 + 操作者可编辑（DB 存储 + 管理 UI + 权限管理）
- 结果缓存/预聚合（重复查询避免重复计算）
- 可选 Blazor 组件包（独立 `TKWF.Ext.Dashboard.UI`，不污染核心数据服务）
- 定时刷新调度
- 下钻导航（clickToSeeRecords）

### 远期 / 评估
- 数据源直连增强（消费方未物化数据时的查询能力）
- 与 Reporting（报表设计器）/Analytics（数据分析）模块协同边界

---

**文档信息**: V0.1.0 | 2026-09-06 | 关联：ADR-Dashboard-定位与数据契约.md、v0.1.0-Dashboard-仪表盘数据服务-开发方案.md（主框架私有）、[仪表盘扩展-使用指南](../../docs/Dashboard/仪表盘扩展-使用指南.md)
