# TKWF.Ext.Analytics 分析服务扩展技术规范

**状态**: 业务分析扩展 (Analytics Extension) | **版本**: V0.1.0 (骨架——门面/Options/Initializer) | **框架**: .NET 10

**核心约束**: 两层架构（核心加载校验在主框架 `TKW.Framework.Utility.Analytics` 零依赖 / 本包集成门面）、spec 文件驱动（git-tracked `docs/analytics-specs/`）、flint 单一真相（JsonDocument 透传不镜像强类型）、路径安全（domain/specKey 正则 + chartType 消毒）

---

## 一、需求分析 (Demand Analysis)

DMP-Lite 已有 6 个分析型 VEntity（销售趋势/支付分布/门店排行/品类分布/会员增长/会员来源），但**数据形态与可视化强绑定**——每个分析意图对应单一图表类型，运行期不可切换（D20 §1.2）。D20 设计确立 AnalysisSpec 产物规范 + flint-chart 渲染（单一真相归 flint schema），本扩展提供**框架级标准读取/校验路径**（`IAnalyticsQueryService` 门面）。

## 二、设计原理 (Design Principles)

本扩展采用 **"纯计算内核（主框架 Utility）+ TKWF 集成层（扩展包）"** 架构（对齐 Metrics 完整先例）。

### 1. 分层结构

- **核心（`TKW.Framework.Utility.Analytics`，主框架 Utility，v4.10.27+，零第三方依赖）**：`AnalyticsSpecLoader`（spec 文件加载 + JSON 解析）+ `AnalyticsSpecValidator`（manifest 状态校验）+ `AnalyticsSpecOptions`（纯 POCO）+ 3 异常（`SpecStaleException`/`ChartSpecLoadException`/`DataShapeMismatchException`，结构化属性）+ `AnalyticsSpecConstants`（目录约定 + 路径安全正则 + flint 版本锁定）。
- **集成层（`TKWF.Ext.Analytics`，本包）**：`IAnalyticsQueryService` 门面（4 方法）+ `AnalyticsQueryService`（无状态组合 Loader）+ `AnalyticsOptions`（`[Options("TKWF:Analytics")]` 派生 POCO + SpecRoot）+ `AnalyticsExtensionInitializer`（三钩子接线）。**消费方引此一包 + 白名单声明即得完整分析能力**。

### 2. 关键设计

- **JsonDocument 透传**：spec JSON 原样返回，不镜像强类型——flint schema 是单一真相（D20B §4.3），杜绝双 schema 漂移。
- **路径安全**：`domain`/`specKey` 命名正则（`AnalyticsSpecConstants`）+ chartType 文件名消毒——防路径遍历（Oracle P0-3）。
- **manifest 状态校验**：stale → `SpecStaleException`（强制重生）；deprecated → 放行（前端读 status 展示警告）；`GetManifestAsync` 不校验 status（状态展示载体，P1-4）。
- **flint 不硬编码**：核心不内置 47 模板名（第三真相源，P1-2）——chartType ∉ 注册表由前端 flint assemble 抛错；快照文档 `docs/D20-附-flint模板注册表快照.md` 作设计期参考。
- **一数多图归 flint pivot**：spec 只声明主图 + 可选 pivot-hint；可切换图型由 flint 前端按数据形态门控裁决（D20B §5.2 删除候选推导）。

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IAnalyticsQueryService`** | 门面——GetSemanticTypes / GetChartSpec / ListPivotChartTypes / GetManifest（domain+specKey 双参） | `AnalyticsQueryService`（组合 Loader，Singleton 无状态） |
| **`AnalyticsOptions`** | 扩展配置（`TKWF:Analytics` 节，继承 `AnalyticsSpecOptions` + SpecRoot） | 本扩展 |
| **`AnalyticsExtensionInitializer`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子 TryAddScoped + 工厂 lambda 桥接 Options→POCO） | 本扩展 |
| **`AnalyticsSpecLoader`** | spec 加载 + JSON 解析（核心） | 主框架 Utility |
| **`AnalyticsSpecValidator`** | manifest 状态校验（核心） | 主框架 Utility |
| **`SpecStaleException`** / **`ChartSpecLoadException`** / **`DataShapeMismatchException`** | 三类异常（结构化属性；DataShapeMismatch 仅定义不抛出） | 主框架 Utility |

## 四、配置 (Configuration)

`AnalyticsOptions` 绑定 `TKWF:Analytics` 配置节（SG1 `[Options]` 自动绑定 + 本扩展 AddOptions 兜底默认值）：

```json
{
  "TKWF": {
    "Analytics": {
      "SpecRoot": "docs/analytics-specs"
    }
  }
}
```

- **`SpecRoot`**：spec 根目录（默认 `docs/analytics-specs`，相对消费方仓库根）。

## 五、AnalysisSpec 产物 (Spec Files)

```
docs/analytics-specs/{Domain}/{specKey}/
├── README.md              # spec 概述
├── view-sql.sql           # ①-1 DDL（供 tkwf-entity 生成 VEntity）
├── data-contract.md       # ①-2 数据契约
├── pivot-hint.json        # ①-3 可切换图型提示（可选）
├── semantic_types.json    # ①-4a flint flat map（spec 内唯一）
├── chart_spec/*.json      # ①-4b 视觉模板（chartType 用 flint 模板注册表名）
└── manifest.json          # spec 元数据（specKey/version/sourceViewSqlHash/status/primaryChartSpecFile/fields）
```

- **specKey 命名**：`{VEntityName}--{intent}`（如 `PaymentLogStatView--daily-sales-trend`）。
- **semantic_types**：flint flat map，**不含自造 `$schema`**（D20B §4.3）；聚合语义在 chart_spec encoding 层。
- **chart_spec**：`chartType` 用 flint 模板注册表名（`"Line Chart"` 等，见使用指南 §六）。
- **生成工具**：`tkwf-analytics-view` skill（产物契约，见主框架 AC-Kit）。

## 六、消费方接线 (Usage)

```csharp
// ① 白名单声明（v4.9.85+）
[TKWFEnabledExtension(typeof(AnalyticsExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// ② 注入 + 使用
public class SalesAnalyticsService(IAnalyticsQueryService analytics)
{
    public async Task<JsonDocument> GetTrendSpecAsync(CancellationToken ct)
        => await analytics.GetSemanticTypesAsync("merchant", "PaymentLogStatView--daily-sales-trend", ct);
}

// ③ API 暴露（消费方 Domain Service 标 [GenerateController] 生成 REST 端点）
```

**失败模式**（D20 §10.4）：`SpecStaleException` → 提示重跑 tkwf-analytics-view；数据空 → "暂无数据"占位；渲染失败 → catch + 占位。渲染接线（flint `assembleECharts` + ECharts 按需导入）见使用指南 §三。

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前：骨架）
- 核心层（主框架 Utility.Analytics，v4.10.27 发布：Loader/Validator/Options/3 异常/Constants + 30 测试）
- 集成层（门面 4 方法 + Options + Initializer + 11 测试）
- flint 模板注册表快照（0.5.1：47 chartType + 5 后端矩阵 + 44 SemanticType + pivot 门控）
- tkwf-analytics-view skill 骨架（产物契约）
- **Oracle 评审**：主框架 v4.10.27 方案 4 P0 + 6 P1 + 4 P2 全吸收
- **待发布**：tag `Analytics/v0.1.0`（4.10.27 发布后，经双模式 `UseLocalFw=false` + CPM 4.10.27）

### V0.2.0+（候选）
- DMP-Lite 端到端样例：消费方接线（`[TKWFEnabledExtension]` + Domain Service + AdminWeb React flint 渲染）
- spec 样例库落地：`docs/analytics-specs/{Domain}/{specKey}/` 全套文件（PaymentLogStatView 5 意图）
- 消费方数据层 `DataShapeMismatchException` 校验接线
- spec 生命周期自动化（build 钩子 stale 标记，D20 Stage 3）

### 远期 / 评估
- tkwf-analytics-view 完整 AI 意图分解（D20 §9.1 边界）
- .NET chart compiler 替换 flint（性能对比评估，D20 Stage 4）
- 服务端像素渲染（flint-chart-mcp / Node 无头 / LiveCharts2 比选，出现实际需求再议）

---

**文档信息**: V0.1.0 | 2026-09-16 | 关联：D20-TKWF分析服务设计方案.md（主框架私有）、D20B-分析服务开源对标与语义层定位.md（主框架私有）、[v0.1.0-Analytics-分析服务-开发方案.md（公开）](../../docs/Analytics/v0.1.0-Analytics-分析服务-开发方案.md)、[分析服务扩展-使用指南](../../docs/Analytics/分析服务扩展-使用指南.md)、v4.10.27-D20分析服务Stage1落地-开发方案.md（主框架私有）
