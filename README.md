# TKWF.Extensions

> TKWF 扩展模块仓库——权限、导航、身份、审计、设置、邮件、存储、账户、数据字典等。
>
> 扩展模块与主框架 **[TKW.Framework](https://github.com/LoongBa/TKW.Framework)** 解耦：扩展代码、测试、使用指南独立演进，不进入主框架 `TKW.Framework.slnx`。

---

## 扩展模块一览

> 各扩展模块使用说明

| 扩展                         | 版本            | 说明                                                                     | Tag                     | README                                   | 指南                                     |
| ---------------------------- | --------------- | ------------------------------------------------------------------------ | ----------------------- | ---------------------------------------- | ---------------------------------------- |
| **Permissions**              | V0.9.1 | 细粒度权限定义 / fail-closed 检查 / 编译期权限名校验（PERM001）/ **多用户批量权限检查（V0.9.0：`IPermissionBatchChecker`——单权限×多用户，按 ProviderKey 分组归因）** | $(System.Collections.Specialized.OrderedDictionary[Permissions]) | [README](./_Framework/Permissions/README.md) | [指南](./docs/Permissions/权限扩展-使用指南.md) |
| **Permissions.Abstractions** | V0.1.0 | 权限契约抽象（`IPermissionChecker`/`RequirePermission`/`IRoleProvider`） | —                       | —                                        | —（并入 Permissions）                    |
| **Permissions.Validation**   | V0.8.0 | 扩展侧 PERM001 DiagnosticAnalyzer（从内核移除耦合）                      | —                       | —                                        | —（并入 Permissions）                    |
| **Identity**                 | V0.3.3 | 用户 / 角色 / 用户角色分配 + PasswordHasher 凭据验证；`GetRolesAsync` VEntity 跨表 JOIN（V0.3.0）| $(System.Collections.Specialized.OrderedDictionary[Identity]) | [README](./_Framework/Identity/README.md)      | [指南](./docs/Identity/身份管理扩展-使用指南.md) |
| **Account**                  | V0.3.1 | 账户锁定 + 密码重置流程（主框架 V4.9.45 缺口补齐）+ **登录历史与异常检测（V0.3.0，消费 SecurityLog 查询）** | $(System.Collections.Specialized.OrderedDictionary[Account]) | [README](./_Framework/Account/README.md)       | [指南](./docs/Account/账户管理扩展-使用指南.md) |
| **Navigation**               | V0.1.2 | 菜单数据模型 / 贡献机制 / 权限过滤（从主框架迁出）                       | $(System.Collections.Specialized.OrderedDictionary[Navigation]) | [README](./_Framework/Navigation/README.md) | [指南](./docs/Navigation/导航扩展-使用指南.md)  |
| **AuditLogging**             | V0.4.1 | 审计日志 FreeSql 存储 + SG1 实体 + 查询 API + 统计聚合与清理（V0.3.0）+ **管理 API（V0.4.0：`[GenerateController]` + ExcludeMethods 排除含 ArgumentsJson 标准 CRUD + 5 REST 端点）** | $(System.Collections.Specialized.OrderedDictionary[AuditLogging]) | [README](./_Framework/AuditLogging/README.md)  | [指南](./docs/AuditLogging/审计日志扩展-使用指南.md) |
| **Settings**                 | V0.1.2 | 全局/用户级配置持久化 + 分层读取                                         | $(System.Collections.Specialized.OrderedDictionary[Settings]) | [README](./_Framework/Settings/README.md)      | [指南](./docs/Settings/设置管理扩展-使用指南.md) |
| **BlobStoring**              | V0.2.1 | 二进制大对象本地文件系统存储 + FreeSql 记录 + **FileStream 流式下载（V0.2.0）** | $(System.Collections.Specialized.OrderedDictionary[BlobStoring]) | [README](./_Framework/BlobStoring/README.md)   | [指南](./docs/BlobStoring/二进制存储扩展-使用指南.md) |
| **Emailing**                 | V0.2.1 | SMTP/MailKit 邮件发送 + FreeSql 发送记录 + **指数退避重试（V0.2.0）**；契约抽取至 `Emailing.Abstractions`（ADR48 D7）| $(System.Collections.Specialized.OrderedDictionary[Emailing]) | [README](./_Framework/Emailing/README.md)      | [指南](./docs/Emailing/邮件发送扩展-使用指南.md) |
| **Emailing.Abstractions**    | V0.1.0 | 邮件发送契约抽取（`IEmailSender`/`EmailMessage`——ADR48 D7 依赖倒置，Notifications 等消费方复用）| — | — | —（并入 Emailing）|
| **DataDictionary**           | V0.1.2 | 数据字典集中管理（定义 + 项 + 按编码查询）                               | $(System.Collections.Specialized.OrderedDictionary[DataDictionary]) | [README](./_Framework/DataDictionary/README.md) | [指南](./docs/DataDictionary/数据字典扩展-使用指南.md) |
| **Tagging**                  | V0.4.2 | 标签存储扩展（标签算法已回归 `TKW.Framework.Utility.Tags`，ADR52 瘦身；V0.4.0 AC 自动机 `DictMatch` 批量匹配 + Options 配置接入）| $(System.Collections.Specialized.OrderedDictionary[Tagging]) | [README](./_Framework/Tagging/README.md)       | [指南](./docs/Tagging/标签服务扩展-使用指南.md)  |
| **PrintTemplates**           | V0.1.2 | 打印模板引擎与版本化（Scriban 沙箱渲染 + Draft/Active/Archived 生命周期）| $(System.Collections.Specialized.OrderedDictionary[PrintTemplates]) | [README](./_Framework/PrintTemplates/README.md) | [指南](./docs/PrintTemplates/打印模板扩展-使用指南.md) |
| **Metrics**                  | V0.2.1 | 业务指标计算引擎（规格文档驱动复合指标计算——复购率/留存/同期群/漏斗/时段桶/比率；核心计算在 `TKW.Framework.Utility.Metrics`）+ **指标结果持久化（V0.2.0：`IMetricResultStore` 契约 + `MetricResultMapper` 标准化行映射——实体归消费方 SG1 接线）**| $(System.Collections.Specialized.OrderedDictionary[Metrics]) | [README](./_Framework/Metrics/README.md) | [指南](./docs/Metrics/指标扩展-使用指南.md) |
| **Dashboard**                | V0.1.2 | 仪表盘数据服务（Metrics 展示层——JSON 描述符 + Widget 数据查询；不引入图表库）| $(System.Collections.Specialized.OrderedDictionary[Dashboard]) | [README](./_Framework/Dashboard/README.md) | [指南](./docs/Dashboard/仪表盘扩展-使用指南.md) |
| **DataPort**                 | V0.1.2 | 数据导入导出（三层架构——核心运行库+MiniExcel Provider+SG1 持久化；FileHash 幂等）| $(System.Collections.Specialized.OrderedDictionary[DataPort]) | [README](./_Framework/DataPort/README.md) | [指南](./docs/DataPort/数据导入导出扩展-使用指南.md) |
| **Notifications**            | V0.4.1 | 通知中心（站内通知收件箱+订阅+事件驱动通知+多通道抽象；第一个事件总线消费者）；`GetListAsync(name)` VEntity 跨表 JOIN（V0.1.0）；+ 多通道路由 UseChannels/Email（V0.2.0）；+ 用户偏好路由 + 逐用户权限门控（V0.3.0，委托 Permissions v0.9.0 `IPermissionBatchChecker`）；+ **SignalR 实时推送通道（V0.4.0，独立包 `TKWF.Ext.Notifications.SignalR`，服务端非 UI）**| $(System.Collections.Specialized.OrderedDictionary[Notifications]) | [README](./_Framework/Notifications/README.md) | [指南](./docs/Notifications/通知中心扩展-使用指南.md) |
| **Notifications.SignalR**    | V0.1.0 | 通知中心 SignalR 实时推送通道（独立包——Hub 类型锚 + SignalRNotifier best-effort 推送 + 端点映射；`FrameworkReference` 共享框架零 NuGet；服务端非 UI，前端归消费方）| $(System.Collections.Specialized.OrderedDictionary[Notifications.SignalR]) | —（并入 Notifications） | —（并入 Notifications 指南） |
| **BackgroundJobs**          | V0.2.1 | 后台任务持久化增强（执行历史 `JobExecution` + 业务结果 `JobResult` 追踪 + **历史清理任务 V0.2.0（RetentionDays 启用）**）| $(System.Collections.Specialized.OrderedDictionary[BackgroundJobs]) | [README](./_Framework/BackgroundJobs/README.md) | [指南](./docs/BackgroundJobs/后台任务持久化扩展-使用指南.md) |
| **BackgroundJobs.Quartz**   | V0.1.0 | Quartz AdoJobStore 一键封装（`UseTkfwAdoJobStore` 12 表自动建表/集群配置）| $(System.Collections.Specialized.OrderedDictionary[BackgroundJobs.Quartz]) | [README](./_Framework/BackgroundJobs.Quartz/README.md) | —（并入 BackgroundJobs） |
| **HealthCheck**             | V0.2.1 | 系统健康探测（net10 内置 HealthChecks + `/health` 端点 + **内置 DB 探针** `AddDatabaseHealthCheck<T>`（V0.2.0，`IEntityReadOnlyDAC` 红线合规路径））| $(System.Collections.Specialized.OrderedDictionary[HealthCheck]) | [README](./_Framework/HealthCheck/README.md) | [指南](./docs/HealthCheck/健康检查扩展-使用指南.md) |
| **RateLimiting**            | V0.1.2 | Web 层限流接线（ASP.NET Core AddRateLimiter + IP/用户分区 + 429/Retry-After；与 Domain `[RateLimit]` 双层防护）| $(System.Collections.Specialized.OrderedDictionary[RateLimiting]) | [README](./_Framework/RateLimiting/README.md) | [指南](./docs/RateLimiting/限流扩展-使用指南.md) |
| **SecurityLog**             | V0.2.1 | 安全日志（登录/登出/改密/重置/锁定/注册/挑战事件 + IP/UA/结果 + **异常检测聚合 + 保留天数清理（V0.2.0）**）| $(System.Collections.Specialized.OrderedDictionary[SecurityLog]) | [README](./_Framework/SecurityLog/README.md) | [指南](./docs/SecurityLog/安全日志扩展-使用指南.md) |
| **Approval**                | V0.2.2 | 轻量审批引擎（流程定义/审批实例/审批任务三实体 + 状态机 + 或签/会签 + 委派/加签/抄送/超时自动处理（v0.2.0）+ 完成事件回调；不依赖外部工作流引擎）| $(System.Collections.Specialized.OrderedDictionary[Approval]) | [README](./_Framework/Approval/README.md) | [指南](./docs/Approval/审批流扩展-使用指南.md) |
| **OrganizationUnit**        | V0.1.2 | 组织单元（树形部门/团队/分组 + 物化路径 Level/Path + 循环防护/删除保护 + 用户关联 + 事务包裹）| $(System.Collections.Specialized.OrderedDictionary[OrganizationUnit]) | [README](./_Framework/OrganizationUnit/README.md) | [指南](./docs/OrganizationUnit/组织单元扩展-使用指南.md) |
| **Calendar**                | V0.1.2 | 日历/排程（日历+事件 CRUD + 重复规则子集（Utility 收纳：DAILY/WEEKLY/MONTHLY/YEARLY + 月末钳制 + 绝对索引）+ occurrence 查询/合并 + UTC 契约）| $(System.Collections.Specialized.OrderedDictionary[Calendar]) | [README](./_Framework/Calendar/README.md) | [指南](./docs/Calendar/日历排程扩展-使用指南.md) |
| **FileManagement**          | V0.2.2 | 文件管理（目录树 + 文件元数据 SHA256/去重 + **文件版本化 + 配额（v0.2.0）** + 上传 10 步安全链（防穿越/白名单/大小/ContentType 服务端推导）+ 依赖倒置 BlobStoring.Abstractions）| $(System.Collections.Specialized.OrderedDictionary[FileManagement]) | [README](./_Framework/FileManagement/README.md) | [指南](./docs/FileManagement/文件管理扩展-使用指南.md) |
| **BlobStoring.Abstractions**| V0.1.1 | Blob 存储契约抽取（`IBlobStorageService`/`BlobInfo`/`BlobStoringOptions`——ADR50 L2 依赖倒置，FileManagement 消费）| — | — | —（并入 BlobStoring）|
| **FeatureManagement**      | V0.3.2 | 功能管理/特性开关（[FeatureContributor] 编译期定义 + Provider 链分层值（v0.2.0 扩展点）+ IFeatureChecker 实现（接入 [RequireFeature]）+ 复杂 ValueType 类型化读写（v0.3.0）+ 变更事件分布式广播（v0.3.0）+ 管理 API）| $(System.Collections.Specialized.OrderedDictionary[FeatureManagement]) | [README](./_Framework/FeatureManagement/README.md) | [指南](./docs/FeatureManagement/功能管理扩展-使用指南.md) |

> 列说明：**README** = 扩展技术规范（随 NuGet 发布，位于 `_Framework/{扩展名}/`）；**指南** = 使用指南（公开文档，位于 `docs/{扩展名}/`）。Permissions.Abstractions/Validation 无独立文档，详见 Permissions 的 README 与指南。

> 全量 **1352 测试全绿**（28 测试项目）——`dotnet test` 零失败。（Approval v0.2.0 + FeatureManagement v0.3.0 + FileManagement v0.2.0 + Calendar + OrganizationUnit + 三件套基础设施 + BlobStoring 安全修复 + Tagging v0.4.0 + HealthCheck v0.2.0 + Notifications v0.2.0 多通道路由 + AuditLogging v0.3.0/v0.4.0 统计聚合与管理 API + Permissions v0.9.0 多用户批量权限检查 + Notifications v0.3.0 偏好路由与权限门控 + Notifications v0.4.0 SignalR 通道（12 用例，独立包） + **Metrics v0.2.0 指标结果持久化（15 用例：Mapper 6 + Store 9）** 后）

---

## 仓库定位

| 项       | 说明                                                                                                               |
| -------- | ------------------------------------------------------------------------------------------------------------------ |
| 主框架   | [`_TKWF/`](https://github.com/LoongBa/TKW.Framework)（TKW.Framework 领域框架）                                     |
| 本仓库   | TKWF 业务扩展包（`TKWF.Ext.*`）——独立版本，与主框架版本无关                                                        |
| 引用模式 | 扩展经 **PackageReference** 引用主框架**发布的 NuGet 包**（`TKWF.Domain` 等，CPM 集中 `Directory.Packages.props`；2026-09-15 迁移——独立构建，消费方视角与 NuGet 模式一致） |
| 版本管理 | MinVer 自动管理（git tag 即版本）；各扩展独立版本（各打各的 tag，命名空间前缀如 `Identity/v0.1.0`）                |

---

## 目录结构

```
_TKWF.Extensions/
├── _Framework/                     # 扩展源码（每个扩展一个项目）
│   ├── Permissions/                 # 权限扩展（V0.7.0：定义/检查/存储/管理 API/Admin.All）
│   ├── Permissions.Abstractions/    # 权限契约抽象（ADR48 D7 依赖倒置）
│   ├── Permissions.Validation/       # PERM001 编译期校验 Analyzer（V0.8.0，扩展侧）
│   ├── Identity/                     # 用户 + 角色 + 用户角色分配 + 凭据验证
│   ├── Account/                      # 账户锁定 + 密码重置流程（主框架缺口补齐）
│   ├── Navigation/                  # 菜单数据模型 + 贡献机制 + 权限过滤
│   ├── AuditLogging/                # 审计日志 FreeSql 存储
│   ├── Settings/                    # 设置管理 FreeSql 存储 + 分层读取
│   ├── BlobStoring/                 # 二进制存储（本地文件系统 + FreeSql 记录）
│   ├── Emailing/                    # SMTP/MailKit 邮件发送（V0.2.0：指数退避重试）
│   ├── Emailing.Abstractions/       # 邮件发送契约（IEmailSender——ADR48 D7 依赖倒置）
│   ├── DataDictionary/              # 数据字典集中管理
│   ├── Tagging/                     # 标签存储扩展（算法已回归 TKW.Framework.Utility.Tags）
│   ├── Notifications/              # 通知中心（发布/收件箱/订阅 + 多通道路由 + 偏好路由/权限门控）
│   ├── Notifications.SignalR/      # 通知中心 SignalR 实时推送通道（v0.4.0 独立包，服务端非 UI）
│   ├── BackgroundJobs/              # 后台任务持久化增强（执行历史 + 业务结果追踪）
│   ├── BackgroundJobs.Quartz/       # Quartz AdoJobStore 一键封装
│   ├── HealthCheck/                 # 系统健康探测（/health 端点接线 + 内置 DB 探针 v0.2.0）
│   ├── RateLimiting/                # Web 层限流接线（IP/用户分区 + 429）
│   ├── SecurityLog/                 # 安全日志（登录/改密/锁定等安全事件）
│   ├── Approval/                    # 轻量审批引擎（流程定义/实例/任务 + 状态机）
│   ├── OrganizationUnit/            # 组织单元（树形部门/团队/分组 + 用户归属）
│   ├── Calendar/                    # 日历/排程（日历+事件 + 重复规则子集）
│   ├── BlobStoring.Abstractions/    # Blob 存储契约（IBlobStorageService——ADR50 L2 依赖倒置）
│   ├── FileManagement/              # 文件管理（目录树 + 文件元数据 + 版本化/配额 + 上传安全链）
│   └── FeatureManagement/           # 功能管理（特性开关——定义收集 + 分层值 + IFeatureChecker）
├── _Tests/                          # 测试（一组扩展一个测试项目）
│   ├── Extension.Permissions.Tests/
│   ├── Extension.Permissions.Consumer/    # 消费方集成验证
│   ├── Extension.Permissions.Validation.Tests/  # Analyzer 单测
│   ├── Extension.Identity.Tests/
│   ├── Extension.Account.Tests/
│   ├── Extension.Navigation.Tests/
│   ├── Extension.AuditLogging.Tests/
│   ├── Extension.Settings.Tests/
│   ├── Extension.BlobStoring.Tests/
│   ├── Extension.Emailing.Tests/
│   ├── Extension.DataDictionary.Tests/
│   ├── Extension.Tagging.Tests/
│   ├── Extension.OrganizationUnit.Tests/
│   ├── Extension.Calendar.Tests/
│   ├── Extension.FileManagement.Tests/
│   └── Extension.FeatureManagement.Tests/
├── docs/                           # 公开使用指南（每个扩展一份）
│   ├── Permissions/权限扩展-使用指南.md
│   ├── Identity/身份管理扩展-使用指南.md
│   ├── Account/账户管理扩展-使用指南.md
│   ├── Navigation/导航扩展-使用指南.md
│   ├── AuditLogging/审计日志扩展-使用指南.md
│   ├── Settings/设置管理扩展-使用指南.md
│   ├── BlobStoring/二进制存储扩展-使用指南.md
│   ├── Emailing/邮件发送扩展-使用指南.md
│   ├── DataDictionary/数据字典扩展-使用指南.md
│   ├── Tagging/标签服务扩展-使用指南.md
│   ├── OrganizationUnit/组织单元扩展-使用指南.md
│   ├── Calendar/日历排程扩展-使用指南.md
│   ├── FileManagement/文件管理扩展-使用指南.md
│   └── FeatureManagement/功能管理扩展-使用指南.md
├── Directory.Build.props           # TKWFSourceRoot + MinVer + 打包属性
├── Directory.Packages.props         # CPM 集中包版本
├── AGENTS.md                        # 扩展仓库开发规则（AI Agent 与人工开发者必读）
└── TKWF.Extensions.slnx            # 扩展解决方案
```

> 扩展**设计文档**（开发方案/ADR/设计思路）+ 使用指南存放于本公开仓库 `docs/`（2026-09-15 裁定后新方案落公开仓库；历史方案/审核报告/总览跟踪在**主框架私有仓库** `_TKWF/docs/03_扩展模块/`，渐进迁移）。

---

## 架构模式

所有扩展遵循统一架构模式（异常静默 + TryAddScoped + SG1 声明式实体）：

```
扩展项目（net10.0）
├── Entity（partial class + [Table] + [DomainGenerateCode] + FreeSql [Column]）
├── Store 抽象 + FreeSql 实现（internal sealed + 异常静默）
├── Manager 门面（internal sealed + 聚合查询）
├── ExtensionInitializer（[TKWFExtension] + TryAddScoped 三钩子）
└── README.md（技术规范）

测试项目（xunit.v3 + FreeSql SQLite 内存）
├── ConsumerHostInitializer（[TKWFEnabledExtension] 白名单样板）
└── 测试类（Store CRUD + Manager 聚合 + Initializer DI + 异常静默）
```

### 扩展启用（v4.9.85+ 必需）

扩展不再"发现即启用"——消费方须在领域初始化器上显式声明白名单：

```csharp
using TKWF.Ext.Identity;

[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

声明后扩展三钩子（`ConfigureServices`/`ConfigureFilters`/`InitializeAsync`）自动接线。

### 依赖倒置（ADR48 D7）

扩展间依赖走 `.Abstractions`（接口/契约），不引用实现项目——L2 门控硬约束（`TKWF0022` Error）。

```
Navigation → Permissions.Abstractions（✅ 合法）
Navigation → Permissions（❌ TKWF0022 Error）
```

---

## 快速开始

### 消费方引用扩展

**NuGet 包模式**（消费方推荐——扩展已发布 `TKWF.Ext.*` NuGet 包）：

```xml
<!-- 消费方 .csproj -->
<PackageReference Include="TKWF.Ext.Identity" Version="0.3.3" />
<!-- 扩展经 PackageReference 传递引用主框架包（TKWF.Domain 4.10.24 等）；
     依赖扩展（如 Identity → Permissions.Abstractions）自动解析 -->
```

**源码模式**（本仓库开发/联调主框架新 API 时）：

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Identity\TKWF.Ext.Identity.csproj" />
```

> 双模式并存：扩展自身构建用 PackageReference（主框架包）；需调试主框架源码新 API 时可临时切源码引用——消费方视角与 NuGet 模式一致。

### 启用 + 使用

```csharp
// 1. 白名单声明（v4.9.85+）
[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 2. 注入 + 使用
public class AuthService(IUserManager userManager)
{
    public async Task<UserEntity?> LoginAsync(string name, string password)
        => await userManager.VerifyCredentialsAsync(name, password);
}
```

---

## 版本管理

```
MinVer：各扩展包独立版本（git tag 命名空间前缀 + csproj MinVerTagPrefix 对齐，如 Identity/v0.3.2 → 0.3.2）
公共内容（构建基建/无扩展归属）：无前缀 v tag（如 v0.8.2）

机制：
- 每个扩展 csproj 设 <MinVerTagPrefix>{扩展名}/v</MinVerTagPrefix>——MinVer 只匹配该扩展自己的前缀 tag，
  各扩展版本完全独立（Permissions 0.9.x 与 Identity 0.3.x 互不影响）
- 扩展 tag 命名：{扩展名}/v{major}.{minor}.{patch}（如 Permissions/v0.9.0、Notifications/v0.3.0）
- 公共内容（Directory.Build.props/构建脚本等无扩展归属的变更）：打无前缀 v tag（既有 v0.1.0~v0.8.2）
- 契约包（*.Abstractions）独立 tag：Account.Abstractions/v0.1.0 等，版本与主扩展独立演进
- 与主框架 _TKWF 版本完全独立
Tag 纪律：必须有开发方案 + 审核报告，且征得用户同意
```

---

## 扩展规划

- **P0（必须）**：**11/11 全部完成** ✅——Identity / Account / Navigation / AuditLogging / Settings / BlobStoring / Emailing / DataDictionary / Tagging / PrintTemplates + Permissions（V0.9.0）。注：Tagging 标签算法已按 ADR52 回归主框架 `TKW.Framework.Utility.Tags`，扩展保留存储层。
- **P1（推荐）**：**13 扩展已实施**（后台任务/功能管理/通知+SignalR/限流/健康检查/安全日志/组织单元/文件管理/审批/导入导出/日历/仪表盘/打印模板）；剩余 15 项按需推进（OpenIddict/MFA/SSO/LDAP/后台服务/安全防护/媒体库/搜索/动态表单/动态字段/工作流/文本模板/报表/文档管理/ApiDocs）。
- **P2（待定）**：20 项全部按需启用（CMS/支付/订阅/聊天/GraphQL/可观测性/数据分析BI 等）。

> 设计思路与 ABP 兼容策略（最优设计为默认、兼容 ABP 为特殊需求、碰巧兼容只记录）+ 各扩展设计分类见 [`docs/扩展模块设计思路与ABP兼容策略.md`](./docs/扩展模块设计思路与ABP兼容策略.md)；状态跟踪见主框架私有 [`_TKWF/docs/03_扩展模块/总览和跟踪.md`](https://github.com/LoongBa/TKW.Framework/blob/master/docs/03_扩展模块/总览和跟踪.md)。

---

## 许可证

Copyright © 2026 LoongBa · [Apache-2.0](./LICENSE)

> 开源、允许商用与闭源衍生，但必须保留版权与归属声明（Attribution）。

## 相关仓库

- [TKW.Framework（主框架）](https://github.com/LoongBa/TKW.Framework) — 领域框架 + 扩展机制（`TKWFExtensionAttribute` + `ExtensionInitializer` 三钩子 + SG1 发现 + `[TKWFEnabledExtension]` 白名单启用 + 三层门控 ADR50）
- [LoongBa-Scaffold](https://github.com/LoongBa/LoongBa-Scaffold) — 文档体系脚手架来源
