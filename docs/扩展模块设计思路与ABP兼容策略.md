# 扩展模块设计思路与 ABP 兼容策略

> TKWF 扩展模块的**设计思路与 ABP 兼容策略**公开文档。
>
> 定位：本文档为**设计文档**（公开）——说明 TKWF 扩展模块的设计立场、与 ABP 的关系、各模块的设计思路分类。
> 内部状态跟踪（版本演进、Oracle 评审、测试数、全量回归）与开发计划见**主框架私有** `_TKWF/docs/03_扩展模块/总览和跟踪.md`。
>
> 最后更新：2026-09-15

---

## 一、设计原则（用户裁定 2026-09-15）

### 1.1 以最优设计为目的（默认）

每个扩展模块**以最优设计为唯一目的**——借鉴 ABP 等优秀框架的功能定位与思想，但实现上**全面发挥 TKWF 框架优势**（SG 编译期生成、DataService 数据访问红线、DomainUser 中心、ITransactionManager 原子提交、VEntity 跨表查询、编译期门控 ADR50 三层）。**不因"对齐 ABP"而牺牲框架优势。**

### 1.2 兼容 ABP 是特殊需求（非默认）

**兼容 ABP 数据结构不是扩展的默认目标**，仅针对特殊需求：

- **客户明确提出**（如存量 ABP 系统迁移切换）→ 由 TKWF 按需评估/实施数据层适配；
- **指导客户自行实现**→ 提供兼容指引文档，客户在消费方侧自建适配（TKWF 扩展保持最优设计不动摇）。

### 1.3 碰巧兼容只需记录

若某扩展在最优设计下**自然碰巧兼容** ABP（如接口同名、数据模型对标、表结构近似），**只需在文档中记录兼容点**，供迁移用户参考——**不刻意追求、不主动扩展兼容面**。

### 1.4 边界声明

TKWF 扩展实体体系（SG1 + 自建 UserType + FreeSql [Table] + DataService 红线）与 ABP（Guid 主键 + EF Core + Application Service 分层）**根本不同构**——强行对齐表结构会丢弃框架优势（编译期生成/委托解析/UoW 事务），与 1.1 原则冲突。因此**默认不做表结构级 ABP 兼容**。

---

## 二、已实现扩展设计思路分类

> 分类标准：
> - **自然兼容（记录）**——碰巧对齐 ABP 接口/数据模型，仅记录，便于迁移用户参考；
> - **借鉴但更优**——借 ABP 功能定位/概念命名，实现走 TKWF 原生路径（编译期/DataService 红线/ITransactionManager 等）；
> - **全新设计**——TKWF 独有或差异化（ABP 无对应或 ABP 需购买商业模块），且体现 TKWF 优势；
> - **接线型**——直接接线主框架/net10 内置能力（Hangfire/Quartz/HealthChecks/AddRateLimiter），零第三方依赖，不重建 Domain 层。

### 2.1 自然兼容 ABP（记录）

| 扩展 | 版本 | 兼容点 | 说明 |
|------|:---:|------|------|
| **Emailing** | V0.2.0 | `IEmailSender`/`EmailMessage` 与 ABP **同名同语义** | 契约层对齐 ABP——消费方代码可平滑迁移；`Emailing.Abstractions` 拆包（ADR48 D7 依赖倒置） |
| **Notifications** | V0.4.0 + `.SignalR` V0.1.0 | **三层数据模型对标 ABP**（`Notification` 发布态 → `UserNotification` 收件箱 → `NotificationSubscription` 订阅） | 概念级可迁移（对标 ABP Notifications）；通道抽象 `INotificationNotifier` 对齐 ABP Notifier |

> 这两个扩展的兼容是**自然碰巧产生**的（Emailing 契约简洁自然、Notifications 三层模型本就是最优解）——按 1.3 原则记录，非刻意追求。

### 2.2 借鉴 ABP 但更优（主流）

| 扩展 | 版本 | 借鉴点 | TKWF 更优处（发挥框架优势） |
|------|:---:|------|------|
| **Permissions** | V0.9.0 | `IPermissionChecker`/`PermissionDefinition`（对齐 ABP Authorization） | **编译期收集权限定义**（SG 接口判定 `IPermissionDefinitionContributor`，v4.10.31 A+ 阶段 3 起否决 ABP 运行时扫描）；`IPermissionBatchChecker` 批量检查（按 ProviderKey 分组归因）；N+1 优化（常数 2 次查询）；PERM001 编译期权限名校验 |
| **Navigation** | V0.1.0 | `IMenuContributor`/`MenuItemDefinition`（对齐 ABP UI.Navigation） | ConfigureMenu **同步化**（Oracle 裁定，否决 ABP 运行时）；权限过滤经 `IPermissionChecker`；循环检测；依赖 `Permissions.Abstractions`（ADR48 D7） |
| **Identity** | V0.3.0 | 用户/角色/用户角色（对齐 ASP.NET Core Identity + ABP 定位） | 补齐框架持久化空白；`IdentityPasswordManager` 开箱密码适配器；`IdentityAuthService` 注册/登录 API（`[GenerateController]`）；样板 171→8 行；VEntity 跨表 JOIN；`IdentityRoleProvider` Scoped 缓存 |
| **Account** | V0.3.0 | 账户锁定/密码重置（ABP Account 定位） | 主框架 V4.9.45 缺口补齐（默认实现）；防用户枚举；`IAccountPasswordManager` 适配器解耦；登录历史/异常检测消费 SecurityLog |
| **AuditLogging** | V0.4.0 | 审计日志模块定位（ABP AuditLogging） | SG1 声明式实体；**管理 API**（ExcludeMethods 排除含 ArgumentsJson CRUD 防 D5 泄露）；统计聚合 + 保留天数清理；QueryService 单一真相源 |
| **Settings** | V0.2.0 | 设置管理定位（ABP SettingManagement） | 完整分层读写（User→Tenant→Global）+ IMemoryCache 缓存 + 匿名短路 |
| **BlobStoring** | V0.2.0 | 对象存储抽象定位（ABP BlobStoring） | `BlobStoring.Abstractions` 依赖倒置（ADR50 L2）；四入口防穿越 fail-closed；FileStream 流式下载；OCE 取消穿透 |
| **FeatureManagement** | V0.3.0 | `IFeatureValueProvider` Provider 链（对齐 ABP）+ 分层值 | **编译期定义收集**（SG1 接口判定 `IFeatureDefinitionContributor`，v4.10.31 A+ 阶段 3）；版本号缓存失效（写后全层即时）；`FeatureValueChangedEvent` **分布式广播**（跨实例即时失效）；复杂 ValueType 类型化读写 |
| **SecurityLog** | V0.2.0 | ABP Pro SecurityLog 定位 | 独立安全事件实体（安全语义区别于 AuditLog）；Domain AOP 过滤器采集（opt-in 不改主框架）；异常检测聚合 + 保留天数清理 |
| **OrganizationUnit** | V0.1.0 | ABP Pro OrganizationUnit 定位 | 物化路径（Level/Path BFS 维护）+ 循环防护 + 删除保护 + 物理删除语义 + junction 唯一约束 + ITransactionManager |
| **FileManagement** | V0.2.0 | ABP Pro File Management 定位（对标核验） | **超越 ABP Pro**——DB 版本表 + SHA256 完整性（回滚指针复用）+ 硬配额（SQL SUM 下推 + 并发"先到先得"）；上传 10 步安全链 |
| **PrintTemplates** | V0.1.0 | 模板引擎选型对标 ABP TextTemplating（Scriban 为 ABP 默认先例） | Scriban 沙箱渲染（MemberFilter 白名单）+ `{Key}@{Version}` 版本化（审计固定渲染）+ Draft/Active/Archived 生命周期 |
| **BackgroundJobs** | V0.2.0 + `.Quartz` V0.1.0 | ABP BackgroundJobs 定位 | 主框架内置调度层 + Hangfire/Quartz Provider；扩展补齐持久化缺口（执行历史统一监听器 + 业务结果 + AdoJobStore 一键封装 + 历史清理） |
| **Tagging** | V0.4.0 | ABP 无独立标签模块（概念借鉴通用标签） | 标签算法**回归 Utility.Tags**（ADR52 收纳）；`ITagBatchMatcher` AC 自动机（O(N+Z) 与规则数解耦）+ Pipeline 分组缓存 + Options 接入 |

### 2.3 全新设计（TKWF 独有/差异化）

| 扩展 | 版本 | 设计定位 | 关键设计 |
|------|:---:|------|------|
| **DataDictionary** | V0.2.0 | **TKWF 独有**（ABP 无独立模块） | 定义+项双实体 + 按编码聚合查询 + 缓存 + 树形分组 |
| **Metrics** | V0.2.0 | **TKWF 独有**（规格文档驱动的复合业务指标计算） | 计算内核 `Utility.Metrics`（零依赖）+ 静态注册表零反射；`IMetricResultStore` 持久化契约（实体归消费方 SG1） |
| **Dashboard** | V0.1.0 | **TKWF 独有**（Metrics 展示层数据服务） | 服务端数据服务（不引入图表库）；JSON 描述符 + `IDashboardDataProvider` + metricRef 消费接线；与 Reporting/Analytics 边界明确 |
| **DataPort** | V0.1.0 | **TKWF 独有**（ABP 无导入导出模块） | 三层架构（核心运行库 Utility.DataPort + MiniExcel Provider + SG1 持久化）；FileHash 幂等；模板方法 `ImportAdapterBase<T>` |
| **Approval** | V0.2.0 | **TKWF 差异化**（ABP 需购买 Elsa） | 三实体 + 显式状态机 + 审批人身份硬校验 + 委派/加签/抄送/超时自动处理 + 审批人抽象；**不依赖外部工作流引擎** |
| **Calendar** | V0.1.0 | **TKWF 独有**（RRULE 子集自研入 Utility 零依赖） | 重复规则子集（月末钳制 + COUNT/UNTIL 互斥 + 绝对索引）+ C1 分路谓词 + RecurrenceEndUtc 同源推导 + UTC 契约 |
| **HealthCheck** | V0.2.0 | 接线型（net10 内置 HealthChecks，零第三方） | `/health` 端点映射（复用 D04 认证豁免）+ 自定义 IHealthCheck + 内置 DB 探针（`IEntityReadOnlyDAC` 只读红线合规路径） |
| **RateLimiting** | V0.1.0 | 接线型（ASP.NET Core AddRateLimiter，不重建 Domain 层） | 全局/端点策略 + IP/用户分区 + 429/Retry-After；与 Domain `[RateLimit]` 双层防护互补 |

---

## 三、设计思路演进观察

1. **最优设计为默认已全面落地**——绝大多数扩展借 ABP 功能定位/概念命名（Provider/Store/Manager/Contributor），但实现走 TKWF 原生路径，ABP 仅作为需求梳理参照系。
2. **自然兼容仅 2 例（Emailing 契约 / Notifications 数据模型）**——按裁定"碰巧兼容只记录"，不刻意扩展兼容面。
3. **接线型是 TKWF 特有第三形态**——底层基础设施（BackgroundJobs 调度 / HealthCheck / RateLimiting）直接接线主框架或 net10 内置能力，省力且无兼容包袱。
4. **兼容 ABP 的落地路径**（当客户提出迁移需求时）：
   - 首选：**指导客户自行实现**——提供兼容指引（接口签名/数据模型对照），客户在消费方侧适配；
   - 次选：TKWF 评估数据层适配（候选：Identity 用户表 / Settings / Notifications 收件箱），需 ADR 记录决策；
   - 否决：**默认不做表结构级对齐**（与 1.1 最优设计原则冲突）。

---

## 四、相关文档

| 文档 | 位置 | 说明 |
|------|------|------|
| 总览和跟踪 | 主框架私有 `_TKWF/docs/03_扩展模块/总览和跟踪.md` | 状态跟踪唯一入口（版本/评审/测试/全量回归） |
| 扩展模块对标清单 | 主框架私有 `_TKWF/docs/03_扩展模块/扩展模块对标清单.md` | ABP/Orchard 逐模块对标 |
| 各扩展使用指南 | 本仓库公开 `docs/{扩展名}/` | 每个扩展一份使用指南 |
| 各扩展技术规范 | 本仓库公开 `_Framework/{扩展名}/README.md` | 随 NuGet 发布的技术规范 |
| 设计/开发方案 | 本仓库公开（逐步迁移）+ 主框架私有（历史） | 每个模块的设计文档 |

---

## 五、变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-09-15 | 初始建立：设计原则（用户裁定——最优设计为默认、兼容 ABP 为特殊需求、碰巧兼容只记录）+ 已实现 24 扩展分类（自然兼容 2 / 借鉴更优 14 / 全新 8）+ 演进观察 + 兼容落地路径 |
