# TKWF.Ext.HealthCheck 系统健康探测扩展技术规范

**状态**: 基础设施接线扩展 (Wiring Extension) | **版本**: V0.2.0 (系统健康探测 + 内置 DB 探针) | **框架**: .NET 10

**核心约束**: **接线型扩展**——net10 内置 HealthChecks（`Microsoft.Extensions.Diagnostics.HealthChecks`）聚合 + `/health` 端点映射、**零第三方依赖**、**V0.2.0 内置 DB 连通性探针**（经 `IEntityReadOnlyDAC<T>` 轻量表级探测——红线合规路径）、复用 D04 框架生命线 `/health` 认证豁免

---

## 一、需求分析 (Demand Analysis)

系统/服务健康状态探测是基础设施生命线的第一环（对标 OrchardCore.HealthCheck）：LB/编排/K8s 探活需要统一、免认证的 `/health` 端点，输出应用总体健康状态；生产诊断需要可开关的组件级细节输出。TKWF 主框架 D04 已豁免 `/health`/`/healthz` 认证（框架生命线自动追加），本扩展补齐 **HealthChecks 聚合 + 端点映射 + 配置** 接线，不重复造轮子。

- **消费场景**：DMP-Lite 等业务领域启用扩展后，`GET /health` 返回 `{"status":"Healthy"}`；消费方注册自定义组件探针（DB/Redis/存储连通性）后聚合为 OverallStatus。
- **差异化定位**：TKWF HealthCheck = **内置 HealthChecks 的 TKWF 接线层**（Options + 端点 + 白名单启用），探针全部归消费方——零内置假设，规避扩展内置 DB 探针的数据访问红线（见 §七边界）。

## 二、设计原理 (Design Principles)

### 1. 接线型三件套

- **`AddTkfwHealthChecks(IServiceCollection, Action<HealthCheckEndpointOptions>? = null)`**：展开 `services.AddHealthChecks()`（net10 内置）+ `AddOptions<HealthCheckEndpointOptions>().BindConfiguration("TKWF:HealthCheck")` + configure 委托应用（代码覆盖配置）。**V0.2.0 返回 `IHealthChecksBuilder`**（源兼容——v0.1.0 丢弃返回值零破坏；支持链式 `AddDatabaseHealthCheck<T>`）。
- **`AddDatabaseHealthCheck<TEntity>(builder, name, failureStatus?, timeout?)`**（V0.2.0）：内置 DB 连通性探针——经 `IEntityReadOnlyDAC<TEntity>` 表级探测（`SELECT COUNT(*)`），默认超时 5 秒防 hang；`TEntity` 为消费方任一 SG1 声明实体（编译期 `IDomainEntity` 约束门禁）。
- **`MapTkfwHealthChecks(IEndpointRouteBuilder)`**：按 Options 映射 `endpoints.MapHealthChecks(path, HealthCheckOptions)`——`Enabled=false` 不映射；`Detailed=true` 走自定义 ResponseWriter 输出组件级状态 JSON；`AllowAnonymous=true`（默认）端点追加 AllowAnonymous 元数据（与 D04 豁免一致）。
- **`HealthCheckExtensionInitializer<TUserInfo>`**（`[TKWFExtension("HealthCheck")]`）：ConfigureServices 仅注册 Options 绑定——探针由消费方经 `AddTkfwHealthChecks().AddDatabaseHealthCheck<T>()` / `AddCheck<T>` 注册。

### 2. 关键设计

- **命名避让（Oracle P1-1）**：扩展 Options 命名 `HealthCheckEndpointOptions`（非 HealthCheckOptions）——避开 ASP.NET Core 内置 `Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions` 类型名冲突；端点映射内部用内置 `HealthCheckOptions`（全限定）。
- **Detailed 响应控制**：`Detailed=false`（默认）自定义 ResponseWriter 输出仅 `{"status":"..."}`——不泄露组件细节（内置默认 writer 会输出 entries）；`Detailed=true` 输出组件级 JSON（名称/状态/耗时/异常）。
- **Options 可靠解析**：`MapTkfwHealthChecks` 优先 `IOptions<HealthCheckEndpointOptions>`（消费方经 AddTkfwHealthChecks / SG1 [Options] 绑定 / AddOptions 任一注册路径），未注册时兜底 `IConfiguration` 直读 `TKWF:HealthCheck` 节，再兜底默认值。
- **聚合语义**：消费方注册多个 `IHealthCheck` 后，扩展聚合全部检查项返回 OverallStatus（最差状态胜出：Unhealthy > Degraded > Healthy）；探针异常 → 该项 Unhealthy（不抛 500、不崩溃）；**多探针并行执行，各探针独立 scope 解析 DAC——并发安全**（P2-5）。
- **`AllowCachingResponses = false`**：健康探测结果实时，不参与 HTTP 缓存。
- **DB 探针红线合规（V0.2.0）**：探针经 `IEntityReadOnlyDAC<T>`（只读接口）表级探测——红线规则 2 仅禁 `IEntityDAC<T>` 读写接口，未禁只读接口；接线型扩展无持久化适用红线不适用边界。**边界声明**：此合规路径仅限基础设施连通性探测，不作为业务扩展数据访问的先例——业务扩展仍须走 SG1 DataService。

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认** |
|----------|---------|---------|
| **`HealthCheckEndpointOptions`** | 端点配置（`TKWF:HealthCheck` 节：Path/Enabled/Detailed/AllowAnonymous） | 本扩展 |
| **`HealthCheckServiceCollectionExtensions`** | `AddTkfwHealthChecks`——AddHealthChecks + Options 绑定 + configure（V0.2.0 返回 `IHealthChecksBuilder`） | 本扩展 |
| **`HealthCheckBuilderExtensions`** | `AddDatabaseHealthCheck<TEntity>`（V0.2.0）——内置 DB 连通性探针注册 | 本扩展 |
| **`DatabaseHealthCheck<TEntity>`** | DB 探针实现（V0.2.0，internal sealed）——`IEntityReadOnlyDAC<TEntity>` 表级探测 + 默认超时 | 本扩展 |
| **`HealthCheckEndpointExtensions`** | `MapTkfwHealthChecks`——端点映射 + Detailed/AllowAnonymous 控制 | 本扩展 |
| **`HealthCheckExtensionInitializer<TUserInfo>`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + Options 接线） | 本扩展 |
| **`IHealthCheck`**（net10 内置） | 组件探针契约——**消费方实现并注册**（扩展不内置） | 消费方 |

## 四、接线示例 (Usage)

### 1. 消费方 csproj

```xml
<ProjectReference Include="..\..\_Framework\HealthCheck\TKWF.Ext.HealthCheck.csproj" />
```

### 2. 白名单启用（v4.9.85+）

```csharp
[TKWFEnabledExtension(typeof(HealthCheckExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

### 3. Program.cs——注册 + 映射 + 探针

```csharp
// ① 注册（Options 默认：/health，Enabled=true，Detailed=false，AllowAnonymous=true）
//    V0.2.0：AddTkfwHealthChecks 返回 IHealthChecksBuilder——支持链式内置 DB 探针
services.AddTkfwHealthChecks(o => o.Detailed = false)
    .AddDatabaseHealthCheck<UserEntity>("db");   // ② V0.2.0 内置 DB 连通性探针（IEntityReadOnlyDAC 表级探测）

// ②b 或消费方注册自定义组件探针（ASP.NET Core 标准路径）
// services.AddHealthChecks()
//     .AddCheck("redis", () => redis.IsConnected() ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("redis down"));

// ③ 映射端点（D04 已豁免 /health 认证）
app.MapTkfwHealthChecks();   // GET /health → {"status":"Healthy"}
```

**内置 DB 探针**（V0.2.0，推荐——免手写探针 + 红线合规路径）：

```csharp
// TEntity = 消费方任一 SG1 声明实体（[DomainGenerateCode] 自动实现 IDomainEntity；编译期门禁）
services.AddTkfwHealthChecks()
    .AddDatabaseHealthCheck<OrderEntity>("db")                    // 默认超时 5s
    .AddDatabaseHealthCheck<ProductEntity>("db-products", timeout: TimeSpan.FromSeconds(10));

// 语义：SELECT COUNT(*) FROM {Entity 表}——连接串有效 + DB 可达 + 表存在 + 查询可执行
// 异常 → Unhealthy（503）；OCE 取消穿透（不误报）；多探针并行执行（独立 scope 并发安全）
```

> **自定义探针**仍可用（消费方实现 `IHealthCheck`）：`services.AddHealthChecks().AddCheck<MyDbHealthCheck>("db")`。

## 五、配置 (Configuration)

`HealthCheckEndpointOptions` 绑定 `TKWF:HealthCheck` 配置节（SG1 `[Options]` 自动绑定 + AddTkfwHealthChecks/Initializer 兜底）：

```json
{
  "TKWF": {
    "HealthCheck": {
      "Path": "/health",
      "Enabled": true,
      "Detailed": false,
      "AllowAnonymous": true
    }
  }
}
```

| 键 | 默认 | 说明 |
|----|------|------|
| **`Path`** | `/health` | 端点路径（D04 已豁免认证；改路径后若需匿名请保持 AllowAnonymous=true） |
| **`Enabled`** | `true` | `false` 时不映射端点（`GET /health` → 404） |
| **`Detailed`** | `false` | `true` 时输出组件级状态 JSON（生产建议关闭，避免泄露组件细节） |
| **`AllowAnonymous`** | `true` | 端点追加 AllowAnonymous 元数据（与 D04 /health 豁免一致）；`false` 时遵循应用认证管线 |

## 六、响应示例 (Responses)

> **状态码语义**：Healthy/Degraded → 200；Unhealthy → **503**（ASP.NET Core 内置 `HealthCheckOptions.ResultStatusCodes` 默认——探针语义，LB/编排依据状态码判定，无需自定义）。

**默认（Detailed=false）**——仅状态，不泄露组件细节：

```json
// GET /health
{ "status": "Healthy" }
```

**Detailed=true**——组件级状态：

```json
{
  "status": "Unhealthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "db":  { "status": "Healthy",   "description": null,  "duration": "00:00:00.0012345", "exception": null,  "data": null },
    "redis": { "status": "Unhealthy", "description": "redis down", "duration": "00:00:00.0012345", "exception": null, "data": null }
  }
}
```

## 七、边界与演进 (Boundaries & Roadmap)

### 边界（v0.2.0）

- **内置 DB 探针**（V0.2.0 已实施）：`AddDatabaseHealthCheck<TEntity>`——经 `IEntityReadOnlyDAC<T>` 轻量表级探测（红线合规路径：只读接口未禁 + 接线型扩展不适用边界；**边界声明：此合规路径仅限基础设施连通性探测，不作为业务扩展数据访问先例——业务扩展仍须走 DataService**）。
- **不内置 Redis/存储等第三方组件探针**——消费方经 `AddCheck<T>` 自注册（YAGNI）。
- 不引入第三方健康检查套件（`AspNetCore.HealthChecks.*`）——YAGNI，net10 内置足够。
- 无健康状态持久化 / 历史 / 告警。
- 不做端点认证豁免的实现（D04 已就绪，AllowAnonymous 仅追加元数据）。

### 后续候选

- 健康状态持久化 / 历史 / 告警（`IHealthCheckPublisher` 接出）
- 端点路由冲突处理（Options.Path 可配已内置）

---

**文档信息**: V0.2.0 | 2026-09-10 | 关联：`v0.2.0-HealthCheck-DB探针-开发方案.md`（主框架私有，Oracle 评审修订 P1-1/2/3 + P2-1~5）、D04 框架生命线
