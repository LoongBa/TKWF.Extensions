# TKWF.Ext.BackgroundJobs.Quartz 扩展技术规范

**状态**: BackgroundJobs 扩展 Part C（Quartz AdoJobStore 一键封装） | **版本**: V0.1.0 | **框架**: .NET 10

---

## 一、定位

`TKWF.Ext.BackgroundJobs.Quartz` 是 **配置级封装包**——将 Quartz AdoJobStore 12 表持久化的常用参数（数据库方言、连接字符串、表前缀、自动建表、集群）封装为一个 `IQuartzBuilder.UseTkfwAdoJobStore` 调用，消费方无需了解 Quartz 4.0 内部 API。

**红线合规**：本扩展**只做 Quartz 配置展开**，绝不注入 `IFreeSql`/`IEntityDAC`、绝不执行 SQL、不触碰 TKWF 数据访问层。Quartz 自管其 12 张调度表（`SchemaProvisioning` 自动建表或 DBA 手动建表）。

## 二、安装

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\..\_Framework\BackgroundJobs.Quartz\TKWF.Ext.BackgroundJobs.Quartz.csproj" />
```

## 三、接线示例

```csharp
using TKWF.Ext.BackgroundJobs.Quartz;

services.AddQuartzBackgroundJobs<MyUserInfo>(
    o => o.PollingInterval = TimeSpan.FromSeconds(5),
    quartzConfig: q =>
    {
        q.UseTkfwAdoJobStore(o =>
        {
            o.DbProvider = "sqlserver";                          // sqlserver / sqlite / postgresql
            o.ConnectionString = "Server=localhost;Database=Quartz;Trusted_Connection=True;";
            o.TablePrefix = "QRTZ_";                            // 默认
            o.AutoCreateSchema = true;                           // 开发环境自动建表
            o.Clustering = true;                                 // 集群模式
            o.InstanceId = "AUTO";                               // 自动生成唯一 ID
            o.ClusterCheckinInterval = TimeSpan.FromSeconds(20); // 集群心跳间隔
        });
    });
```

### 三实现"二选一"

`AddQuartzBackgroundJobs` 与 `AddBackgroundJobs` / `AddHangfireBackgroundJobs` **只调一个**（M1 语义——`IBackgroundJobManager` 解析以最后 `AddSingleton` 注册为准）。

## 四、Options

`QuartzAdoJobStoreOptions` 绑定配置节 `TKWF:BackgroundJobs:Quartz:AdoJobStore`：

```json
{
  "TKWF": {
    "BackgroundJobs": {
      "Quartz": {
        "AdoJobStore": {
          "DbProvider": "sqlserver",
          "ConnectionString": "Server=...",
          "TablePrefix": "QRTZ_",
          "AutoCreateSchema": true,
          "Clustering": false,
          "InstanceId": "AUTO",
          "ClusterCheckinInterval": null
        }
      }
    }
  }
}
```

| 属性 | 类型 | 默认 | 说明 |
|------|------|------|------|
| **DbProvider** | `string` | `""` | 必填。`"sqlserver"` / `"sqlite"` / `"postgresql"`（大小写不敏感） |
| **ConnectionString** | `string` | `""` | 必填。数据库连接字符串 |
| **TablePrefix** | `string` | `"QRTZ_"` | Quartz 表前缀 |
| **AutoCreateSchema** | `bool` | `true` | `true` = 自动建表（`ProvisionSchema()`/`CreateIfMissing`，开发）；`false` = 不自动建表（Quartz 默认 `Validate`，生产 DBA 手动建表） |
| **Clustering** | `bool` | `false` | 启用集群模式（`UseClustering`） |
| **InstanceId** | `string` | `"AUTO"` | 集群实例 ID。`"AUTO"` 自动生成 `NODE_xxx` 唯一 ID |
| **ClusterCheckinInterval** | `TimeSpan?` | `null` | 集群心跳间隔。`null` = Quartz 默认 7500ms |

### 必填校验

- `ConnectionString` 为空 → `InvalidOperationException`
- `DbProvider` 不在支持列表 → `NotSupportedException`

## 五、生产部署

**⚠️ `AutoCreateSchema=true` 仅供开发环境**（自动建表方便调试）。

**生产建议**：
1. 设 `AutoCreateSchema = false`（不自动建表，Quartz 默认 `Validate` 校验——生产 DBA 手动执行官方建表脚本）
2. DBA 手动执行 Quartz 官方建表脚本（随 Quartz 包附带）：
   - SQL Server: `tables_sqlServer.sql`
   - SQLite: `tables_sqlite.sql`
   - PostgreSQL: `tables_postgres.sql`

此策略对齐 VEntity 生产 DBA 建视图先例——自动建表/建视图仅开发便利，生产由 DBA 掌控 schema。

## 六、边界

| 做了 | 没做 |
|------|------|
| Quartz AdoJobStore 配置封装 | ❌ 任何 SQL 执行 |
| 数据库方言自动选择 | ❌ TKWF 数据访问 |
| 12 表自动建表（SchemaProvisioning） | ❌ 实体/查询/执行追踪（Part B 职责） |
| 集群配置封装 | ❌ Quartz 调度管理 API |
| 必填参数校验 | ❌ 业务逻辑 |

## 七、依赖

| 依赖 | 版本 | 说明 |
|------|------|------|
| **Quartz** | 4.0.0 | 后台作业调度器（单一包，含 DI/Hosting） |
| **TKWF.BackgroundJobs.Quartz** | 主框架源码 | Quartz Provider 桥接 |
| **TKWF.Domain** | 主框架源码 | 领域抽象 |

---

**文档信息**: V0.1.0 | 2026-09-09 | 关联：v0.1.0-BackgroundJobs-持久化增强-开发方案.md（主框架私有）
