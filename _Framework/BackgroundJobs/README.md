# TKWF.Ext.BackgroundJobs 后台任务持久化增强技术规范

**状态**: 核心基础设施 (Core Infrastructure) | **版本**: V0.1.0（持久化增强——执行历史审计 + 业务结果追踪） | **框架**: .NET 10

**定位**（ADR-BackgroundJobs-持久化增强与执行追踪架构）：补齐主框架三实现（内置 `TKWF.BackgroundJobs` / `TKWF.BackgroundJobs.Hangfire` / `TKWF.BackgroundJobs.Quartz`）的持久化缺口：
- **执行历史审计**：`JobExecution` 实体——每次执行一行（耗时/重试/异常归档），经统一 `IBackgroundJobExecutionListener` 同步回调自动落库
- **业务结果追踪**：`JobResult` 实体 + `IJobResultRecorder`（作业内显式记录业务产出）+ 查询 API（按 JobId 回查）

**核心约束**: 数据访问全部走 SG1 DataService（禁裸 ORM / 禁直接 IEntityDAC）；实体 `[DomainGenerateCode]` 不指定 UserType（ADR42 D4）；审计字段 DateTime（UTC）

---

## 一、模块结构

```
_Framework/BackgroundJobs/
├── TKWF.Ext.BackgroundJobs.csproj        # 引 TKWF.BackgroundJobs + FreeSql + SG1 接线 ①-⑤
├── JobExecutionEntity.cs                  # SG1 实体：执行历史（每次执行一行）
├── JobResultEntity.cs                     # SG1 实体：业务结果
├── JobExecutionRecorder.cs               # IBackgroundJobExecutionListener 实现 → DataService 落库
├── IJobResultRecorder.cs                 # 作业内显式记录业务产出接口
├── JobResultRecorder.cs                  # IJobResultRecorder 实现
├── IJobExecutionQueryService.cs          # 执行历史查询接口 + DTO/Input 定义
├── JobExecutionQueryService.cs           # 查询服务实现
├── IJobResultQueryService.cs             # 业务结果查询接口 + DTO/Input 定义
├── JobResultQueryService.cs              # 结果查询服务实现
├── BackgroundJobsPersistenceOptions.cs   # TKWF:BackgroundJobs 配置（RetentionDays 预留）
├── BackgroundJobsExtensionInitializer.cs # [TKWFExtension] + TryAddScoped 三钩子
├── DataServices/
│   ├── JobExecutionEntityDataService.cs  # partial 业务方法（分页过滤 + SQL 级聚合）
│   └── JobResultEntityDataService.cs     # partial 业务方法（分页 + 按 JobId 查最新）
└── README.md
```

## 二、核心组件

| 组件 | 职责 | 默认实现 |
|------|------|---------|
| `IBackgroundJobExecutionListener` | 执行完成回调（主框架契约） | `JobExecutionRecorder`（internal sealed） |
| `IJobResultRecorder` | 作业内记录业务产出 | `JobResultRecorder`（internal sealed） |
| `IJobExecutionQueryService` | 执行历史分页/统计 | `JobExecutionQueryService`（internal sealed） |
| `IJobResultQueryService` | 业务结果查询 | `JobResultQueryService`（internal sealed） |
| `BackgroundJobsPersistenceOptions` | 配置（RetentionDays 预留） | 默认 180 天 |

## 三、使用说明

### 1. 启用扩展

```csharp
[TKWFEnabledExtension(typeof(BackgroundJobsExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

### 2. 作业内记录业务产出

```csharp
public class MyJob : IBackgroundJob
{
    private readonly IJobResultRecorder _recorder;

    public async Task ExecuteAsync(IDictionary<string, string> args, CancellationToken ct)
    {
        // ... 业务逻辑 ...

        // 记录业务产出（JobId 从 BackgroundJobContext.Current 自动读取）
        await _recorder.RecordAsync("success", JsonSerializer.Serialize(result), "处理完成", ct);
    }
}
```

### 3. 查询执行历史

```csharp
public class ExecutionHistoryService(IJobExecutionQueryService queryService)
{
    public async Task<JobExecutionStats> GetDailyStatsAsync()
        => await queryService.GetStatsAsync(TimeSpan.FromHours(24));

    public async Task<JobExecutionPagedResult> SearchAsync(string? provider, bool? isSuccess)
        => await queryService.GetListAsync(new JobExecutionQueryInput(
            Provider: provider, IsSuccess: isSuccess, Take: 50));
}
```

## 四、数据模型

### JobExecution（执行历史）

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | long | 主键自增 |
| JobId | string(64) | 调度器 JobId（弱关联透传） |
| JobType | string(1024) | 作业类型（AQN） |
| Provider | string(16) | builtin/hangfire/quartz |
| IsSuccess | bool | 是否成功 |
| IsCancelled | bool | 是否取消 |
| RetryAttempt | int | 第几次尝试 |
| DurationMs | long | 耗时毫秒 |
| StartedAtUtc | DateTime | 开始时间 UTC |
| CompletedAtUtc | DateTime | 完成时间 UTC |
| ErrorText | string? | 异常归档（nvarchar(max)） |
| TenantId | long? | 租户 Id |
| CreateTime | DateTime | 创建时间 UTC |

索引：IX_JobExecution_JobId / IX_JobExecution_Provider_Success / IX_JobExecution_StartedAt

### JobResult（业务结果）

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | long | 主键自增 |
| JobId | string(64) | 调度器 JobId |
| ResultType | string(32) | 结果类型（默认 "success"） |
| ResultJson | string? | 业务产出 JSON（nvarchar(max)） |
| Summary | string(512)? | 摘要 |
| CreateTime | DateTime | 创建时间 UTC |

索引：IX_JobResult_JobId

## 五、架构决策

- **列表 DTO 不含 ErrorText**（安全决策，对齐 AuditLogging 先例）——详情按 Id 取全量
- **统计 SQL 级聚合**（Oracle C1）——`Dac.CountAsync`（SQL COUNT(*) 分区计数）+ `FreeSqlQueryableExtensions.AvgAsync/MaxAsync`（SQL AVG/MAX 下推，ADR15 聚合 API 分层：IQueryable 桥接不支持 GroupBy 翻译，走 FreeSql ISelect 原生聚合）——**禁 Dac.ToListAsync + 内存 GroupBy**
- **TryAddEnumerable 注册监听器**（Oracle C3）——多监听器可叠加，无注册时零开销
- **v0.1.0 无 ExecutionId**（Oracle C4）——JobExecution 后置写入，执行中不可得（YAGNI）
- **异常静默**——监听器/记录器异常不阻断作业执行，ILogger.Warning 记录

## 六、AI Agent 协作契约

1. **红线**：全部数据访问走 SG1 DataService（禁裸 ORM / 禁直接 IEntityDAC）
2. **实体范式**：partial + BCL `[Table]` + FreeSql 全限定 `[Column]` + `[DomainGenerateCode]` 不指定 UserType
3. **聚合范式**：SQL 级聚合（Dac.CountAsync + FreeSqlQueryableExtensions.Avg/Max——禁 Dac.ToListAsync + 内存 GroupBy，ADR15）
4. **异常静默**：监听器/记录器 try/catch + ILogger.Warning

### 文档信息

- 归档日期: 2026-09-09
- 维护团队: TKW Framework Team
