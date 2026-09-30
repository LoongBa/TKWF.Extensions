# TKWF 框架实战教学 · 系列开篇：扩展模块最优解探索——Schema 级数据组合

> **定位**：TKWF 扩展模块设计**方法论总纲**——从 UserCenter 案例深挖（2026-09-30）提炼的创造性思维模式与最佳实践，用于**倒推优化全部扩展模块**，并作为框架实战教学系列文章的起点。
> **状态**：🟢 活跃（**持续演进**——随每个扩展优化迭代更新；发现机制缺口时登记 §九 框架演进候选，按需完善框架）
> **关联**：UserCenter 方案（`docs/UserCenter/v0.1.0-UserCenter-用户中心-开发方案.md`）；主框架 `D17`（扩展机制基座）/ `D17A`（跨模块数据关联）/ `D17B`（ABP 对比）；Oracle 探索裁决（2026-09-30，五路机制验证）
> **核心主张**：TKWF 扩展机制的完整潜力**尚未被现有扩展触及**——现有实现（VEntity 采用率仅 2/27、内存拼装为主流）多数停留在"传统 OO 思维 + TKWF 语法"层面。本系列的目标是**把数据装配、安全投影、契约装配从运行时前移到 Schema 声明与编译期**，这是 TKWF 哲学（编译期确定性 / 可靠可控 / 数据归属主 / 领域逻辑进扩展）的完整发挥。

---

## 一、系列定位与阅读地图

| 篇 | 主题 | 状态 |
|----|------|------|
| **本篇（开篇）** | 方法论总纲：七个根本差异 + Schema 级数据组合 + 9 维判据 + 倒推路线图 | ✅ 初始 |
| 第 2 篇（规划） | UserCenter 案例解剖：契约层 + 读模型层 + 装配层组合视图 | 随 v0.2.0 实施 |
| 第 3 篇（规划） | 倒推案例：OrganizationUnit / FileManagement 树形读模型化 | 随迭代 |
| 第 4 篇（规划） | 倒推案例：Approval / PrintTemplates 两步查询 VEntity 化 | 随迭代 |
| … | 每次扩展优化产出一篇案例 + 修订本篇路线图 | 持续 |

> 本系列是**实战教学**：每个案例 = 一个真实扩展的"次优现状 → 更优解 → 迁移成本 → 落地验证"完整循环。

---

## 二、核心认知：TKWF 与传统 OO 的七个根本差异

> 理解这七点，是进入 TKWF 设计思维的前提。传统思维"换个框架语法"做扩展，等于没发挥框架。

| # | 差异 | 传统 OO 设计 | TKWF 设计 | 机制支撑 |
|---|------|-------------|-----------|---------|
| 1 | **Schema 级 Open-Closed** | 扩展点 = 接口抽象 + 运行时多态 | 扩展点 = **表 + 视图声明**（零修改扩展实体） | VEntity（D17A 裁定 2） |
| 2 | **装配数据化** | 跨模块聚合 = .NET 内存 load + merge | 跨模块聚合 = **SQL JOIN 投影**（单查询） | VEntity ViewSql + SyncViewsAsync |
| 3 | **安全投影化** | 脱敏/裁剪 = 代码后处理（易漏路径） | 脱敏/裁剪 = **读模型定义**（所有路径自动覆盖） | ViewSql 表达式列（AS 别名校验） |
| 4 | **契约实现方归位** | 主项目实现接口（Provider 样板） | **数据属主扩展实现契约**（扩展间协作） | ADR48 D7 + 扩展 Initializer TryAdd |
| 5 | **读模型联邦** | 查询模型手写 + 映射 | 写模型单一、**读模型按需无限构建** | Entity / VEntity 类型级 CQRS（SG 生成） |
| 6 | **编译期三件套** | 发现/注册/校验 = 运行时（扫描/反射） | 发现/生成/注册/校验 = 编译期（门控 Error） | SG1 + ADR47/48/61 + 门控 |
| 7 | **生成物即文档** | 文档事后手写（易漂移） | **活态文档 = 编译副产物**（禁止读 .g.cs） | xCodeGen + [xCodeGen.Hash] |

**业界佐证**（D17B + 2026-09-30 调研）：可控性阵营（Granit/SimpleModule）正把发现从运行期迁移到编译期——TKWF 已在前沿；"发现≠启用"是横向共识（ABP 官方确认依赖树不可运行期改写）；扫描式实现（ABP 服务级反射）已积累 5+ 语义缺陷 issue。

---

## 三、Schema 级 Open-Closed（范式转移详解）

### 3.1 洞察来源（UserCenter 案例）

> "UserProfile 可变性在项目层。传统：定义抽象层接口由项目实现 + 运行时内存装载 UserProfile 再拼装项目定制部分。
> TKWF：扩展提供默认实现，甚至在项目层建 Entity/Table + 建 VEntity/View 跨表关联扩展模块的 UserProfile → 自动组装得到项目层 VEntity/Dto——**不需要在内存中装载拼装**。"

### 3.2 范式转移

| 维度 | 传统 OO（接口 + 多态） | TKWF（Schema 组合） |
|------|----------------------|--------------------|
| 扩展点载体 | 接口抽象 + 运行时多态分派 | 表 + 视图声明 |
| 装配位置 | .NET 内存（load + merge + 映射） | SQL 层（JOIN + 投影，单查询） |
| 校验时机 | 运行时（漏实现/漏拼装才暴露） | 编译期（ViewSqlColumnValidator 双向列校验） |
| 加属性 | 改实体类 / ABP 式扩展表 | 项目建表 + 视图投影（扩展实体零修改） |
| 加关系 | 实体类耦合 / 手写 SQL | 视图任意 JOIN（一等公民） |
| 消费 API | 手写 Provider/DTO 映射 | **自动 DTO + REST/GraphQL 默认暴露** |

### 3.3 机制验证（2026-09-30 实证）

- **ViewSqlColumnValidator** 不解析 SQL 表达式语义，只提取 `AS 别名` 与 C# 属性名双向比对——`concat/substr/substring/CASE/CAST/COALESCE` 任意表达式**带 AS 别名即通过**（与 SUM/ROW_NUMBER/CAST 同路径，测试实证）
- **DTO 按 C# 属性声明生成**（类型由 C# 侧决定，框架不校验 SQL 返回类型）
- **FreeSql 查询层对表达式透明**（表达式在视图定义时物化为列，查询只按 `[Column(Name)]` 映射列名）
- **SyncViewsAsync 完全支持消费方/装配实例自有视图**（ADR49 决策 6："已启用扩展的视图纳入 SG 聚合清单，SyncViewsAsync 代码零改动即覆盖"）；表先于视图有框架级硬保证（SyncTables → SyncViewsAsync 固定序，消除 PG 42P01）

### 3.4 边界（必须认知，避免滥用）

| 边界 | 说明 |
|------|------|
| **同库硬前提** | VEntity JOIN 限定单连接串（D17A 裁定 3）；跨库走数据库引擎能力（FDW/Linked Server）或契约+拼装（裁定 1） |
| **只读** | VEntity 禁写（IDomainViewEntity 守卫）；写操作归属主实体 + 服务（裁定 1） |
| **递归不能下推** | 树形组装（OU/File 目录）、重复展开（Calendar）仍需内存递归——VEntity 只下推 JOIN/投影 |
| **视图链禁令** | ViewSql 只允许引用基表，禁止引用其它 `vw_` 视图（编译期 VIEW002）——脱敏视图不能嵌套 |
| **生产 DBA** | SyncViewsAsync 仅开发环境自动建；生产需 DBA 手动执行 ViewSql（写入指南） |

---

## 四、VEntity 读模型联邦

### 4.1 模型

```
写模型（Entity）：每扩展单一，SG1 建表 + DataService（ADR61 自动注册）
读模型（VEntity）：按查询需求按需构建，SG1 自动 DTO/StatsDto + REST/GraphQL 默认暴露
联邦：消费方/装配实例按业务场景组合读模型（组合视图或多次查询）
```

### 4.2 两条暴露策略（关键取舍）

| 策略 | 形态 | 适用 | 代价 |
|------|------|------|------|
| **接口不变**（现状 Identity/Notifications） | 视图行映射回原实体，对外接口零迁移 | **有外部消费方/历史负担**时 | VEntity 独有列被丢弃，读模型"降格为查询手段" |
| **直接暴露**（最优形态） | VEntity DTO + `[GenerateController(FromDataService=true)]`/`ExposeGraphqlQuery` 直接可查 | **内部测试/无历史负担**（当前阶段） | 视图 schema 变更影响 API 契约（需版本化策略） |

**倒推纪律（2026-09-30 用户裁定）**：**当前内部测试、无历史负担——倒推时直接采用"直接暴露"最优形态，不需要保留兼容路径**。仅当出现外部消费方（对外发布/第三方接入）后再评估兼容路径（届时按"保留兼容路径 + 增量新增"迁移）。

### 4.3 选型判断（何时 VEntity / 何时内存拼装）

```
跨扩展/跨表聚合需求
├─ 纯投影聚合（无业务逻辑、列拼接/过滤/分组）  → VEntity（裁定 2）
│    └─ 含 SQL 可表达的确定性安全规则（如手机号前3后4脱敏）→ 归投影，可下推 SQL（§5.2）
├─ 有业务逻辑（判定/编排/递归/展开/动态角色级规则） → 内存拼装（裁定 1，主键+服务+拼装+缓存；安全规则走门面软脱敏）
├─ 高频且固定视图                             → VEntity + 业务缓存
└─ 写路径关联（幂等检查等）                   → 服务调用（裁定 1）
```

> **脱敏归属澄清（Oracle3 P4, 2026-09-30）**：SQL 可表达的**确定性**规则（固定掩码模板 → 投影，下推 SQL 硬脱敏）；**动态/角色级**规则（依赖当前用户上下文 → 业务逻辑，走门面软脱敏）。判定标准 = 规则是否依赖调用时上下文。

---

## 五、投影下沉（安全逻辑前移到读模型）

### 5.1 核心收益与边界

**收益**：脱敏/裁剪/预聚合下沉到 ViewSql——**任何消费路径自动生效**（GraphQL 直查 / REST / 内部 DAC 直查全过视图），消灭"门面漏脱敏"。
**边界**（2026-09-30 验证）：
1. 表达式**必须带 AS 别名**（否则非简单标识符 → 判"缺失列" → 运行期阻断启动）
2. **方言差异**：`substr`/`CASE`/`COALESCE`/`CAST` 双方言一致；`concat` PG13+/SQLite 3.44+ 版本依赖；`substring(s from p for l)` 仅 PG——倾向 `substr + ||` 拼接，备 ViewSqlSQLite 变体（ADR53）
3. **零先例**需 Tier 1.5 SQLite 真实视图测试验证（Tier 1 内存 DAC 无 VEntity 填充）
4. 生产 DBA 需审核含脱敏逻辑的视图 DDL

### 5.2 分层脱敏（推荐模型）

| 层 | 规则 | 例 |
|----|------|-----|
| **SQL 硬脱敏**（读模型层） | 简单**确定性**规则（固定掩码模板），所有路径自动覆盖 | 手机号前 3 后 4 |
| **门面软脱敏**（应用层） | **动态/角色级**规则（依赖调用时上下文），业务逻辑 | 按用户角色裁剪档案字段 |

> **归属判据（P4）**：规则是否依赖调用时上下文（当前用户/角色/租户）——上下文无关确定性 → SQL；上下文相关动态 → 门面。注意 SQL 视图无法感知"仅本人"（当前用户过滤是查询参数/全局过滤，非视图投影）。

---

## 六、契约实现方归位（扩展间依赖倒置生态）

### 6.1 机制

传统：主项目实现扩展接口（Provider 样板，装配层手写桥接）。
TKWF：**数据属主扩展在自身 Initializer 内实现他扩展契约**——先例：`IdentityExtensionInitializer L47 TryAddScoped<IAccountPasswordManager, IdentityPasswordManager>()`（Account.Abstractions→Identity 实现）、`L51 AddScoped<IRoleProvider, IdentityRoleProvider>()`（覆盖 Permissions 默认）。依赖方向单向无循环（csproj 只引 .Abstractions，L2 门控合规）。

### 6.2 装配时序（必须掌握）

- 扩展 Initializer 钩子**先于**消费方 `OnRegisterDomainServices` 执行
- 扩展内注册可覆盖同扩展 TryAdd 默认（用 AddScoped）
- 消费方要覆盖扩展默认 → 须 AddScoped（TryAdd 被跳过）
- **DataService 零手动注册**（ADR61 铁律）——实体型/只读型 DataService 全部 SG 自动注册，Initializer 只注册门面

### 6.3 契约包放置判据（对齐 G17A §7）

| 实现方 | 契约位置 | 先例 |
|--------|---------|------|
| 其它扩展（终态） | **`.Abstractions` 契约包** | Account.Abstractions→Identity；Permissions.Abstractions→Identity |
| 装配实例/消费方 | 主包内定义即可 | IUserEmailProvider / ISmsSender / IAuthorizationMapper |

---

## 七、"更优解"判据（9 维框架）

> 评价任何扩展设计是否"更优"——不是风格偏好，是可量化的维度。核心张力：**前移/消除/Schema 组合倾向 VEntity；边界可执行/演进平滑倾向契约层——更优解不是消灭契约层，而是让契约层与读模型层各司其职**。

| # | 维度 | 判据 |
|---|------|------|
| 1 | 运行时责任前移度 | 多少运行时决策（多态/内存拼装/脱敏后处理）前移到编译期或 SQL 层 |
| 2 | 样板消除度 | 减少多少手写代码（桥接/映射/DTO/注册） |
| 3 | 边界可执行性 | 边界是编译期门控（Error）还是文档约定 |
| 4 | Schema 组合能力 | 能否在数据层组合多扩展数据 |
| 5 | 消费方零桥接度 | 装配层是否需要手写适配器 |
| 6 | 读模型保真度 | 读模型是一等公民（独立 DTO 暴露）还是查询手段（映射回实体） |
| 7 | 数据归属清晰度 | 写归属主、读可联邦 |
| 8 | 可测性 | 编译期/SQL 层可测（Tier 1.5 真实视图）vs Mock 大量运行时协作 |
| 9 | 演进平滑度 | 过渡→终态增量还是推翻 |

---

## 八、倒推路线图（持续更新——每次优化迭代修订）

### 8.1 现状盘点（2026-09-30，27 扩展）

| 形态 | 扩展数 | 占比 | 说明 |
|------|:---:|:---:|------|
| VEntity 读模型 | 2 | 7.4% | Identity / Notifications（均"映射回原实体"策略） |
| 内存拼装/两步查询 | 6 | 22.2% | OrganizationUnit / FileManagement / DataDictionary / Calendar / Approval / PrintTemplates |
| 契约抽象 | 13 接口 | — | ADR48 D7 全量落地（红线全量整改，主线零 IFreeSql/IEntityDAC 直注入） |
| 纯内存/单表/Web | 其余 | — | 不适用 VEntity |

### 8.2 可 VEntity 化候选优先级（2026-09-30 探针细化）

| 优先级 | 扩展 | 当前次优形态 | 潜在更优解 | 迁移成本 | 状态 |
|:---:|------|------------|-----------|:---:|:---:|
| 🟢 | **Approval** | 两步查询（实例→任务链） | `vw_ApprovalTaskView`（JOIN 基表，21 投影列；实例查询保留取 BusinessDataJson） | 低 | ✅ **方案已评审**（02 案例，oracle3 PASS WITH CONDITIONS） |
| 🟢 | **PrintTemplates** | 两步查询（模板→版本） | `vw_PrintTemplateVersionView`（JOIN 基表，12 投影列含 Key/TemplateName；**真消除往返 2→1**） | 低 | ✅ **方案已评审**（同上） |
| 🟡 | **DataDictionary** | 两步查询 + 内存树形 | `vw_DictionaryItemView`（JOIN Definition→Item；BuildTree 递归留内存；缓存仍有效；**"定义存在无项"语义保留**——先单查定义 + 视图查项零行=空项） | 中 | ✅ **方案已评审**（04 案例，oracle3 PASS WITH CONDITIONS） |
| 🟡 | **OrganizationUnit** | 6 处全量内存拼装 | 可下推 3 处：`GetSubTreeAsync`（Path 前缀）/`GetUserIdsInOrganizationUnitAsync`（JOIN OU→OUUser）/`GetAncestorsAsync`（Code IN 分段）；`GetTreeAsync` 递归留内存；**L137 计数本期不改**（CountByParentIdAsync 语义确认后） | 中 | ✅ **方案已评审**（04 案例，同上） |
| 🔴 | **FileManagement** | 全量内存建树 | **无需 VEntity 化**：`GetFolderTreeAsync` 全树递归必须留内存；`GetSubFoldersAsync`/`SortOrder max+1` 已 SQL 下推 | — | ⚪ 排除（04 方案 C5 裁定确认） |
| 🟢 | **Identity** | 映射回原实体（保守） | **直接暴露升级**——GraphQL 已就绪（`ExposeGraphqlQuery=true`）；REST 需 **Service 包装类**（非 DataService 命名，`[GenerateController]`）；**仅本人防护**（userId 从 IDomainUser 取——防 IDOR） | 低 | ✅ **方案已评审**（03 案例，oracle3 PASS WITH CONDITIONS） |
| 🟢 | **Notifications** | 映射回原实体（保守） | 同上——`UserNotificationView` 收件箱敏感面，**ExposeGraphqlQuery 回溯审查**（§九 F4/F11）+ REST Service 包装类（name 可空查全部） | 低 | ✅ **方案已评审**（03 案例，同上） |
| 🟢 | **认证中心（Authentication）** | v0.1.0 实施后能力补强 | **查询契约缺口**（无 `IAuthAccountQueryService`——UserCenter 方案 P2 注记）+ **UserCenter 终态承接**（认证中心实现 `IUserProfileSource`——§5.9 终态演进落地，装配实例零桥接） | 低 | ✅ **方案已评审**（05 案例，oracle3 PASS WITH CONDITIONS） |

> **迁移策略（2026-09-30 用户裁定）**：当前内部测试、无历史负担——倒推**直接采用最优形态**（VEntity 直接暴露），不留兼容双轨；迁移成本仅含"改实现 + 同步测试宿主"，不含"保留旧 API"。
> **节奏调整（2026-09-30 用户指示）**：教学系列**不做完整案例文档**（如 02 仅方案+评审记录），**每完成一个扩展的实际升级 → 补产出一篇教学案例**（§一阅读地图）；当前以 UserCenter 范本为重心。
> **REST 直接暴露机制事实（bg_1dc747d4 探针确认）**：VEntity 手写只读 DataService **不支持 `[GenerateController(FromDataService=true)]`**——`ControllerGenerator.cs` L629-642 `isDataService`（命名约定 `EndsWith("DataService")`）门控 early-return，标了也不生成 REST；**REST 暴露唯一路径 = Service 包装类**（非 DataService 命名，继承 `DomainServiceBase`/`DomainReadOnlyDataServiceBase`，标 `[GenerateController]`，public async 方法自动纳入契约）。GraphQL 独立于 REST 门控（`ExposeGraphqlQuery` 直扫，已自动就位）。
> **Calendar 确认排除（2026-09-30 核查）**：`GetAllAsync` 单表动态过滤（启用日历判定）+ 事件查询已 C1 分路 SQL 下推——不适用 VEntity，§8.2 未列入为正确（非遗漏）。

### 8.3 认知修正（倒推时的纪律）

- **Identity/Notifications "映射回原实体" 是保守形态**（2026-09-30 用户裁定：内部测试、无历史负担）——倒推时**直接升级为"直接暴露"**：GraphQL 已就绪（VEntity DTO + `ExposeGraphqlQuery`）；**REST 经 Service 包装类暴露**（非 `FromDataService`——机制不支持，§8.2 注记）；VEntity DTO 一等公民（可直接作为返回类型）。"破坏既有 API"当前不是障碍（唯一消费方是随仓库同步升级的测试宿主）
- 树形组装/重复展开/单表动态过滤**不适用 VEntity**（递归需内存 / 非跨表）
- 契约化已全量落地——无"该契约化却手写桥接"场景
- **聚合统计**（GroupBy/Count）不在 VEntity 能力内——4 扩展 6 处内存 GroupBy 走框架 **F7 候选**（`GroupCountAsync`/`GroupByAsync` 聚合 API 下推）。**F7 需主框架侧开发方案**（`FreeSqlQueryableExtensions` 增 API，`_TKWF/docs/02-迭代开发/` 流程），**扩展侧不需独立方案**——框架落地后各扩展替换调用（轻量改造随扩展常规迭代）

---

## 九、框架演进候选（按需完善框架——机制不足优先优化机制）

> 用户裁定（2026-09-30）："遵循机制，机制不足优先优化机制"。本系列探索中发现的框架级机会登记于此，触发后再立项（V5 候选）。

| # | 候选 | 描述 | 触发条件 | 状态 |
|---|------|------|---------|:---:|
| F1 | **`ReadModelProjection<T>` 契约自动实现** | VEntity 显式标注投影目标契约，SG 检测满足关系自动生成适配器 + 自动注册（规避鸭子类型误判） | 扩展间契约 + 读模型自动接线成为高频需求 | ⚪ V5 前瞻（立 ADR 锁方向） |
| F2 | **D17A 裁定 1 措辞澄清** | 明确"纯投影聚合（无业务逻辑）应优先 VEntity；内存拼装用于有业务逻辑场景"（防裁定 1 被滥用为"全都内存拼装"） | 随本系列推广即可推进 | ⚪ 建议近期修订 |
| F3 | **VEntity 视图版本化** | 视图 schema 变更影响消费方直接查询 API 的契约演进策略（`vw_x_v2` / GraphQL schema 演进） | 直接暴露读模型成为主流后 | ⚪ 观察 |
| F4 | **跨扩展 DBA 建视图规范** | 跨扩展组合视图的生产部署指南（依赖表清单 + 多方言变体 + 建表前置条件） | 首个跨扩展视图（UserCenter v0.2.0）落地 | ⚪ 随 UserCenter |
| F5 | **组合 VEntity 模板化** | SG 基于源表结构生成组合 ViewSql 模板（降低装配层手写成本） | JOIN 语义可模板化前（当前 JOIN 条件/聚合是业务决策，风险高） | ⚪ V5 探索 |
| F6 | **仅本人过滤（OwnerFilter）** | `IGlobalQueryFilter` 扩至按当前用户过滤——新增 `IOwnerQueryFilter`/`OwnerGlobalQueryFilter`，对含 `OwnerId`/`UserId` 字段实体自动 `WHERE ... = context.User.Id` | UserCenter C3 方案 A / 任何"仅本人"数据隔离（现 `TenantGlobalQueryFilter` 是唯一实现，无 UserId 标准机制） | 🟢 随 UserCenter + 倒推优化 |
| F7 | **`GroupCountAsync`/`GroupByAsync` 聚合 API** | `FreeSqlQueryableExtensions` 增分组聚合下推（`SELECT key, COUNT(*) ... GROUP BY key ORDER BY count DESC LIMIT n`）——替代 4 扩展 6 处 `Take(100_000)` 内存 GroupBy | AuditLogging/Tagging/SecurityLog/BackgroundJobs TopN 聚合场景 | 🟢 建议近期立项 |
| F8 | **VEntity 使用指南 + 脚手架模板** | G17A 补 VEntity 声明/手写 DataService/生产 DBA 规范；`tkwf-entity` skill 增 VEntity 模板；D17A §7.4"无业务级实例"、27 扩展仅 2 采用（认知+框架双因） | 配合倒推路线图首个案例（Approval/PrintTemplates）落地 | 🟢 随本方案 2 |
| F9 | **StatsDto 门控扩展 MIN/MAX** | xCodeGen `Engine.cs` L78-81 StatsDto 生成门控从 `SUM/COUNT/AVG` 扩至含 `MIN/MAX` | 报表 MIN/MAX 聚合场景（实施成本低） | 🟡 观察 |
| F10 | **方言变体维护负担降低** | VIEW003 已门控；提议方言变体模板（PG 为主自动推断）+ 变体验证（VIEW004 语义一致性）；远期才考虑自动翻译（重） | 多方言变体成为扩展开发负担（当前 5 方言手工翻译） | 🟡 观察 |
| F11 | **默认不暴露安全策略（Opt-in）** | `QueryExposureDefaults.cs`：VEntity 默认 `ExposeGraphqlQuery = true`——敏感视图（审批/模板正文/组合视图）应显式 `false` 经门面（方案 2 C4/UserCenter C3 方案 B 实证） | 安全审计要求统一暴露策略（默认收敛） | 🟡 观察 |
| F12 | **物化路径树形查询模板** | 新增"物化路径实体"模板（`[DomainGenerateCode(IsTree=true, PathColumn="Path")]`）自动生成 `GetSubTreeAsync`（Path LIKE 下推）/`GetAncestorsAsync`/`CountChildrenAsync` | OU 6 处 + File 1 处 + DataDictionary 1 处全量内存拼装（Path 列已就绪） | 🟡 观察 |
| F13 | **表名命名约束（SG1 门控 + G17A 指南）** | 新增 `TKWF_SG1_TABLE001` Warning（表名不含扩展名前缀）；G17A §3.1 补命名约定；`[Table]` 支持 `ExtensionPrefix` | 单库部署下 27 扩展裸表名冲突风险（`Setting`/`Notification` 先例）——先文档引导，门控仅 Warning | 🟡 建议先文档 |
| F14 | **VEntity + 缓存组合模式** | `ICachedReadOnlyDataService<T>` 装饰器 / `[Cacheable(Duration)]` 特性自动包裹只读 DataService 查询 | DataDictionary 高频只读聚合 + 缓存两步查询摩擦点 | 🔴 低 |
| F15 | **PocoDto 裁剪优化** | FreeSql 非实体 DTO 1:1 全列回退（v4.9.112 G2 实证）——`?fields`/裁剪场景 SQL 带宽未优化 | 裁剪/带宽优化成为消费方痛点 | 🔴 低 |
| F16 | **SyncViewsAsync 接线增强** | 视图同步策略（Auto/Manual/Hybrid）+ 同步审计 + 回滚 | 跨扩展组合视图普及、运维需要统一控制 | 🔴 低 |
| F17 | **InlineSelectSql 方言变体 + 校验** | 内联 SQL 无方言变体/无 SyncViewsAsync/无 VIEW001-002 校验（`EntityMetadataGenerator.cs` L1190-1192） | 多数据库消费方用 InlineSelectSql 遇方言差异 | ⚪ 观察 |
| F18 | **视图链放宽（Composed View）** | VIEW002 视图链禁令放宽为显式声明 `[ViewChain(DependsOn=...)]` 自动拓扑排序——**与 UserCenter C1（JOIN 基表）不冲突**：C1 是组合视图应 JOIN 基表（性能+解耦），F18 是"视图引用视图"的多层聚合替代方案 | 多层聚合视图需求（汇总→部门→公司）出现后 | ⚪ 远期探索 |

---

## 十、实践清单（速查）

```
设计一个扩展的读能力时（自问三步）：

1. 数据在谁手里？
   ├─ 本扩展自己 → 建 Entity + 表；读面用 VEntity 还是门面？（见 3）
   └─ 别的扩展 → 引 .Abstractions 契约（ADR48 D7）；实现方 = 数据属主扩展/装配层
2. 跨模块聚合怎么组装？
   ├─ 纯投影（同库）→ VEntity 组合视图（编译期校验 + 自动 DTO + 自动暴露）
   ├─ 有业务逻辑 → 主键 + 服务 + 内存拼装 + 业务缓存（裁定 1）
   ├─ 递归/展开 → 内存（VEntity 只下推 JOIN）
   └─ 跨库 → 数据库引擎能力 或 事件最终一致
3. 安全投影（脱敏/裁剪）放哪？
   ├─ 简单确定性规则 → SQL 硬脱敏（AS 别名必写 + substr/|| 双方言安全 + Tier 1.5 测试）
   └─ 动态/角色级 → 门面软脱敏（业务逻辑）
4. 消费方怎么用？
   ├─ 契约（IUserProfileSource 等）→ 扩展内 TryAdd 注册实现（扩展钩子先于消费方）
   ├─ 直接读模型 → VEntity DTO + [GenerateController]/ExposeGraphqlQuery 直接暴露
   └─ 装配组合 → SyncViewsAsync 自动建视图（开发）+ DBA 手动（生产）
```

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0 | 初始——UserCenter 案例深挖（Oracle 裁决 + 五路机制验证）：七个根本差异 + Schema 级 Open-Closed + VEntity 读模型联邦 + 投影下沉 + 契约实现方归位 + 9 维判据 + 倒推路线图（27 扩展盘点）+ 框架演进候选（F1-F5） |
| 2026-09-30 | v0.1.1 | 用户裁定修订：**当前内部测试、无历史负担——倒推直接采用"直接暴露"最优形态，不留兼容双轨**——§4.2 倒推纪律重写 + §8.2 迁移策略注记 + §8.3 认知修正（Identity/Notifications"映射回原实体"定性为保守形态，倒推直接升级为 VEntity DTO 直接暴露；"破坏既有 API"当前不是障碍） |
| 2026-09-30 | v0.1.2 | Oracle3 评审（`bg_d2bc9fc2`）P4 采纳——§4.3 选型树 + §5.2 分层脱敏表澄清**脱敏归属**：SQL 可表达的**确定性**规则（固定掩码）→ 投影下推；**动态/角色级**规则（依赖调用时上下文）→ 门面软脱敏。判据 = 规则是否依赖调用时上下文。同时 §5.2 注明"SQL 视图无法感知'仅本人'（当前用户过滤是查询参数/全局过滤，非视图投影）" |
| 2026-09-30 | v0.1.3 | §九 框架演进候选扩充 **F6-F18**（两路机制探针合并去重）：bg_a7da85a5（VEntity 机制深挖 8 维 27 候选）+ bg_6dbfbdb2（扩展实践摩擦 5 候选）；综合 RLS 业界调研（bg_8aaba03b）——F6 仅本人过滤（OwnerFilter，含 DB RLS 纵深——PG owner 豁免陷阱/sql_invoker 四件套/SQL Server 天然继承，慎用 DB 级）、F7 GroupByAsync 聚合下推、F8 VEntity 指南+脚手架、F9-F18 中低优先级登记 |
| 2026-09-30 | v0.1.4 | §八 路线图探针细化（bg_2927440c OU+DataDict+File / bg_1dc747d4 Identity+Notifications）：Approval+PrintTemplates ✅ 方案已评审（02 案例 oracle3 PASS WITH CONDITIONS）；DataDictionary/OU 可下推点确认、FileManagement 排除（全树递归留内存）；**REST 直接暴露机制事实登记**（VEntity DataService 不支持 `[GenerateController(FromDataService=true)]`——isDataService 门控 early-return，REST 需 Service 包装类；GraphQL 独立已就绪）；§8.3 认知修正补"聚合统计走 F7 非 VEntity"。**节奏调整注记（用户指示）**：教学系列不做完整案例文档，每完成实际升级 → 补案例篇；当前以 UserCenter 范本为重心 |
| 2026-09-30 | v0.1.5 | §八 路线图状态更新——**03/04 方案 oracle3 评审 PASS WITH CONDITIONS 全修订**：Identity/Notifications ✅ 方案已评审（03 案例——REST Service 包装类 + 仅本人防护 C1-high IDOR + name 可空 + 注册机制核实）；OrganizationUnit/DataDictionary ✅ 方案已评审（04 案例——H1 空项语义 + H2 L83 标签更正 + H3 L137 CountByParentId + F12 更正 + LIKE 转义 + 双模式）；FileManagement ⚪ 排除（C5 裁定确认） |
| 2026-09-30 | v0.1.6 | **05 方案 oracle3 评审 PASS WITH CONDITIONS 全修订**（认证中心 v0.2.0 查询契约 + UserCenter 承接）：C1 过渡桥接类文档示例澄清 / C2 命名碰撞（AuthAccountUserProfileSource）/ C3 原子条件 UPDATE 严重度分层（safety 待办）/ C4 微信推导措辞 / C5 IdentityPasswordManager 先例精确化 / C6 两契约边界显式陈述；§8.2 认证中心 ✅ 方案已评审；§8.3 补 F7 主框架侧归属注记（扩展侧不需独立方案）+ Calendar 确认排除 |
| — | — | （后续：每优化一个扩展模块 → 产出教学案例篇 + 修订 §八 路线图；机制缺口 → 修订 §九） |