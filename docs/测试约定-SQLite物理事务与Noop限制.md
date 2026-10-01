# 测试约定——SQLite 物理事务边界与 Noop 事务限制

> **落点**：扩展模块组公开仓（`_TKWF.Extensions`）测试约定文档。
> **来源**：`_TKWF/docs/03_扩展模块/SQLite物理事务与测试基建-转达登记与移交.md`（2026-10-02 登记移交，建议 1/3 落点本仓）。
> **实证来源**：EduPlatform V0.1.3 群组 CRUD 迭代（2026-10-02 实测）+ FreeSql 官方事务文档 + SQLite 单写者锁设计语义。
> **性质**：测试基建约定（非框架缺陷）——生产 PostgreSQL 不受影响；本约定防止后续开发者误以为扩展测试已覆盖物理事务语义。

---

## 一、现状：扩展测试宿主事务接线

扩展测试统一采用以下组合（Calendar/OrganizationUnit/Approval/FileManagement 等测试宿主共识模式）：

| 组件 | 接线 | 说明 |
|---|---|---|
| 数据库 | **SQLite `:memory:`**（文件库仅特殊场景） | `UseAutoSyncStructure(true)` 自动建表；每用例独立实例隔离 |
| 数据访问 | **真实 `FreeSqlEntityDAC<T>`**（经 `UnitOfWorkManager` 逐操作持久化） | 红线合规路径 DataService → IEntityDAC 的真实实现 |
| 事务管理器 | **`NoopTransactionManager`**（默认）| `BeginAsync`/`CommitAsync` 空操作——写路径**不经** UoW 物理事务 |
| 事务结构测试 | **`RecordingTransactionManager`**（专项覆盖）| 记录型 Fake——仅统计 Begin/Commit/Rollback 调用次数，可注入提交失败 |

**含义**：扩展测试的写路径 = DAC 池连接直写（每操作独立事务），**从未**经过真实 `FreeSqlTransactionManager` 的物理事务闭环（Commit/Rollback 原子性）。**全量测试全绿 ≠ 物理事务语义已覆盖**。

---

## 二、SQLite 物理事务边界（建议 1 接线前提）

### 2.1 根因（官方文档 + 实证双重确认）

FreeSql 官方事务文档**明示**：

> "提示：uow 范围内，尽量别使用 `fsql` 对象，以免不处在一个事务。"

- `UnitOfWork` 是对 `DbTransaction` 的封装，uow 内操作走**独立的事务连接**；`fsql` 对象（含经池解析的 DAC）走**连接池连接**。
- SQLite 采用**单写者模型**——同一时刻仅允许一个写事务，整个数据库文件在写事务期间被锁定（官方设计行为，"SQLite does not support concurrent write transactions, and never has"）。
- 二者叠加：uow 开启事务（BEGIN 写锁）后，池连接再写 → **写锁互斥 → `database is locked`**（`:memory:` 单连接崩溃 / 文件库 SQLiteException）。连接串参数（`Pooling=False` / `Default Timeout` / `AutoSync=false` / 连接清理 / `Journal Mode=Off` / Monitor 串行化）均**不能**根治——锁在 DB 层而非代码层。
- 先例：ABP（扩展机制对标对象）官方对 SQLite 的建议同样是 `UnitOfWorkTransactionBehavior.Disabled`（禁用 DB 事务）。

### 2.2 接线结论

| 场景 | 正确姿势 | 说明 |
|---|---|---|
| 普通 CRUD / 结构测试 | **SQLite `:memory:` + Noop TM**（现状） | 已验证 DataService/Store 业务逻辑、异常静默、事件语义 |
| 事务**包裹结构**验证 | **Recording TM**（现状） | 验证写路径确实进入 Begin→Commit / 失败 Rollback（结构钉子） |
| 事务**物理原子性**验证 | **PostgreSQL（测试容器/生产同语义）** | SQLite 单写者锁 + uow/池连接不一致 → 物理事务测试在 SQLite 上**不可靠**，不投入 |

**强制约定**：
1. **新增事务类测试默认走 Recording 模式**（不要试图在 SQLite 上搭真实 `FreeSqlTransactionManager`）。
2. **物理 Commit/Rollback 闭环测试只允许在 PostgreSQL 上做**（见 §四 建议 2 评估）；如确需 SQLite 冒烟，必须全链路同一连接（uow 内 `uow.Orm`/`uow.GetRepository`，禁止混用 `fsql`），且仅限单写、短事务。
3. 事务内**禁止表结构迁移**（SQLite 锁冲突；`SyncStructure` 须在事务前完成）。

---

## 三、Noop 限制注记（建议 3，防误读）

> 后续开发者阅读测试代码时的必读注记。

- **Noop TM ≠ 无事务需求**：扩展写路径（Create/Update/Delete）仍声明事务包裹（DataService 内 `ITransactionManager.Begin`），只是测试宿主用 Noop 空操作替换——**生产环境仍走真实 `FreeSqlTransactionManager`**。
- **覆盖边界**：
  - ✅ 已验证：业务逻辑、异常静默、DataService 逐操作持久化、Recording 模式下的 Begin/Commit/Rollback 调用形态。
  - ❌ **未验证**：物理事务原子性（异常中断后多实体是否真回滚、提交失败是否真无部分成功）、UoW 连接一致性与并发事务隔离。
- **判定**：凡涉及「跨实体级联写入后异常传播 → 无部分成功」类断言，当前 SQLite + Noop 测试仅能证明「代码按事务形态调用」，**不能**证明数据库层原子性——此类语义须在 PostgreSQL 测试容器补验（§四）。

---

## 四、PostgreSQL 测试容器增强评估（建议 2）

### 4.1 方案评估

| 项 | 评估 |
|---|---|
| 依赖包 | `Testcontainers.PostgreSQL`（1.9.x 起 net8.0+ 兼容，net10.0 可用；FreeSql 生态无官方绑定，直连 `Npgsql` 连接串即可） |
| 运行时依赖 | **Docker**（本地开发机 + CI Linux runner 均有；Windows 无 Docker 的机器需条件跳过） |
| 与现有基建 | 测试宿主 `Create` 增加 PG 变体（容器启动 → 连接串 → `FreeSqlBuilder.UseConnectionString(DataType.PostgreSQL, ...)`）；表结构经 `AutoSyncStructure`（PG 无 SQLite 锁问题，事务前同步无碍） |
| 成本 | 每容器启动约 2-5s；CI 需 Docker service；本地无 Docker 时 `[Trait]`/条件化跳过 |
| 收益 | 补唯一缺口——`FreeSqlTransactionManager` 物理 Commit/Rollback 闭环自动化覆盖；生产语义对齐 |

### 4.2 覆盖建议（跨实体事务各 ≥1 例）

| 扩展 | 建议用例 | 验证点 |
|---|---|---|
| **OrganizationUnit** | OU + OUUser 用户归属跨表写入 → 提交后二者一致 | Commit 闭环 + 级联原子性 |
| **FeatureManagement** | 功能定义 + 项/Provider 链分层值批量写入 | Commit 闭环 |
| **Approval** | 审批流程定义 + 实例 + 任务三实体（或签/会签分支）→ 中途异常 → 无部分成功 | **回滚原子性**（核心） |

另建议：跨实体「异常传播 + 无部分成功」等价断言从 EduPlatform 案例反向落地为 PG 回归。

### 4.3 排期建议

- **性质**：增强级（非紧急）——独立排期，随扩展测试基建迭代立项。
- **流程**：按 AGENTS §3——编写开发方案（`docs/` 公开）→ Oracle 评审 → 实施（Testcontainers 引入 + 3 扩展各 ≥1 例）→ 全量回归。
- **前置**：CI workflow 确认 Docker service 可用；本地无 Docker 条件跳过策略先行验证。

---

## 五、变更记录

| 日期 | 变更 |
|------|------|
| 2026-10-02 | 初稿——转达登记（EduPlatform 实测 + FreeSql 官方文档核实）落地：建议 1/3 接线约定 + Noop 限制注记 + 建议 2 PG 容器评估（转达 §三 归属执行） |

<!-- EOF -->
