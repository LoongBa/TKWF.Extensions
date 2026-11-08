# TKWF.Ext.DataPort 数据导入导出扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.5（DP-04 幂等重设计 + DP-05 锁死治理） | **框架**: .NET 10

**核心约束**: 三层架构（核心运行库 + MiniExcel Provider + 扩展模块带持久化）、批次切分算法在核心、FileHash 幂等检查（R3）、消费方自管业务数据持久化（R2）、SG1 声明式实体、领域服务门面继承 DomainServiceBase（IDomainUser 永不注册 DI）

---

## 一、需求分析 (Demand Analysis)

数据导入导出（Excel/CSV 等）是企业应用高频横切能力——但长期散落在业务代码中（静态工具类 + 硬编码映射）。TKWF 以 **1 行配置 + 1 个适配器** 的消费方集成模式（对齐优化 ExcelTools `IImportDataAdapter`）提供统一导入导出，包发布到 `TKWF.Ext.*` 扩展仓库。

- **消费场景**：DMP-Lite 商家账单/对账文件导入（美团/支付宝等第三方 CSV 对账）、运营数据导出。
- **集成模式（R2/R2'）**：消费方派生 `ImportAdapterBase<T>` 实现钩子（ColumnMapping/OnValidate/OnBatchWrite），核心 `ImportService` 调度批次并调用钩子——消费方在 `OnBatchWrite` 内自管业务数据持久化（DataPort 不感知/不持久化消费方业务实体）。
- **批次记录（扩展模块）**：`DataImportRecordEntity`（SG1 `[DomainGenerateCode]`）记录每次导入的批次号/文件哈希/状态/统计/错误摘要——`FileHash` 唯一索引防重复导入（幂等），`BatchNo` 供消费方按批次回滚。

## 二、设计原理 (Design Principles)

本扩展采用 **"核心运行库（Utility.DataPort，零第三方依赖）+ MiniExcel Provider（可选包）+ 扩展模块带持久化（本包）"** 三层架构（ADR-DataPort 裁定）。

### 1. 分层结构

- **核心运行库** `TKWF.Utility.DataPort`（主框架 Utility 独立项目）——导入/导出流程算法：列映射 → 类型转换（委托缓存）→ 验证三态（Keep/Skip/Terminate）→ **批次切分**（每 BatchSize 行回调 onBatch）→ 错误收集（行级 `ImportFailure` + 批次级 `BatchFailure`）。<br>**⚠️ 独立包（非编入 TKWF.Utility.dll）**——扩展经 **PackageReference** 引用（CPM 集中版本，见 `Directory.Packages.props`）。
- **MiniExcel Provider** `TKWF.Utility.DataPort.Providers.MiniExcel`（Apache-2.0 读写引擎，可选引入）——流式读 xlsx/csv → `ImportRow`（非泛型，Provider 不感知 T）；列表 → xlsx/csv 写流。只读场景可不引用。
- **扩展模块**（本包）——`DataPortExtensionInitializer`（DI 接线）+ `DataPortOptions`（`TKWF:DataPort`）+ `DataImportRecordEntity`（SG1 实体）+ `IDataImportTaskService`（导入执行 + 批次记录落库 + FileHash 幂等 + 状态跟踪）。

### 2. 关键设计

- **批次切分语义（核心）**：Provider 流式返回行 → Service 累积每 `BatchSize` 行触发一次 `onBatch`（含 batchIndex + startRowIndex）；末批余数显式 flush（空文件 → 0 批 0 回调）；批次回调异常 → `BatchFailure` 收集 + `StopOnBatchFailure` 终止/继续；`ImportResult.SuccessCount` = 验证通过数（独立于批次持久化，Oracle C3）。
- **模板方法模式（R2'）**：`ImportAdapterBase<T>` 基类——不同导入各一派生类（如 `PaymentImportAdapter : ImportAdapterBase<PaymentLog>`），便于管理与维护；改善 ExcelTools 静态工具类 + 接口混职责问题。
- **幂等检查（R3）**：`FileHash`（SHA256）唯一索引——同文件二次导入 → Processing/Succeeded 返回已有批次（拒绝）；Failed 批次 → 重置为 Processing 允许重导。`BatchNo` 为消费方回滚钩子入口。
- **幂等语义（V0.1.5 DP-04 批次级重放）**：PartiallySucceeded 重导 = **仅重放"失败 ∪ 未尝试尾部"批次**（definition 模式 onBatch 过滤——跳过已成功批次回调，消费方零重复写、零数据丢失）；Failed 保持全量重导。重放判据 = `FailedBatchIndexes`（JSON 数组）+ `MaxAttemptedBatchIndex`（统一覆盖 continue/早停/Terminate：尾部纳入重放防丢失、前缀跳过零重复写）；`BatchSize` 首跑落库 + 重放一致性校验（不一致拒绝，防索引错位）；三列均 null 的旧记录按全量重导（升级零破坏）。框架 `BuildImportDefinition` 仍 internal → 本地反射提取接入 definition 模式（P0-1 方案 B 降级，脆弱性已披露，见代码注释；框架公开后移除反射改直调）。
- **状态跟踪**：Processing → Succeeded / Failed / PartiallySucceeded（`BatchFailures.Count > 0`），错误摘要取前 5 个批次失败消息拼接。
- **Processing 超时治理（V0.1.5 DP-05）**：进程崩溃 → 记录冻结 Processing → 同文件永远无法再导入（无解锁路径）。新增 `IStaleImportRecoveryService` 门面（CAS 重置超时 Processing → Failed + "stale recovery" 标记）+ **导入心跳**（onBatch 内周期条件 touch `UpdateTime`，仅 Processing 态）+ Initializer **启动扫描**兜底（进程崩溃 → 下次启动即治愈）+ 文档周期调度接线示例（对齐 BackgroundJobs `IJobHistoryCleanupService` 先例）；选项 `ProcessingTimeoutMinutes`（默认 60，0 禁用）/`HeartbeatIntervalMinutes`（默认 1）。
- **异常语义**：核心导入失败统一抛 **`DataImportException`**（核心运行库导出的导入异常类型——含批量失败明细）；消费方可捕获该异常并据 `BatchNo` 回滚（§三.5）。

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

白名单声明后自动注册（**V0.1.4 二态形态，V4.10.53 ADR90 领域自治根治**）：

| 接口 | 实现 | 注册形态 | 消费方式 |
|------|------|---------|---------|
| `IImportService` / `IExportService` | `ImportService` / `ExportService`（核心运行库框架类型） | **接线型 `TryAddScoped`**（消费方可自定义覆盖） | 普通 DI 注入/`GetRequiredService` |
| `IImportProvider` / `IExportProvider` | `MiniExcelImportProvider` / `MiniExcelExportProvider`（无状态） | **接线型 `TryAddSingleton`**（消费方可自定义覆盖） | 普通 DI 注入/`GetRequiredService` |
| `IDataImportTaskService` | `DataImportTaskService`（本扩展，internal sealed 继承 `DomainServiceBase`） | **`AddConstructibleService`**（接口可构造守卫工厂 + 实现类 throw-factory） | `User.Use<IDataImportTaskService>()` |

> **V0.1.4（V4.10.53 ADR90 领域自治根治）**：`IDataImportTaskService` 注册形态由 `TryAddScoped` 改为 **`AddConstructibleService`**——
> 门面继承 `DomainServiceBase`（经基类 `User` 取上下文——**IDomainUser 永不注册 DI**，D01）+
> `[DiContractIgnore]` 豁免 DI001（运行时手写注册）；消费方统一经 `User.Use<IDataImportTaskService>()` 解析
> （AOP 路径：设 CurrentAopUser → GetRequiredService → 守卫工厂）。旧形态构造注入 IDomainUser 生产解析必失败（v0.3.3 同根缺陷）。
> 核心 Import/Export + Provider 为框架类型（零用户上下文依赖）——**接线型保留**（普通 DI，不继承基类）。

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
// ⚠️ 禁构造注入 IDataImportTaskService——AddConstructibleService 守卫工厂，域作用域外解析必抛（DI004）
// 正确形态：DomainServiceBase 派生 + 内部经 User.Use<IDataImportTaskService>() 解析
public class PaymentImporter : DomainServiceBase
{
    public PaymentImporter(IDomainUser user) : base(user) { }

    public async Task<DataImportTaskResult> ImportAsync(string filePath)
    {
        // 批次记录落库 + FileHash 幂等（同文件二次导入 → 返回已有批次）+ 状态跟踪
        var result = await User.Use<IDataImportTaskService>()
            .ImportAsync(filePath, new PaymentImportAdapter(_db));
        // result.RecordId / result.BatchNo / result.ImportResult（SuccessCount + Failures + BatchFailures）
        return result;
    }
}
```

### 3. 简单回调入口（definition 模式）

`IImportService.ImportAsync(filePath, definition, onBatch)`——不需要派生适配器即可用；`onBatch` 为批次写库回调 (batchIndex, startRowIndex, batch, ct)。

> **V0.1.5（DP-04 重放过滤）**：`DataImportTaskService` 内部统一走 definition 模式 onBatch 回调（经反射提取消费方 adapter 的 `BuildImportDefinition`——含既有 SheetName/OnValidate/OnConvert，`OnBatchWrite` 为 public virtual 由 onBatch 闭包按重放判据调度）；重放时仅调用失败/未尝试批次的 `OnBatchWrite`（已成功批次跳过 = 消费方零重复写）；`OnValidate`/`OnConvert` 对所有行全量执行。

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

> ⚠️ **导出公式注入边界（安全）**：导出单元格以 `=` 开头的内容可能被 Excel 解释执行（CSV/公式注入）——**数据源自用户输入时注意消毒**（如首字符 `=`/`+`/`-`/`@` 前置 `'` 或空格）。

### 5. 回滚（消费方钩子，R3）

```csharp
var record = await taskService.GetRecordByBatchNoAsync(batchNo);   // 回滚入口
// 消费方按 BatchNo 删自己数据表数据 → 更新/删除批次记录
```

> ⚠️ **恢复后全量重导双写警示（V0.1.5 DP-05，P1-4）**：Processing 超时被恢复扫描标记为 Failed 的记录（`ErrorSummary` 含 **"stale recovery"** 标记 = 崩溃前有部分批次已写入）→ 消费方再导入同文件 = **全量重导**（Failed 语义）→ 会对崩溃前已写入的批次**重复写**——**非幂等 `OnBatchWrite` 消费方须先按 `BatchNo` 回滚（删除已落数据）再重导**；幂等消费方（OnBatchWrite 天然去重）可直接重导。`BatchNo` 回滚钩子是此场景的前置手段。

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
| **`ProcessingTimeoutMinutes`** | 60 | Processing 超时（分钟）——启动/周期扫描时，超过该时长仍为 Processing 的记录重置为 Failed（0 = 禁用恢复扫描） |
| **`HeartbeatIntervalMinutes`** | 1 | 导入心跳间隔（分钟）——导入执行中每批检查，距上次心跳达到该值即更新 UpdateTime（仅 Processing 态条件更新）；0 = 禁用心跳 |

> **`ImportBatchOptions`（核心运行库，导入时按次覆盖）**：构造参数 `(BatchSize, StopOnBatchFailure, SkipValidationErrors)`——第三个参数 `SkipValidationErrors`（bool，默认 `true`：Skip 行收集 failure 继续；`false` 时**首个 Skip 行即终止导入**）。**`DataPortOptions` 无此配置项**——消费方经 `ImportBatchOptions` 显式传（如 `new ImportBatchOptions(BatchSize: 500, StopOnBatchFailure: false, SkipValidationErrors: true)`）。

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IImportService`** | 导入门面（adapter 模板方法 + definition 回调双入口，批次切分 + 错误收集） | `ImportService`（核心运行库） |
| **`IExportService`** | 导出门面（列表 → 流） | `ExportService`（核心运行库） |
| **`IImportProvider`** | 读取引擎抽象（流式行，非泛型；只读场景可仅注册） | `MiniExcelImportProvider`（MiniExcel Provider） |
| **`IExportProvider`** | 写入引擎抽象（IEnumerable 懒求值） | `MiniExcelExportProvider`（MiniExcel Provider） |
| **`IDataImportTaskService`** | 批次任务：导入执行 + 批次记录落库 + FileHash 幂等 + 状态跟踪 + PartiallySucceeded 失败批次重放（V0.1.5） | `DataImportTaskService`（本扩展——V0.1.4 继承 `DomainServiceBase` + `[DiContractIgnore]`，`AddConstructibleService` 注册，消费方 `User.Use<IDataImportTaskService>()`） |
| **`IStaleImportRecoveryService`** | Processing 超时恢复门面（V0.1.5 DP-05）——CAS 重置超时 Processing → Failed + "stale recovery" 标记 | `StaleImportRecoveryService`（本扩展——`AddConstructibleService` 注册，消费方 `User.Use<IStaleImportRecoveryService>()`） |
| **`DataImportRecordEntity`** | 导入批次记录实体（批次号/FileHash 唯一/状态/统计/错误摘要/CreatedBy + V0.1.5 重放三新列） | SG1 声明式实体 → `TKWF_DataImportRecord` 表 |
| **`DataPortOptions`** | 配置（`TKWF:DataPort` 节） | 内置 |
| **`DataPortExtensionInitializer<TUserInfo>`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子 DI 接线） | 本扩展 |

## 五、实体表结构 (Entity Schema)

`DataImportRecordEntity` 映射到 `TKWF_DataImportRecord` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| BatchNo | NVARCHAR(64) | 批次号（GUID，唯一，回滚入口） |
| FileHash | NVARCHAR(128) | 文件哈希（SHA256，唯一索引——幂等检查） |
| FileName | NVARCHAR(512) | 原始文件名 |
| ProviderName | NVARCHAR(128) | Provider/适配器名称（实际存 adapter 类型名，如 `PaymentImportAdapter`——`adapter.GetType().Name`） |
| Status | NVARCHAR(64) | 导入状态（Processing/Succeeded/Failed/PartiallySucceeded） |
| SuccessCount | INT | 验证通过数（独立于批次持久化） |
| FailedCount | INT | 行级失败数 |
| BatchFailureCount | INT | 批次级持久化失败数 |
| StartTime | DATETIME | 开始时间（UTC） |
| EndTime | DATETIME? | 结束时间（UTC） |
| ErrorSummary | NVARCHAR(2048)? | 错误摘要（前 5 个批次失败消息） |
| CreatedBy | NVARCHAR(128)? | 创建人（审计字段，v0.1.0 预留） |
| CreateTime | DATETIME | 创建时间 |
| UpdateTime | DATETIME | 更新时间 |
| FailedBatchIndexes | NVARCHAR(512)? | **V0.1.5** 失败批次索引集合（JSON 数字数组字符串，如 `"[0,2]"`；PartiallySucceeded 时落库，重导判定重放集合；null = 旧记录按全量重导） |
| BatchSize | INT? | **V0.1.5** 首跑批次大小（重放时校验一致性——传不同 BatchSize 拒绝，防索引错位） |
| MaxAttemptedBatchIndex | INT? | **V0.1.5** 首跑已尝试的最大批次索引（重放判定"未尝试尾部"纳集——早停路径不丢数据） |

**索引**：`TKWFIX_Dir_Filehash`（FileHash 唯一）+ `TKWFIX_Dir_Batchno`（BatchNo 唯一）+ `TKWFIX_Dir_Status_Updatetime`（**V0.1.5** Status+UpdateTime，超时恢复扫描）。

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.5（DP-04 幂等重设计 + DP-05 超时治理，2026-11-09 迭代）
- **DP-04 批次级幂等**：PartiallySucceeded 重导语义改为**失败批次重放**——新增 `FailedBatchIndexes`/`BatchSize`/`MaxAttemptedBatchIndex` 三结构化列 + definition 模式 onBatch 重放过滤（`ReplayFilter` 统一判据：失败 ∪ 未尝试尾部纳集；continue 模式零重复写、早停模式零丢失）；Failed 全量重导不变、Succeeded/Processing 拒绝不变；重放 BatchSize 一致性校验（拒绝防索引错位）；FailedBatchIndexes 解析失败降级全量（LogWarning）；旧记录（三列 null）全量重导（升级零破坏）。框架 `BuildImportDefinition` internal → 本地反射提取接入（P0-1 方案 B 降级，脆弱性披露于代码注释；框架公开后移除反射）
- **DP-05 超时治理**：新门面 `IStaleImportRecoveryService` + `StaleImportRecoveryService`（CAS 恢复：条件更新 WHERE Status='Processing' AND UpdateTime<阈值，影响行数判竞态）+ 导入心跳（onBatch 内周期 `TouchProcessingAsync` 仅 Processing 刷新 UpdateTime）+ Initializer `InitializeAsync` 启动扫描（SystemUser，异常静默不阻断启动）+ 文档周期调度接线示例（对齐 `IJobHistoryCleanupService` 先例）；恢复为 Failed（保留元数据/BatchNo 回滚钩子）+ **恢复后全量重导双写警示**
- 测试 33 用例全绿（既有 6 适配三新列 + T1-T11：重放批次/零重复写/多失败/兼容边界/早停/BatchSize 拒绝/脏数据降级 + 超时恢复/未超时/启动扫描/恢复重导闭环/CAS/心跳）
- 文档同步：README + 使用指南（幂等/状态/超时治理小节 + 恢复与重导警示 P1-4）+ 变更记录

### V0.1.4（V4.10.53 领域自治根治，ADR90——正确路线）
- `IDataImportTaskService`（`: IDomainService`）门面化：实现继承 `DomainServiceBase`（经基类 `User` 取上下文——IDomainUser 永不注册 DI）+ `[DiContractIgnore]` 豁免 DI001；注册由 `TryAddScoped` 改 **`AddConstructibleService`**（接口可构造守卫工厂 + 实现类 throw-factory）；消费方经 `User.Use<IDataImportTaskService>()` 解析
- 核心运行库 `ImportService`/`ExportService` + MiniExcel Provider **接线型保留**（框架类型 ctor 零用户上下文——普通 TryAddScoped/TryAddSingleton 不变，不继承基类）
- 测试宿主重写走**生产路径**：真实 DI（Initializer ConfigureServices + FreeSql 基础设施 + AddLogging）+ `DomainUser<TestUserInfo>.BindScope` + `User.Use<IDataImportTaskService>()` AOP + Initializer 注册形态断言（守卫工厂/throw-factory/域外抛）；弃 StubDomainUser + 手写 DataService 直构
- slnx/CPM 接线 + 全量回归

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

**文档信息**: V0.1.0 | 2026-09-06 | 关联：ADR-DataPort-读写引擎选型与分层架构.md（主框架私有）、v0.1.0-DataPort-数据导入导出-开发方案.md（主框架私有）；**V0.1.4 整改注记**（2026-10-04）：领域自治根治（ADR90——正确路线）——门面 AddConstructibleService + 接线型保留框架服务，详见 §三 自动注册表与 §六 V0.1.4；**V0.1.5 迭代注记**（2026-11-09）：DP-04 幂等重设计（失败批次重放） + DP-05 超时治理（恢复门面 + 心跳 + 启动扫描），详见 §二.2/§三.5/§五/§六 V0.1.5 与 `docs/DataPort/幂等与锁死治理-开发方案.md`