# TKWF.Ext.Metrics 业务指标计算引擎扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (规格驱动的复合业务指标计算) | **框架**: .NET 10

**核心约束**: 纯计算内核（无取数/无 ETL）、零第三方依赖、静态注册表零反射、顺序执行、规格文件驱动（git-tracked）

---

## 一、需求分析 (Demand Analysis)

DMP-Lite 已有 6 个分析型 VEntity（销售趋势/支付分布/门店排行/品类分布/会员增长/会员来源），覆盖一级 SQL 聚合 + 二级 LINQ 再聚合。但**复购率、留存、漏斗、时段桶、比率**等**复合业务指标**，SQL/LINQ 不易表达或需规格驱动，现有链路无统一计算机制。

- **消费场景**：基于支付数据（PaymentLog）+ 提取的 tag 数据计算经营指标（复购率、客单价、销售趋势、留存），数据导入后重算更新。
- **历史教训**：主框架 `_Extensions/DMPCore`（StatEngine）为前期 POC **失败品**（Oracle 2026-09-04 裁定：设计思路与代码均不构成依据，废弃归档，从零设计——本扩展不承接其代码）。

## 二、设计原理 (Design Principles)

本扩展采用 **"纯计算内核（主框架 Utility）+ TKWF 集成层（扩展包）"** 架构。

### 1. 分层结构

- **核心计算（`TKW.Framework.Utility.Metrics`，主框架 Utility，零第三方依赖）**：`IMetricsEngine`/`MetricsEngine` + `IMetricCalculator`（**非泛型**）+ `MetricRow`（**委托字段访问**）+ 6 内置计算器 + `CalculatorFactory`（**静态注册表零反射**）+ `MetricDefinitionLoader`（规格加载/校验）。
- **集成层（`TKWF.Ext.Metrics`，本包）**：`MetricsExtensionInitializer`（DI 接线）+ `MetricsOptions`（`[Options]` 绑定 + SpecRoot）+ `MetricsSpecFileProvider`（规格文件存取）。**消费方引此一包 + 白名单声明即快速拼接完整指标能力**。

> **为何核心计算在主框架 Utility**：零第三方依赖的纯计算内核，对齐 ADR52 Tagging 收纳先例（标签算法回归 `TKW.Framework.Utility.Tags`）。接口与实现同置 Utility，**不设 Abstractions 独立项目**（当前无跨扩展依赖场景）。核心计算非 TKWF 项目亦可引用 `TKWF.Utility` 包使用。

### 2. 关键设计

- **非泛型计算器 + MetricRow 委托访问**：`IMetricCalculator.Calculate(IReadOnlyList<MetricRow>, MetricDefinition)`——零反射实例化，字段名来自规格文件本就运行时配置；引擎按类型 T 构建期编译字段访问委托并缓存（运行期零反射）。
- **静态注册表零反射**：`CalculatorFactory` 用编译期已知的 `Dictionary<string, IMetricCalculator>` 纯查找，无 `MakeGenericType`/`Activator.CreateInstance`。
- **顺序执行**：`foreach` 顺序计算（删 `Task.Run`+`WaitAll`，AOT 友好）；引擎无状态 → Singleton 安全，定义校验/访问器缓存跨请求复用。
- **规格驱动 + 严格失败**：`metric-definitions.json`（git-tracked）驱动计算；`MissingCalculatorBehavior.Throw`/`MissingFieldBehavior.Throw` 默认——规格错误显式暴露，运行时失败优于静默产出错误指标。

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IMetricsEngine`** | 指标引擎——按规格顺序计算，纯计算不含取数 | `MetricsEngine`（主框架 Utility） |
| **`IMetricCalculator`** | 计算器（非泛型，返回单个 `MetricResult`） | 6 内置计算器（见 §四） |
| **`MetricRow`** | 数据行（非泛型，字段经委托访问，`Get<T>` 无约束泛型） | 内置 |
| **`MetricDefinition`** | 指标定义（Name/Calculator/扁平键 Parameters） | 内置 record |
| **`MetricResult`** | 指标结果（Name/Value/Unit/Dimensions，可序列化为 D20 data.values；**Value 可为 null**——分母 0/空数据/全 null 桶） | 内置 record |
| **`MetricSlice`** | 指标切片（多切片计算器的 Value 元素，C1） | 内置 record |
| **`IMetricCalculatorFactory`** | 计算器工厂（静态注册表零反射） | `CalculatorFactory` |
| **`MetricsEngineOptions`** | 执行参数（纯 POCO：超时/失败行为/对齐标志） | 内置（可派生） |
| **`MetricDefinitionLoader`** | 规格文件加载 + 结构校验 | 内置 static |
| **`MetricDefinitionException`** | 规格异常（specKey + 指标名 + 原因） | 内置 |
| **`MetricsOptions`** | 扩展配置（`TKWF:Metrics` 节，继承 `MetricsEngineOptions` + SpecRoot） | 本扩展 |
| **`MetricsSpecFileProvider`** | 规格文件存取（SpecRoot 解析 + 加载） | 本扩展 |
| **`MetricsExtensionInitializer`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子） | 本扩展 |

## 四、内置计算器 (Built-in Calculators)

| 注册名 | 计算器 | 场景 | 输出形态 |
|--------|--------|------|:---:|
| `repurchase-rate` | RepurchaseRateCalculator | 复购率 = 首购后窗口内复购用户 / 有单用户（缺省全量 = ≥2 单/≥1 单） | 单值 |
| `retention` | RetentionRateCalculator | 留存率 = 首日锚点单周期留存（D+retentionDays 仍活跃 / 初始） | 单值 |
| `cohort-retention` | CohortRetentionCalculator | 同期群留存矩阵（首次出现分群 + 偏移交集） | 多切片 |
| `funnel` | FunnelConversionCalculator | 漏斗转化（有序子序列匹配，逐步骤转化率） | 多切片 |
| `time-bucket` | TimeBucketAggregateCalculator | 时段桶聚合（hour/day/week/month [+groupField]） | 多切片 |
| `ratio` | RatioCalculator | 两聚合之比（如客单价 = sum/count；分母 0 → null） | 单值 |

> **多切片约定（C1）**：Cohort/TimeBucket/Funnel 返回 `Value = MetricSlice[]`（每切片含自身 Dimensions + 值），引擎展开为多个 `MetricResult`——`CalculateAsync` 输出保持扁平 `IReadOnlyList<MetricResult>`，可直接作为 D20 `data.values` 行消费。

## 五、配置 (Configuration)

`MetricsOptions` 绑定 `TKWF:Metrics` 配置节（SG1 `[Options]` 自动绑定 + 本扩展 AddOptions 兜底默认值）：

```json
{
  "TKWF": {
    "Metrics": {
      "SpecRoot": "docs/analytics-specs",
      "CalculateTimeout": "00:00:30",
      "MissingCalculatorBehavior": "Throw",
      "MissingFieldBehavior": "Throw",
      "AlignToD20DataValues": true
    }
  }
}
```

- **`SpecRoot`**：规格文件根目录（默认 `docs/analytics-specs`，相对消费方仓库根；规格存取接入）。
- **执行参数**：继承自 Utility `MetricsEngineOptions`（超时/失败行为/对齐标志）。
- **`AlignToD20DataValues`**：v0.1.0 为保留标志（no-op），D20 `data.values` 行映射由消费方负责。

## 六、规格文件 (Spec Files)

目录：`{SpecRoot}/{Domain}/{specKey}/metric-definitions.json`（git-tracked，对齐 D20 spec 目录）。

```json
{
  "$schema": "tkwf-metrics-definitions/v1",
  "specKey": "PaymentLogStatView--daily-sales-trend",
  "metrics": [
    { "name": "repurchase-rate-30d", "calculator": "repurchase-rate",
      "parameters": { "userIdField": "MemberId", "orderTimeField": "BizDate", "windowDays": "30" } },
    { "name": "aov-cny", "calculator": "ratio",
      "parameters": { "numeratorField": "TotalAmount", "numeratorAggregate": "sum",
                      "denominatorField": "PaidCount", "denominatorAggregate": "count" } }
  ]
}
```

校验（加载时一次 + 引擎内缓存）：计算器名存在 / 指标名唯一 / JSON 合法 + `$schema` 匹配 / 字段名引用（`*Field` 值匹配数据行）/ 聚合值 ∈ {sum,count,avg}；D20 manifest 状态校验为 v0.2.0 占位。

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 核心计算内核（主框架 `TKW.Framework.Utility.Metrics`，19 文件，零第三方依赖）
- 6 内置计算器（Repurchase/Retention/Cohort/Funnel/TimeBucket/Ratio）
- 多切片输出约定（`MetricSlice` + 引擎展开）
- 扩展集成层（初始器 + Options + SpecFileProvider），消费方一包快速拼接
- **70/70 测试全绿**（核心计算纯单测 + 扩展集成；扩展回归 436/436 全绿）
- **Oracle 双审通过**：开发方案 PASS WITH CONDITIONS（C1/C2/C3 + M1-M4 + Minor#1-7 落实）+ 代码审核 PASS WITH CONDITIONS（C-1 阻塞项 + Issue 1-6 处理）；`MetricResult.Value` 为 `object?`（Oracle Issue#1 修正）

### V0.2.0（规划）
- 消费方自定义计算器：**DI 覆盖 `IMetricCalculatorFactory`（TryAdd 语义）/ 包装默认 `CalculatorFactory`**（2026-09-06 修订：**无必要勿增 SG**——消费方自定义已有 DI 覆盖方案，不构成 SG 必要性；SG 仅在真实必要性出现时评估，且须权衡消费方配置复杂度/对接管线成本）
- D20 manifest 状态校验激活（自建 `MetricSpecStaleException`，零 D20 依赖）
- 指标结果持久化评估（`IMetricResultStore`，若 DMP-Lite 出现集中管理需求）
- 多周期留存曲线 / 复杂漏斗变体（DMP-Lite 需求驱动）

---

**文档信息**: V0.1.0 | 2026-09-06 | 关联：D21-TKWF指标引擎-MetricsEngine-设计记录.md、v0.1.0-Metrics-业务指标计算引擎-开发方案.md、[指标扩展-使用指南](../../docs/Metrics/指标扩展-使用指南.md)
