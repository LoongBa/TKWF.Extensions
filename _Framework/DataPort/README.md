# TKWF.Ext.DataPort 数据导入导出扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 | **框架**: .NET 10

**核心约束**: 三层架构（核心运行库 + MiniExcel Provider + 扩展模块带持久化）、批次切分算法在核心、FileHash 幂等检查（R3）、消费方自管业务数据持久化（R2）、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

数据导入导出（Excel/CSV 等）是企业应用高频横切能力——但长期散落在业务代码中（静态工具类 + 硬编码映射）。TKWF 以 **1 行配置 + 1 个适配器** 的消费方集成模式（对齐优化 ExcelTools `IImportDataAdapter`）提供统一导入导出，包发布到 `TKWF.Ext.*` 扩展仓库。

- **消费场景**：DMP-Lite 商家账单/对账文件导入（美团/支付宝等第三方 CSV 对账）、运营数据导出。
- **集成模式（R2/R2'）**：消费方派生 `ImportAdapterBase<T>` 实现钩子（ColumnMapping/OnValidate/OnBatchWrite），核心 `ImportService` 调度批次并调用钩子——消费方在 `OnBatchWrite` 内自管业务数据持久化（DataPort 不感知/不持久化消费方业务实体）。
- **批次记录（扩展模块）**：`DataImportRecordEntity`（SG1 `[DomainGenerateCode]`）记录每次导入的批次号/文件哈希/状态/统计/错误摘要——`FileHash` 唯一索引防重复导入（幂等），`BatchNo` 供消费方按批次回滚。

## 二、设计原理 (Design Principles)

本扩展采用 **"核心运行库（Utility.DataPort，零第三方依赖）+ MiniExcel Provider（可选包）+ 扩展模块带持久化（本包）"** 三层架构（ADR-DataPort 裁定）。

### 1. 分层结构

- **核心运行库** `TKW.Framework.Utility.DataPort`（主框架 Utility，编入 TKWF.Utility.dll）——导入/导出流程算法：列映射 → 类型转换（委托缓存）→ 验证三态（Keep/Skip/Terminate）→ **批次切分**（每 BatchSize 行回调 onBatch）→ 错误收集（行级 `ImportFailure` + 批次级 `BatchFailure`）。
- **MiniExcel Provider** `TKWF.Utility.DataPort.Providers.MiniExcel`（Apache-2.0 读写引擎，可选引入）——流式读 xlsx/csv → `ImportRow`（非泛型，Provider 不感知 T）；列表 → xlsx/csv 写流。只读场景可不引用。
- **扩展模块**（本包）——`DataPortExtensionInitializer`（DI 接线）+ `DataPortOptions`（`TKWF:DataPort`）+ `DataImportRecordEntity`（SG1 实体）+ `IDataImportTaskService`（导入执行 + 批次记录落库 + FileHash 幂等 + 状态跟踪）。

### 2. 关键设计

- **批次切分语义（核心）**：Provider 流式返回行 → Service 累积每 `BatchSize` 行触发一次 `onBatch`（含 batchIndex + startRowIndex）；末批余数显式 flush（空文件 → 0 批 0 回调）；批次回调异常 → `BatchFailure` 收集 + `StopOnBatchFailure` 终止/继续；`ImportResult.SuccessCount` = 验证通过数（独立于批次持久化，Oracle C3）。
- **模板方法模式（R2'）**：`ImportAdapterBase<T>` 基类——不同导入各一派生类（如 `PaymentImportAdapter : ImportAdapterBase<PaymentLog>`），便于管理与维护；改善 ExcelTools 静态工具类 + 接口混职责问题。
- **幂等检查（R3）**：`FileHash`（SHA256）唯一索引——同文件二次导入 → Processing/Succeeded 返回已有批次（拒绝）；Failed 批次 → 重置为 Processing 允许重导。`BatchNo` 为消费方回滚钩子入口。
- **状态跟踪**：Processing → Succeeded / Failed / PartiallySucceeded（`BatchFailures.Count > 0`），错误摘要取前 5 个批次失败消息拼接。

### 3. 与主框架的关系

- `IImportService`/`IExportService`/`IImportProvider`/`IExportProvider`/`ImportAdapterBase<T>`/`ImportResult` 等全部由核心运行库定义（不动）。
- 本扩展提供 `DataPortExtensionInitializer`（DI 接线）+ `IDataImportTaskService`（批次任务）+ `DataImportRecordEntity`（SG1 声明式实体）。
- 消费方白名单启用（V4.9.85+）：`[TKWFEnabledExtension(typeof(DataPortExtensionInitializer<>))]`。

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.DataPort` 包，扩展经 `[TKWFExtension]` 被 SG1 发现（能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(DataPortExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`IImportService`/`IExportService`（默认核心实现）+ `IImportProvider`/`IExportProvider`（默认 MiniExcel）+ `IDataImportTaskService`（默认批次任务实现）+ `DataPortOptions`（`TKWF:DataPort` 节）。

### 2. 消费方派生适配器 + 导入（R2' 推荐）

```csharp
// 消费方派生 ImportAdapterBase<T>——不同导入各一派生类，便于管理与维护
public sealed class PaymentImportAdapter : ImportAdapterBase<PaymentLog>
{
    public override string DataSourceName => "美团支付";
    public override IReadOnlyDictionary<string, string> ColumnMapping { get; } =
        new Dictionary<string, string> { ["订单号"] = "OrderNo", ["金额"] = "TotalAmount" };

    protected override ImportRowAction OnValidate(int rowIndex, IReadOnlyDictionary<string, object?> row, PaymentLog entity)
        => entity.TotalAmount > 0 ? ImportRowAction.Keep : ImportRowAction.Skip;

    public override Task OnBatchWrite(IReadOnlyList<PaymentLog> batch, CancellationToken ct)
        => _db.Insert(batch).ExecuteAffrowsAsync(ct);   // 消费方自管持久化
}
```

```csharp
// 注入 + 执行（批次记录落库 + 幂等检查 + 状态跟踪）
public class PaymentImporter(IDataImportTaskService taskService)
{
    public async Task<DataImportTaskResult> ImportAsync(string filePath)
    {
        var result = await taskService.ImportAsync(filePath, new PaymentImportAdapter(_db));
        // result.RecordId / result.BatchNo / result.ImportResult（SuccessCount + Failures + BatchFailures）
        return result;
    }
}
```

### 3. 简单回调入口（definition 模式）

`IImportService.ImportAsync(filePath, definition, onBatch)`——不需要派生适配器即可用；`onBatch` 为批次写库回调 (batchIndex, startRowIndex, batch, ct)。

### 4. 导出

```csharp
public class PaymentExporter(IExportService exportService)
{
    public async Task ExportAsync(Stream output, IReadOnlyList<PaymentLog> rows)
    {
        var definition = new ExportDefinition<PaymentLog>(
            new Dictionary<string, string> { ["OrderNo"] = "订单号", ["TotalAmount"] = "金额" });
        await exportService.ExportAsync(output, "payments.xlsx", rows, definition);
    }
}
```

### 5. 回滚（消费方钩子，R3）

```csharp
var record = await taskService.GetRecordByBatchNoAsync(batchNo);   // 回滚入口
// 消费方按 BatchNo 删自己数据表数据 → 更新/删除批次记录
```

### 6. 配置选项

通过 `appsettings.json` 配置（`TKWF:DataPort` 节）：

```json
{
  "TKWF": {
    "DataPort": {
      "BatchSize": 500,
      "StopOnBatchFailure": false,
      "Provider": "miniexcel"
    }
  }
}
```

| 属性 | 默认 | 说明 |
|------|------|------|
| **`DefaultBatchSize`** | 500 | 默认批次大小（每批处理行数，≥1） |
| **`StopOnBatchFailure`** | false | 批次回调异常时是否终止（false = 继续下一批） |
| **`DefaultProvider`** | "miniexcel" | 默认 Provider 注册名（IImportProvider/IExportProvider） |

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IImportService`** | 导入门面（adapter 模板方法 + definition 回调双入口，批次切分 + 错误收集） | `ImportService`（核心运行库） |
| **`IExportService`** | 导出门面（列表 → 流） | `ExportService`（核心运行库） |
| **`IImportProvider`** | 读取引擎抽象（流式行，非泛型；只读场景可仅注册） | `MiniExcelImportProvider`（MiniExcel Provider） |
| **`IExportProvider`** | 写入引擎抽象（IEnumerable 懒求值） | `MiniExcelExportProvider`（MiniExcel Provider） |
| **`IDataImportTaskService`** | 批次任务：导入执行 + 批次记录落库 + FileHash 幂等 + 状态跟踪 | `DataImportTaskService`（本扩展） |
| **`DataImportRecordEntity`** | 导入批次记录实体（批次号/FileHash 唯一/状态/统计/错误摘要/CreatedBy） | SG1 声明式实体 → `DataImportRecord` 表 |
| **`DataPortOptions`** | 配置（`TKWF:DataPort` 节） | 内置 |
| **`DataPortExtensionInitializer<TUserInfo>`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子 DI 接线） | 本扩展 |

## 五、实体表结构 (Entity Schema)

`DataImportRecordEntity` 映射到 `DataImportRecord` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| BatchNo | NVARCHAR(64) | 批次号（GUID，唯一，回滚入口） |
| FileHash | NVARCHAR(128) | 文件哈希（SHA256，唯一索引——幂等检查） |
| FileName | NVARCHAR(512) | 原始文件名 |
| ProviderName | NVARCHAR(128) | Provider 名称（如 "miniexcel"） |
| Status | NVARCHAR(64) | 导入状态（Processing/Succeeded/Failed/PartiallySucceeded） |
| SuccessCount | INT | 验证通过数（独立于批次持久化） |
| FailedCount | INT | 行级失败数 |
| BatchFailureCount | INT | 批次级持久化失败数 |
| StartTime | DATETIMEOFFSET | 开始时间（UTC） |
| EndTime | DATETIMEOFFSET? | 结束时间（UTC） |
| ErrorSummary | NVARCHAR(2048)? | 错误摘要（前 5 个批次失败消息） |
| CreatedBy | NVARCHAR(128)? | 创建人（审计字段，v0.1.0 预留） |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

**索引**：`IX_dir_filehash`（FileHash 唯一）+ `IX_dir_batchno`（BatchNo 唯一）。

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 三层架构落地：核心运行库（批次切分算法在核心）+ MiniExcel Provider + 扩展模块（SG1 实体 + 批次任务 + Initializer）
- `IDataImportTaskService`：FileHash 幂等检查 + 批次记录落库 + Processing→Succeeded/Failed/PartiallySucceeded 状态跟踪
- 消费方集成模式（R2/R2'）：adapter 模板方法 + 消费方自管业务持久化 + BatchNo 回滚钩子
- slnx/CPM 接线 + 全量回归

### V0.2.0（候选，能力完善）
- 断点续传/失败重试调度（可接 BackgroundJobs）
- 导出模板/多 sheet 导出/`IAsyncEnumerable<T>` 异步流式导出
- CsvHelper Provider（复杂 CSV 场景按需评估）
- `CreatedBy` 链路写入（接消费方当前用户上下文）

### 远期 / 评估
- `.xls`/`.xlsb` 旧格式读取（体现 ExcelTools 桥接评估）
- Excel 模板渲染/样式/公式（与 `TKWF.Ext.Reporting` 边界对齐）

**文档信息**: V0.1.0 | 2026-09-06 | 关联：ADR-DataPort-读写引擎选型与分层架构.md（主框架私有）、v0.1.0-DataPort-数据导入导出-开发方案.md（主框架私有）