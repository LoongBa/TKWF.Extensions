# 微信公众号运营 GUI 拆分方案（WeChatOperations.UI 前置契约）

> **状态**：评审中（待 Oracle 审核） · **日期**：2026-11-06 · **版本线**：WeChatOperations.UI（未来拆回，独立版本线）
> **关联**：`docs/WeChatOperations/微信公众号运营扩展-开发方案.md` §八（2026-10-18 承诺交付物 6——**本文件履约**）；`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`（2026-11-05 WeChatChannels 拆分）；主框架 v4.10.73 ADR106（页面装配机制：IPageContributor + PageAssemblyWebExtension + spec 词汇）
> **分工裁定（2026-11-06 用户）**：扩展模块的**业务装配 / UI 装配的设计方案与模式由框架组/扩展模块组设计**（不限于联邦/微信，常态规则）——**实现/拆分在 DMP-Lite 项目的 Merchant.AdminUI / Platform.Web 完成**，验证后回归扩展项目

---

## 一、背景与承诺溯源

1. **2026-10-18 公众号运营方案**（§3.3 / 交付物 6 / §八）：GUI 阶段二可视化编辑器——**本仓库先不做，出拆分方案交 DMP-Lite 项目实现，磨合界面装配机制后拆回本仓库为 `WeChatOperations.UI` 扩展**，沉淀「带 GUI 的扩展拆分」skill。承诺交付文档 `docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（边界契约 + 回收验收 + skill 提炼计划）——**本文件为该交付物**。
2. **2026-11-05 WeChatChannels 拆分**：通道底座独立扩展（平台模式），运营扩展依赖契约复用（租户模式）——GUI 页面清单随之分属两个扩展侧（平台通道管理 / 租户运营管理），双白名单语义。
3. **2026-11-06 分工裁定升级为常态规则**：UI 装配模式设计归扩展模块组（不限于联邦/微信），装配实现归消费方（DMP-Lite 的 Merchant.AdminUI / Platform.Web）——本方案即微信域该规则的首个实例。

---

## 二、分工模式（常态规则，2026-11-06 裁定）

| 层 | 归属 | 内容 |
|----|------|------|
| **装配模式设计** | 框架组 / 扩展模块组 | 页面贡献契约（IPageContributor + spec 词汇）、装配形态（白名单 + PageAssemblyWebExtension）、路由/权限/配置节约定、页面清单与关切定义 |
| **装配实现** | 消费方（DMP-Lite） | Merchant.AdminUI（租户运营面）/ Platform.Web（平台通道面）的页面 UI 实现（技术栈/布局/UX 自由） |
| **回归** | 扩展模块组 | DMP 实证后按回收验收标准拆回 `.UI` 扩展（StaticWebAssets/RCL）+ 沉淀 skill |

**原则**：模式单源（防各消费方装配形态漂移——P2 唯一权威事实源 + P5 显式决策）；实现自由（表现层关切消费方自治——P4 承诺管辖）；事后回归成本低于事前定契约。

---

## 三、装配机制现状（关键前提）

| 机制 | 状态 | 说明 |
|------|------|------|
| `PageAssemblyWebExtension`（v4.10.73 ADR106） | ✅ 可用 | 装配一行 `UseWebExtensions(e => e.Add<PageAssemblyWebExtension>())` + 页面静态路由；白名单一行 + `Tkwf.Page.load` 消费形态 |
| `IPageContributor` 接口本体 | ❌ **未落地**（v4.10.73 明示待实现：接口本体 + TS codegen + `@tkwf/reference-components`） | 页面贡献机制基建缺口（框架侧后续，落地后另行通知） |
| R2 路径（消费方自研前端直连 REST） | ✅ 可用兜底 | GUI 在 IPageContributor 落地前用此路径，待机制可用后声明式贡献（§八 4） |

**处置**：微信域 GUI 按 R2 路径起步（消费方自研前端直连 `[GenerateController]` 端点）；IPageContributor 落地后按 `Tkwf.Page` 形态声明式贡献（不阻塞、可演进——零破坏切换）。**⚠️ R2 增长上限（Oracle2 C4）**：`IPageContributor` 落地后，**新页面必须用声明式贡献**，既有 R2 页面按迁移计划迁离——R2 不得用于新页面（防漂移为 de-facto 标准，P5 显式决策锁定）。

---

## 四、GUI 页面清单（微信域）

### 4.1 平台侧（Platform.Web，装配 WeChatChannels——平台模式）

| 页面 | 关切 | 消费端点（REST） | 权限键 |
|------|------|-----------------|--------|
| 通道管理（列表/详情/编辑） | 公众号通道 CRUD + 凭证（AES-GCM 管理）+ 能力状态 | `IChannelConfigService` `/api/wechat-channels/channels` | `WeChatChannels.Query/Manage` |

### 4.2 租户侧（Merchant.AdminUI，装配 WeChatChannels + WeChatOperations——运营模式）

| 页面 | 关切 | 消费端点（REST） | 权限键 |
|------|------|-----------------|--------|
| 素材库 | 素材 CRUD + 引用反查（跨 ReplyRule 聚合） | `IMaterialService` `/api/wechat-ops/*` | `WeChatOperations.Manage` |
| 回复规则 | 全消息类型规则编辑器（14 分支映射 + 排序 + 启用） | `IReplyRuleService` | 同上 |
| 菜单管理 | 菜单树可视化编辑（raw JSON 自由形态）+ 发布链（快照/版本/审计）+ 当前菜单反查 | `IMenuService` | 同上 |
| 订阅统计 | 按日趋势/汇总卡（新增/取消/净增/累计） | `ISubscriptionStatService` / `IWeChatOperationsStatusService` | 同上 |
| 交互日志 | 日志检索 + 匹配过程明细（MatchTraceJson）+ 转发日志 | `ILogQueryService` / `IMessageForwardDispatcher` 对账面 | `WeChatOperations.Query` |
| **网关管理** | **路由规则编辑器（CRUD + 排序 + 启用）/ 转发端点管理 / 熔断状态查看 / 运行时开关** | `IMessageGatewayService` `/gateway/*`（rules×3 + endpoints×3 + status + enabled + forward-logs，**Oracle2 C1 补齐**） | `WeChatOperations.Manage` |
| 状态卡 | 网关状态/规则命中/端点健康聚合 | `IWeChatOperationsStatusService` | `WeChatOperations.Query` |

> 页面清单以既有门面契约为准（不新增图面 API——缺能力走转达框架组/扩展组补契约原语，禁止 GUI 直连 DB/ORM/微信）。

---

## 五、边界契约（GUI 实现必须遵守——自运营方案 §八 扩充）

1. **只消费 REST**：GUI 页面仅经 `[GenerateController]` 端点（`User.Use<T>` 同一路径，DI004）——**不直连微信、不读 DB、不引平台库 DLL、不注 ORM**
2. **自备前端资产**：SPA 静态资源（StaticWebAssets/RCL——ADR106 决策 5 许可路径）、消费方自研前端直连 REST（R2 路径）
3. **装配一行**：`UseWebExtensions(e => e.Add<PageAssemblyWebExtension>())` + 页面静态路由；Domain 白名单双声明（WeChatChannels + WeChatOperations——**漏声明 WeChatChannels → keyed provider 缺失 → 运行时 throw**，WeChatChannels 拆分方案 O4 警示）
4. **界面装配机制**：IPageContributor 未落地 → R2 兜底；落地后 `Tkwf.Page` 声明式贡献（零破坏演进）
5. **菜单 poco 共享**：GUI 不引平台库 DLL——菜单结构 raw JSON（编辑器自由形态，不类型化）；如需类型化拆 Abstractions 契约包（Oracle P2 待触发）
6. **安全红线继承**：凭证即错即脱敏（DTO 投影不含凭证 + 出站日志脱敏）；页面不落明文
7. **配置节**：平台侧 `TKWF:WeChatChannels`（密钥路径/IsProduction）；租户侧 `TKWF:WeChatOperations`（运营字段）——GUI 读取经既有 Options 透出，不新开节

---

## 六、路由 / 权限 / 白名单速查

| 项 | 平台侧 | 租户侧 |
|----|--------|--------|
| 扩展装配 | `WeChatChannelsExtensionInitializer<>`（+ Federation 已有） | **双声明** `WeChatChannelsExtensionInitializer<>` + `WeChatOperationsExtensionInitializer<>` |
| 端点前缀 | `/api/wechat-channels/*`（非 Web 形态，消费方 SG2 生成控制器） | `/api/wechat-ops/*`（运营扩展生成端点） |
| 权限键 | `WeChatChannels.Query/Manage` | `WeChatOperations.Query/Manage`（沿用不变） |
| 页面装配 | Platform.Web 页面 | Merchant.AdminUI 页面（`/m/{mid}/` 租户路径下） |

---

## 七、回收验收与拆回条件

### 7.1 拆回产物：`TKWF.Ext.WeChatOperations.UI`（或 `WeChatChannels.UI`）

- `.UI` 扩展**零持久化**（仅静态资源 + 装配钩子 + REST 消费），可独立发布（独立版本线，MinVerTagPrefix 独立）
- 页面实现经消费方前端项目 → `.UI` 包 StaticWebAssets/RCL 迁移路径可执行
- 权限键/路由/白名单形态与界面装配契约一致（本方案 §五/§六）——拆回零契约变更

### 7.2 拆回条件（自运营方案 §八 扩充）

1. **IPageContributor 落地**（页面贡献机制可用——框架侧缺口的驱动信号）
2. **GUI 稳定**（Oracle2 C2 具象化——P5 显式决策，替代原"N 次迭代"）：(a) §四 全量页面（含 C1 网关管理页）已实现并上线生产；(b) 无 P0/P1 UI 缺陷 open；(c) 最近 2 次迭代**转达环静默**（无契约缺口返工）
3. **资产可迁移**：消费方前端资产与扩展包 StaticWebAssets 形态对齐（转换路径验证）
4. **转达闭环**：DMP 实现中暴露的模式缺口（装配机制/契约原语）已转达并按需修复
5. **契约稳定性**（Oracle2 C4）：最近 K 次迭代门面契约（REST 端点/DTO/权限键）无破坏性变更——回归即冻结不稳定契约
6. **权限键定稿**（Oracle2 C4）：`WeChatChannels.Query/Manage` / `WeChatOperations.Query/Manage` 回归前定稿（回归后变更须破坏性版本）

### 7.3 回收动作

- 拆回后沉淀「带 GUI 的扩展拆分」skill（范式对齐 tkwf-extension §4.11 / tkwf-use-extension §4.7——**页面贡献=接口即意图声明 + spec 纯契约名 + 跨扩展引用白名单门控**）
- 主框架跟踪表登记 `WeChatOperations.UI` 新行

---

## 八、skill 提炼计划（带 GUI 的扩展拆分范式）

1. **过程沉淀**：DMP 实证路径（R2 兜底 → IPageContributor 声明式）全记录——含装配形态/权限键迁移/资产打包
2. **产出**：`tkwf-extension` skill 补「GUI 扩展拆分」小节（对齐既有 V2.x 版本体系）+ 通用指南 §UI 装配分工节引用
3. **验收**：第二个 GUI 扩展（不限微信域）按 skill 可独立走通

---

## 九、评审纪律与变更记录

1. **本方案 Oracle 评审**（实施前必做——本期交付物为方案本身）
2. DMP 实现完成后：code-reviewer 迭代审核 → 回收验收（§七）→ 审核报告；**拆回时另起 `WeChatOperations.UI` 开发方案 → Oracle 评审（实施前必做，Oracle2 C3——新建扩展循 AGENTS §3 步骤 3）→ 实施 → code-reviewer 审核 → 审核报告**
3. 主框架 `总览和跟踪.md` 微信域状态勾选；WEB GUI 拆回时更新根 README 版本表

| 日期 | 变更 |
|------|------|
| 2026-11-06 | 初稿：履约 2026-10-18 方案交付物 6——§八 扩充为独立方案 + 2026-11-06 分工裁定（模式归扩展组/实现归消费方）纳入 + 页面清单（平台/租户双表）+ 回收验收扩充 |
| 2026-11-06 | **Oracle2 PASS WITH CONDITIONS 复审（C1-C6）→ 修订闭环**：C1 页面清单补**网关管理页**（9 `/gateway/*` 端点 CRUD/端点/熔断/开关——转达 doc 已列"转发网关页"）；C2 "N 次迭代"具象化（全量页面上线 + 无 P0/P1 + 转达环静默 2 迭代）；C3 §九 显式要求**拆回时另起 `WeChatOperations.UI` 开发方案 + Oracle 评审**（循 AGENTS §3）；C4 R2 **增长上限**条款（IPageContributor 落地后新页面必须声明式）+ 拆回条件补契约稳定性/权限键定稿 2 项；C5/C6 通用指南同步（见通用指南 §四之三）；小瑕疵 O4 归属措辞修正（→WeChatChannels 拆分方案 O4） |