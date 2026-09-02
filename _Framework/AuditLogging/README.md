# TKWF.Ext.AuditLogging 审计日志扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (审计日志数据库存储与查询 API) | **框架**: .NET 10

**核心约束**: 方法级审计日志持久化、查询 API、异常静默处理、ORM 无关存储抽象、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对业务操作进行审计日志记录——超越调试日志（LoggingFilter），面向安全合规的持久化存储、脱敏、结构化记录。

- **调试日志不足**：LoggingFilter 输出到 ILogger（开发调试用），不持久化、不脱敏、不结构化。

- **安全合规需求**：生产环境需记录谁在什么时间调用了什么方法、参数（脱敏）、耗时、成功/异常。

- **持久化耦合**：审计日志存储直接依赖特定 ORM（FreeSql/EF Core），扩展无法做到 ORM 无关。

- **配置割裂**：审计配置（是否启用、是否记录匿名用户）散落在代码中，无法通过 `appsettings.json` 统一管理。

- **查询缺失**（V0.2.0 解决）：V0.1.0 仅提供写入（`IAuditLogStore.SaveAsync`），消费方无法按条件查询审计日志——安全审计、问题排查均需标准化查询能力。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"存储抽象 + 查询 API + ORM 无关持久化 + 异常静默"** 架构。

### 1. 结构分层

- **过滤器级 (`AuditLogFilterAttribute`)**：主框架已有，记录调用者/目标方法/参数（脱敏）/耗时/关联 ID 到 `IAuditLogStore`。消费方 opt-in 启用。

- **存储抽象 (`IAuditLogStore`)**：主框架已有接口，定义 `SaveAsync(AuditLogEntry)`。扩展提供 FreeSql 默认实现。

- **查询抽象 (`IAuditLogQueryService`)**：V0.2.0 新增，扩展侧自建接口（不修改主框架）。按条件分页查询审计日志，返回 DTO 列表。

- **持久化实现 (`FreeSqlAuditLogStore`)**：将 `AuditLogEntry` 映射为 `AuditLogEntity` 并持久化。异常静默处理（不阻塞业务）。

- **查询实现 (`AuditLogQueryService`)**：V0.2.0 新增，internal sealed，FreeSql 查询 + 异常静默（Warning 日志 + 返回空结果）。

- **声明式实体 (`AuditLogEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` + `[Index]` 特性。

### 2. 安全语义

- **异常静默**：写入/查询失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IAuditLogStore` / `IAuditLogQueryService` Scoped，自动参与当前请求上下文。

- **DTO 安全（D5）**：`AuditLogListItemDto` 不含 `ArgumentsJson`——防止脱敏前的参数结构泄露。

### 3. 与主框架的关系

- `IAuditLogStore` + `AuditLogEntry` + `AuditLogFilterAttribute` 由主框架定义（不动）。
- 本扩展提供 `FreeSqlAuditLogStore` 实现 + `IAuditLogQueryService` 查询服务 + `AuditLoggingExtensionInitializer` 注册。
- 消费方通过 `FilterBuilder.AddAuditLog()` 启用审计日志过滤器。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.AuditLogging` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(AuditLoggingExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`IAuditLogStore`（默认 `FreeSqlAuditLogStore`）+ `IAuditLogQueryService`（默认 `AuditLogQueryService`）。

### 2. 启用审计日志过滤器

在消费方 `ConfigureGlobalFilters` 中通过 `FilterBuilder.AddAuditLog()` 启用：

```csharp
protected override void ConfigureGlobalFilters(FilterBuilder<MyUserInfo> builder)
{
    builder.AddAuditLog(filter =>
    {
        // 可选：为匿名调用记录审计日志
        filter.IsEnabledForAnonymous = true;
        // 可选：序列化返回值到审计记录（慎用，防止大对象/敏感返回值落盘）
        filter.SaveReturnValues = false;
    });
}
```

### 3. 查询审计日志（V0.2.0）

```csharp
// 注入 IAuditLogQueryService
public class AuditQueryService(IAuditLogQueryService queryService)
{
    // 按时间范围 + 用户查询
    var result = await queryService.GetListAsync(new AuditLogQueryInput
    {
        StartTime = DateTime.Today.AddDays(-7),
        UserName = "alice",
        Success = false,
        Take = 20
    });

    // 统计总数
    var total = await queryService.CountAsync(new AuditLogQueryInput
    {
        ServiceName = "OrderService"
    });
}
```

### 4. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "AuditLogging": {
      "IsEnabled": true,
      "LogAnonymous": false,
      "SaveReturnValues": false,
      "AdditionalSensitiveFields": ["creditCard", "ssn"]
    }
  }
}
```

消费方如需从配置绑定 Options，在自身 `ConfigureServices` 中调用：
```csharp
services.Configure<AuditLoggingOptions>(configuration.GetSection("TKWF:AuditLogging"));
```

### 5. 自定义 IAuditLogStore

若需替换 `FreeSqlAuditLogStore`（如写文件、发送到 SIEM）：

```csharp
// 消费方 ConfigureServices 中
services.AddScoped<IAuditLogStore, SiemAuditLogStore>();
```

TryAdd 语义确保消费方实现优先。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IAuditLogStore`** | 审计日志存储抽象（主框架定义） | `FreeSqlAuditLogStore`（本扩展） |
| **`IAuditLogQueryService`** | 审计日志查询服务（V0.2.0，扩展侧自建） | `AuditLogQueryService`（本扩展） |
| **`AuditLogEntry`** | 审计日志数据模型（主框架定义） | record 类型 |
| **`AuditLogFilterAttribute`** | 方法级审计日志过滤器（主框架定义） | 消费方 opt-in 启用 |
| **`AuditLogEntity`** | 审计日志表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`AuditLogQueryInput`** | 查询输入参数（V0.2.0） | record 类型 |
| **`AuditLogListItemDto`** | 查询列表项 DTO（V0.2.0，不含 ArgumentsJson） | record 类型 |
| **`AuditLogPagedResult`** | 分页查询结果（V0.2.0） | record 类型 |
| **`AuditLoggingUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`AuditLoggingOptions`** | 配置选项（`TKWF:AuditLogging` 节） | 内置 |
| **`AuditLoggingExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现（能力清单）+ 消费方 `[TKWFEnabledExtension]` 白名单启用 |

---

## 五、实体表结构 (Entity Schema)

`AuditLogEntity` 映射到 `AuditLog` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserName | NVARCHAR(128) | 调用者用户名 |
| UserId | NVARCHAR(128) | 调用者用户 ID |
| ServiceName | NVARCHAR(200) | 目标服务名（类名） |
| MethodName | NVARCHAR(200) | 目标方法名 |
| ArgumentsJson | NVARCHAR(MAX) | 参数 JSON（已脱敏） |
| ExecutionTime | DATETIME | 执行时间 |
| DurationMs | INT | 执行耗时（毫秒） |
| Success | BIT | 是否执行成功 |
| Exception | NVARCHAR(MAX) | 异常信息（失败时记录） |
| CorrelationId | NVARCHAR(128) | 关联 ID（分布式链路追踪） |
| CreateTime | DATETIMEOFFSET | 记录创建时间 |

**索引**（V0.2.0 新增）：`IX_AuditLog_ExecutionTime` / `IX_AuditLog_UserName` / `IX_AuditLog_CorrelationId`（均为非唯一索引）。

---

## 六、与 EntityHistoryFilter 的区别 (Comparison)

| 维度 | AuditLogFilter | EntityHistoryFilter |
|------|---------------|-------------------|
| **跟踪对象** | 方法调用事件 | 实体字段变更 |
| **记录内容** | 调用者、方法、参数、耗时、异常 | 实体字段 OldValue/NewValue |
| **持久化** | 需 IAuditLogStore 实现 | 需 IEntityHistoryStore 实现 |
| **查询** | `IAuditLogQueryService`（V0.2.0） | 待实现 |
| **脱敏** | 内置（SensitiveFields） | 无 |
| **适用场景** | 安全合规审计 | 业务数据变更追踪 |

---

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0
- FreeSql 审计日志存储
- 基础查询（实体建表）
- 异常静默处理

### V0.2.0（当前）
- 查询 API（`IAuditLogQueryService`：按时间/用户/服务名/方法名等条件分页查询）
- 索引补建（ExecutionTime / UserName / CorrelationId）
- Options 绑定修复（`AddOptions<AuditLoggingOptions>` 注册）
- DTO 安全（不含 ArgumentsJson）

### V0.3.0（规划）
- 统计聚合（CountByServiceAsync / CountByUserAsync 等）
- 数据清理/归档策略
- 审计日志统计分析
