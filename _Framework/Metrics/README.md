# TKWF.Ext.Metrics 业务指标计算引擎扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (指标结果持久化契约——IMetricResultStore) | **框架**: .NET 10

**核心约束**: 纯计算内核（无取数/无 ETL）、零第三方依赖、静态注册表零反射、顺序执行、规格文件驱动（git-tracked）、**指标结果持久化为契约层（扩展不内建实体/默认实现，实体归消费方）**

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

校验（加载时一次 + 引擎内缓存）：计算器名存在 / 指标名唯一 / JSON 合法 + `$schema` 匹配 / 字段名引用（`*Field` 值匹配数据行）/ 聚合值 ∈ {sum,count,avg}；D20 manifest 状态校验占位（`tk` 依赖 D20 落地）。

## 七、指标结果持久化（V0.2.0）

### 7.1 定位与边界

V0.2.0 为引擎输出引入**标准落库路径**——`IMetricResultStore` 契约 + `MetricResultRow`/`MetricResultQuery` 契约 DTO + `MetricResultMapper` 静态映射器。**关注 v0.1.0 方案 L67 的"消费方自行落库 → 标准路径"演进**：消费方获得一站式"计算 → 标准持久化操作"的落库路径。

**边界（ADR-Metrics-指标结果持久化契约与实体归属）**：
- ❌ 扩展**不注册默认 `IMetricResultStore` 实现**（实体形态消费方自定，扩展无法预知）。
- ❌ 扩展**不定义持久化实体**/SG1 接线/FreeSql 依赖（保持 ADR-Metrics 决策 1"扩展无持久化"）。
- ❌ 扩展**不做建表/CRUD/API**（这些是消费方实体的 SG1 职责）。
- ❌ `MetricResultMapper` **不处理 `MetricSlice`**（引擎已扁平化——`MetricsEngine.AddResult` 把 `MetricSlice[]` 展开为多个 `MetricResult`，映射器只做 1:1 投影，防御性切片展开是死代码，禁止引入）。
- ✅ 实体（Entity/VEntity）由**消费方**提供（`[DomainGenerateCode]` 接线 SG1 自动生成 DataService/DTO/API，零额外配置）。

### 7.2 契约组件

| **组件** | **职责** |
|----------|---------|
| **`IMetricResultStore`** | 指标结果持久化契约——`SaveAsync`/`QueryAsync`/`CountAsync`/`CleanupExpiredAsync` 四类标准化操作（签名围绕 `MetricResultRow`/`MetricResultQuery`，**无实体类型**） |
| **`MetricResultRow`** | 标准化行 DTO——`SpecKey`/`Name`/`Value`/`Unit`/`DimensionsJson`/`CalculatedAtUtc` |
| **`MetricResultQuery`** | 查询条件 DTO——`SpecKey`/`Name`/`FromUtc`/`ToUtc`/`Skip`/`Take`/`DimensionFilter` |
| **`MetricResultMapper`** | 静态映射器——`MetricResult` → `List<MetricResultRow>` 1:1 投影 + DimensionsJson 序列化（§7.3）+ `calculatedAtUtc` 注入（默认 `UtcNow`）；`JsonOptions` 暴露供消费方复用 |

### 7.3 DimensionsJson 序列化约定

- **序列化器**：`System.Text.Json.JsonSerializer`（BCL，零新增依赖）。
- **选项**：`MetricResultMapper.JsonOptions`——`{ DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false }`。⚠️ `WhenWritingNull` 对**字典项不生效**（仅 POCO 属性）——映射器内部先过滤 null 值维度再序列化。
- **`Dimensions == null`**（单值计算器无维度）→ `DimensionsJson = null`。
- **空字典** `{}` → `"{}"`。
- **null 维度值**（`{"bucket":"2026-08","segment":null}`）→ 省略为 `{"bucket":"2026-08"}`。
- **类型保真**：不保证反序列化类型保真（decimal→JSON number→可能变 double）——DimensionsJson 为**存储态不透明字符串**，消费方查询时按需解析（`JsonDocument.Parse`）。

### 7.4 消费方接线（三步）

```csharp
// ① 定义实体（消费方；SG1 自动生成 DataService/DTO/CRUD API）
[Table("SalesMetricResult")]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class SalesMetricResultEntity
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)] public long Id { get; set; }
    [FreeSql.DataAnnotations.Column(Position = 2)] [MaxLength(128)] public string? SpecKey { get; set; }
    [FreeSql.DataAnnotations.Column(Position = 3)] [MaxLength(128)] public string Name { get; set; } = "";
    [FreeSql.DataAnnotations.Column(Position = 4)] public string? ValueText { get; set; }   // 值（JSON 化）
    [FreeSql.DataAnnotations.Column(Position = 5)] [MaxLength(64)] public string? Unit { get; set; }
    [FreeSql.DataAnnotations.Column(Position = 6)] [MaxLength(2000)] public string? DimensionsJson { get; set; }
    [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)] public DateTime CalculatedAtUtc { get; set; }
    [FreeSql.DataAnnotations.Column(Position = 8, CanUpdate = false)] public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}

// ② DataService 分部写 public 委托方法（关键：xCodeGen 生成的转发方法为 internal——消费方 Store
//    与 DataService 跨程序集时不可见，须 public 包装；对齐 Notifications DataService.CreateAsync 先例）
partial class SalesMetricResultEntityDataService(IDomainUser user, IEntityDAC<SalesMetricResultEntity> dac)
    : DomainDataServiceBase<SalesMetricResultEntity, SalesMetricResultEntityDto>(user, dac, hasSoftDelete: false)
{
    public async Task CreateBatchAsync(IReadOnlyList<SalesMetricResultEntity> entities, CancellationToken ct = default)
        => await EntityCreateBatchAsync(entities, ct);

    public async Task<List<SalesMetricResultEntity>> QueryByMetricAsync(
        string? specKey, string? name, DateTime? fromUtc, DateTime? toUtc,
        int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(BuildPredicate(specKey, name, fromUtc, toUtc), skip, take,
            q => q.OrderByDescending(e => e.CalculatedAtUtc), ct);

    public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var total = 0;
        while (true)   // C3：循环分批清完（对齐 SecurityLogAnalyticsService）——best-effort，竞态由下次调用补删
        {
            var rows = await EntitySelectAsync(e => e.CalculatedAtUtc < cutoffUtc, 0, batchSize, ct: ct);
            if (rows.Count == 0) break;
            total += await EntityDeleteBatchAsync(rows.Select(r => r.Id), ct);
        }
        return total;
    }
}

// ③ 实现 IMetricResultStore（行→实体映射 + 委托 DataService public 方法——红线合规）
public sealed class SalesMetricResultStore : IMetricResultStore
{
    private readonly SalesMetricResultEntityDataService _ds;
    public SalesMetricResultStore(SalesMetricResultEntityDataService ds) => _ds = ds;

    public async Task<int> SaveAsync(IReadOnlyList<MetricResultRow> rows, CancellationToken ct = default)
    {
        var entities = rows.Select(r => new SalesMetricResultEntity
        {
            SpecKey = r.SpecKey, Name = r.Name,
            ValueText = r.Value switch          // P3：后引擎 Value 为标量（decimal/double/string/null）
            {
                null => null, string s => s,
                _ => System.Text.Json.JsonSerializer.Serialize(r.Value, MetricResultMapper.JsonOptions)
            },
            Unit = r.Unit, DimensionsJson = r.DimensionsJson, CalculatedAtUtc = r.CalculatedAtUtc
        }).ToList();
        await _ds.CreateBatchAsync(entities, ct);
        return entities.Count;
    }

    public async Task<IReadOnlyList<MetricResultRow>> QueryAsync(MetricResultQuery query, CancellationToken ct = default)
    {
        // Take 钳制到 [1,200] 静默（P4）→ 映射到 _ds.QueryByMetricAsync(query.SpecKey, query.Name,
        //   query.FromUtc, query.ToUtc, skip, take, ct) → 实体 → 行
        throw new NotImplementedException();
    }

    public Task<long> CountAsync(MetricResultQuery query, CancellationToken ct = default) => /* 同条件计数 */ throw new NotImplementedException();

    public Task<int> CleanupExpiredAsync(int retentionDays, int batchSize = 500, CancellationToken ct = default)
        => _ds.DeleteBeforeAsync(DateTime.UtcNow.AddDays(-retentionDays), batchSize, ct);
}

// ④ DI 注册（扩展不注册默认实现——消费方自行 TryAddScoped）
services.TryAddScoped<IMetricResultStore, SalesMetricResultStore>();
```

**关键接线约束**：
- **DataService public 委托方法**：xCodeGen 生成的转发方法（`EntityCreateBatchAsync`/`EntityDeleteBatchAsync` 等）为 **internal**——Store 与 DataService 同程序集（TKWF 标准布局）可见，跨程序集（Store 在 Application 层）不可见。**约定写 public 委托方法包装**（防御性约定，M1）。
- **Take 钳制（P4）**：`MetricResultQuery.Take` 默认 50，消费方 Store **MUST 钳制到 [1,200]**——超界静默钳制为 200、`Take<0` 视为 0。
- **DimensionFilter（P1）**：可选增强——Store 按需实现（SQL JSON 函数或内存过滤）；未实现时返回未按维度过滤的结果（兼容降级）。
- **清理调度**：`CleanupExpiredAsync(retentionDays, batchSize=500)` 调用方需按需定时触发（BackgroundJobs/Quartz 每日推荐）——扩展不内建调度器。

### 7.5 测试宿主（仓库内验证）

本扩展测试项目首次在测试宿主内定义 `[DomainGenerateCode]` 实体（模拟消费方接线），**走标准管线（2026-09-14）**：`TestMetricResultEntity`（手写定义 `Entities\`）→ SG1 分析器产生元数据 + xCodeGen（`.xCodeGen\extensions\metrics-tests.xCodeGen.json`）生成 `TestMetricResultEntity.g.cs`/`Dto.g.cs`/`Conditions.g.cs`/`DataService.g.cs`（internal 原子转发 `EntityCreateBatchAsync`/`EntitySelectAsync`/`EntityDeleteBatchAsync`）→ 手写 DataService 分部只编写业务方法（public 委托包装，对齐扩展项目 AuditLogEntityDataService.cs + .g.cs 分部对）；宿主 `ConsumerHostInitializer` 用 SG1 生成的 `ProjectMetaContext`（消费方真实形态，含 ADR61 DataService 自动注册）；`TestMetricResultStore`（委托 DataService public 方法，红线合规）+ SQLite 内存库真实 `FreeSqlEntityDAC<T>` 驱动全链路测试（15 用例）。

## 八、架构演进路线 (Architecture Roadmap)

### V0.1.0
- 核心计算内核（主框架 `TKW.Framework.Utility.Metrics`，19 文件，零第三方依赖）
- 6 内置计算器（Repurchase/Retention/Cohort/Funnel/TimeBucket/Ratio）
- 多切片输出约定（`MetricSlice` + 引擎展开）
- 扩展集成层（初始器 + Options + SpecFileProvider），消费方一包快速拼接
- **70/70 测试全绿**（核心计算纯单测 + 扩展集成；扩展回归 436/436 全绿）
- **Oracle 双审通过**：开发方案 PASS WITH CONDITIONS（C1/C2/C3 + M1-M4 + Minor#1-7 落实）+ 代码审核 PASS WITH CONDITIONS（C-1 阻塞项 + Issue 1-6 处理）；`MetricResult.Value` 为 `object?`（Oracle Issue#1 修正）
- **已发布**：tag `Metrics/v0.1.0`（2026-09-06）；消费方验证后按反馈修补

### V0.2.0（当前：指标结果持久化契约）
- **`IMetricResultStore` 契约**（保存/查询/计数/清理四类标准化操作，签名围绕 `MetricResultRow`/`MetricResultQuery`，无实体类型）+ **`MetricResultMapper` 静态映射器**（1:1 投影 + DimensionsJson 序列化 + `calculatedAtUtc` 注入）+ **`MetricResultRow`/`MetricResultQuery` 契约 DTO**
- **实体归消费方（ADR 决策 1）**：扩展不内建实体/不注册默认 Store 实现/不做建表 CRUD；消费方 `[DomainGenerateCode]` 实体 + DataService public 委托方法 + Store 行映射即接上标准落库路径
- **3C+5P 全部落实**（Oracle 评审 PASS WITH CONDITIONS）：C1 引擎已扁平化 Mapper 不处理 MetricSlice / C2 测试项目 SG1/FreeSql 接线 / C3 CleanupExpired 循环分批清完 + 竞态容忍 / P1 DimensionFilter 可选降级 / P2 calculatedAtUtc 注入 / P3 Value 后引擎标量不变量 / P4 Take 钳制 [1,200] / P5 DimensionsJson 序列化约定 + JsonOptions 复用
- **测试**：84/84 全绿（v0.1.0 70 + v0.2.0 14——MapperTests 6 + StoreTests 8，SQLite 内存全链路）
- **政策**：v0.1.0 自落库路径继续可用，不强制迁移

### V0.2.1+（候选）
- 消费方自定义计算器**全链路验证**：DI 覆盖 `IMetricCalculatorFactory`（TryAdd 语义）/ 包装默认 `CalculatorFactory`（**无必要勿增 SG**——自定义已有 DI 覆盖方案，不构成 SG 必要性；SG 仅在真实必要性出现时评估，且须权衡消费方配置复杂度/对接管线成本）
- 规格示例库落地：`docs/analytics-specs/{Domain}/{specKey}/metric-definitions.json` git-tracked 示例规格（支付 + tag 数据真实字段）
- **Funnel 时间缺失行为定案**（Oracle Issue#6）：提供 `timeField` 但某行时间 null → 排除 null 事件或显式报错
- D20 manifest 状态校验激活（自建 `MetricSpecStaleException`，零 D20 依赖；**依赖 D20 落地**——tkwf-analytics-view skill 尚未实施）
- 计算器扩展（DMP-Lite 需求驱动）：多周期留存曲线、复杂漏斗变体、新经营指标

### 远期 / 评估
- 性能/并发冒烟：大数据集（10 万+ 行）× 多定义性能基线（构建期访问器缓存已设计，无基准数字）
- 消费方 Options 绑定真实验证：`[Options("TKWF:Metrics")]` SG 自动绑定在消费方 IConfiguration 生效（当前测试仅覆盖 AddOptions 默认值）
- 定时触发重算：DMP-Lite 每日指标重算可用 `IBackgroundJobManager` 包装（框架组 BackgroundJobs 补齐后；Metrics 侧零改动）

---

**文档信息**: V0.2.0 | 2026-09-14 | 关联：D21-TKWF指标引擎-MetricsEngine-设计记录.md、v0.1.0-Metrics-业务指标计算引擎-开发方案.md、[v0.2.0-Metrics-指标结果持久化-开发方案.md（主框架私有）](../../../_TKWF/docs/03_扩展模块/Metrics/v0.2.0-Metrics-指标结果持久化-开发方案.md)、ADR-Metrics-指标结果持久化契约与实体归属（主框架私有）、[指标扩展-使用指南](../../docs/Metrics/指标扩展-使用指南.md)
