# ADR-WeChatOperations-管理UI单双通道扩展边界

> **版本**：v0.1
> **状态**：已评审通过（Oracle PASS WITH CONDITIONS 2026-11-27，C-1~C-6 + CR-1~CR-4 已闭环；配套两份方案同步定稿）
> **关联**：本 ADR 配套两份开发方案——`docs/WeChatOperations/管理UI-运营面-开发方案.md`（WeChatOperations.UI.BlazorAntDesign 单通道商户面）+ `docs/WeChatChannels/管理UI-通道治理面-开发方案.md`（WeChatChannels.UI.BlazorAntDesign 多通道平台面）；取代混装方案 `docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（标注 superseded，零内容删除）；关键前置否决理由 `docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md` §二候选 A（单模块 + Mode 配置被否决）；常态分工规则 `docs/扩展模块通用指南.md` §四之三
> **Oracle 评审**：oracle5（2026-11-27 架构设计裁定 D1–D7）——"UI 边界轴 = 能力域（通道治理 vs 运营操作），非通道数；通道数是运营面 UI 内变体；两个 UI 扩展 = '装配即模式' 在 UI 层的正向应用，与被否决的 Mode 方案不同"
> **日期**：2026-11-27

---

## 目的与目标

确立联邦/微信**管理端 GUI** 的扩展边界纪律：**UI 扩展按"管理域/能力域"切分，不按"通道数（单/多通道）"切分**；"单通道/多通道"是运营面 UI 内部的运行时形态变体，由后端 B 守卫裁决，不新造 UI 模式开关。落地形态：新增两个 UI 扩展——`TKWF.Ext.WeChatOperations.UI.BlazorAntDesign`（运营面，商户管理端单通道语境）与 `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`（通道治理面，平台管理端多通道语境），与各自消费的后端扩展 1:1 镜像命名；消费方按"接哪个管理域"择一装配。读者 3 句话内明白：后端已否决"单模块 + Mode 配置"，因为模式开关会把两个权限域打包进一个包、无法隔离；UI 层若重蹈覆辙（做一个带"单/多通道开关"的 UI 包）等于复辟已被否决的决策；正确做法是两个包、消费方白名单选择即模式。

## 问题

### 问题现象

1. **现状只有一种 UI 接入**：扩展只提供 REST 端点（`[GenerateController]` + `[ApiExpose]`），前端由消费方自研直连（R2 路径）——本仓库零 Razor/Blazor/TS/页面贡献资产，`IPageContributor` 接口本体框架侧未落地（`docs/WeChatOperations/微信公众号运营GUI-拆分方案.md:29-37`）。
2. **规划态也只有一个 UI 扩展**：既有拆分方案只规划单一 `TKWF.Ext.WeChatOperations.UI.BlazorAntDesign`，且把**平台页集（通道管理）+ 租户页集（运营）混装在同一份方案/同一个包**（同方案 `:41-62,90-94`）——任何消费方接入都得背双域页面与权限面。
3. **用户诉求的语义陷阱**："单通道/多通道两种情况管理功能不一样、消费方按需接入其一"在 DMP-Lite 当前场景（商户端=单通道做运营，平台端=多通道管通道）**恰好**与能力域对齐——但这是巧合对齐，不是语义恒等：商户明天有 2 个公众号，他仍只需运营面（多一个通道选择器），不会突然需要通道治理面；平台若要"代运营"则需两 UI 包组合。若真按"通道数"做扩展边界，这两个场景都会破模型。

### 触发场景

- 设计/新增任何联邦/微信管理端 GUI 扩展时——本 ADR 为边界判据与形态样板。
- 消费方（DMP-Lite Merchant.AdminUI / Platform.Web）择一接入管理 UI，或后续平台"代运营"双挂两 UI 包时。
- 后续任何"要不要加一个单/多通道开关"的演进提案——本 ADR 提供否决依据。

### 现有方案的不足

- **方案 A（单 UI 包 + 单/多通道 Mode 开关）**：复辟已被后端否决的"单模块 + Mode 配置"——`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md:24-35` 明示否决理由：平台被迫打包运营全链、权限共键无法隔离、违背"装配即模式"哲学。UI 层同样会把 `WeChatChannels.Query/Manage` 与 `WeChatOperations.Query/Manage` 两套权限域塞进一个包。
- **方案 B（按通道数切两个 UI 包，运营面只服务单通道）**：语义错误——后端单/多通道无模式开关，是运行时数据形态（活跃通道计数 + B 守卫 `_Framework/Federation/FederationWebExtension.cs:599-618`：`active.Count>1` 且无通道段 → `CHANNEL_REQUIRED`；`==1` → 降级绑定）；运营面本就同时服务单/多通道，按通道数切会造出"商户 2 通道需第三个 UI 包"的死结。
- **方案 C（继续维护混装单方案）**：缺陷 3 原样保留——双域页面/权限面耦合，两消费方接入形态无法独立演进。

## 使用场景

| 场景 | 使用方式 |
|------|---------|
| 商户管理端（单通道语境，DMP-Lite Merchant.AdminUI） | 只接 `WeChatOperations.UI.BlazorAntDesign`：素材/回复规则/菜单/统计/日志/网关/运营配置页；消费 `/api/wechat-ops/*`；权限 `WeChatOperations.Query/Manage`；无通道选择器（后端 B 守卫单活跃通道自动绑定），无通道 CRUD/凭据页 |
| 平台管理端（多通道语境，DMP-Lite Platform.Web） | 只接 `WeChatChannels.UI.BlazorAntDesign`：通道列表/新建/编辑+凭据/能力状态页；消费 `/api/wechat-channels/*`；权限 `WeChatChannels.Query/Manage` |
| 平台"代运营"（对选定通道做运营） | `WeChatChannels.UI.BlazorAntDesign` + `WeChatOperations.UI.BlazorAntDesign` 双挂；运营页出现通道选择器（N>1 时按后端 `CHANNEL_REQUIRED` 错误驱动弹出），能力轴的自然组合，非第三扩展 |
| 商户从 1 个公众号扩到 N 个 | 仍只接 `WeChatOperations.UI.BlazorAntDesign`——选择器自动出现；不新增任何 UI 包，不引入模式开关 |
| 后续演进提案（"要不要加单/多通道开关"） | 按本 ADR 否决：边界按能力域，通道数是运营面内变体，模式 = 消费方白名单选择 |

**不适用边界**：本 ADR 只管管理端 GUI 扩展的**边界与装配形态**，不定义任何业务门面契约（REST/GraphQL 仍走既有 `[GenerateController]` + `.Abstractions`，权限标注不变）；不改变后端单/多通道的 B 守卫语义；不规定 UI 技术栈/布局/UX（归消费方自治，`docs/扩展模块通用指南.md` §四之三）。`IPageContributor` 未落地前，两 UI 扩展的页面资产由消费方托管（R2 路径），落地后声明式贡献——本 ADR 不阻塞该演进。

---

## 决策

1. **边界轴 = 能力域（管理域），非通道数**：两 UI 扩展按"通道治理 vs 运营操作"切分；"单/多通道"降级为运营面 UI 内的形态变体（N==1 无选择器 / N>1 错误驱动选择器），不新造模式开关、不复制后端 B 守卫逻辑到 UI。
2. **命名 = `{扩展}.UI.{技术栈}`（最优、非兼容；2026-11-27 定）**：`TKWF.Ext.WeChatOperations.UI.BlazorAntDesign`（运营面）+ `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`（通道治理面）。`.UI` = 表现层角色（层中立，可并列 `.UI.Maui`/`.UI.React`）；`.BlazorAntDesign` = 具体技术栈（**Blazor RCL + AntDesign**，RCL 宿主无关——WASM/Server 通用）；**不含部署角色词**（D10：不按 `dmp_xxx`/`Admin` 这类消费方部署事实命名）；**不占用 `.Web`**（本仓 `.Web` 已为端点层，如 `AuditLogging.Web`）。扩展取最优设计，消费方按此名适配。
3. **与 Mode 否决和解**：两 UI 扩展 = "装配即模式"在 UI 层的正向应用——模式 = 消费方白名单选了哪个 UI 扩展，零运行时开关、零打包污染；与被否决的"单模块 + Mode"（运行时标志 + 能力混装 + 权限共键）有本质区别，不是复辟。
4. **页面清单按域隔离（真正不同）**：运营面排除通道 CRUD/凭据/能力开关（通道状态用运营面自带 status 端点承载，不碰 `/api/wechat-channels/*`，避免给商户授 `WeChatChannels.Query`）；治理面排除全部运营页。详见两份开发方案。
5. **通道上下文放 UI 本地轻量状态**：selected channel id 按三段路径所有权拼进调用，不建扩展级 scope 契约服务——绑定语义唯一权威在后端 B 守卫，UI 侧再造服务即双源真相（对齐"表现层不镜像领域配置"红线）。
6. **UI 选择 ≠ 后端白名单集合**（最关键解耦）：UI 选择 = 消费方唯一决策；后端白名单集合由域依赖拓扑决定——运营域硬依赖通道基座，漏声明 `WeChatChannelsExtensionInitializer<>` → keyed provider 缺失 → `MessageGatewayService` 首解析运行时抛（`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md:123` O4）。商户端即使只接运营 UI，后端仍须双白名单。
7. **页面契约 Phase 1 冻结**：路由前缀/权限键/REST 消费面在 R2 阶段即定稿，`IPageContributor` 落地后迁移 = 纯接线（零破坏），遵守 R2 增长上限（落地后新页面必须声明式贡献）。

---

## 影响面

| 面 | 影响 | 处置 |
|---|---|---|
| 文档体系 | 混装方案 `微信公众号运营GUI-拆分方案.md` 被取代 | 顶部标注 superseded 指向两份新方案，零内容删除（历史可追溯） |
| 新增包 | 2 个 UI 扩展（`.UI.BlazorAntDesign` 后缀，零持久化/零配置） | 各自独立版本线（MinVerTagPrefix `{扩展名}.UI.BlazorAntDesign/v`）；先走 R2 消费方托管，IPageContributor 落地后收敛回 RCL |
| 消费方装配 | 商户端/平台端各择一 UI 包；后端白名单按域依赖 | 使用指南补装配矩阵；商户端双白名单红色警示保留 |
| 后端扩展 | 契约加法（2026-11-27 已补 3 端点 + 在用守卫，待发布 bump） | `GetByChannelIdAsync`/`GetAsync`/`DeleteChannelAsync` 补 REST（`ExposeGraphQL=false`）；`IChannelUsageGuard` 契约 + `DeleteChannelAsync` 在用门禁（`CHANNEL_IN_USE`）+ `WeChatOperationsChannelUsageGuard` 实现（配置引用拒删） |
| 权限面 | 两域权限键隔离到两 UI 包 | `WeChatChannels.Query/Manage` vs `WeChatOperations.Query/Manage`，不再混装 |

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-27 | 初稿——oracle5 架构设计裁定 D1–D7 落地：确立能力域为 UI 边界轴、通道数为运营面内变体、两 UI 扩展镜像命名、UI 选择≠后端白名单的关键解耦；取代混装 GUI 拆分方案；配套两份开发方案 |
| 2026-11-27 | **评审闭环（Oracle PASS WITH CONDITIONS / code-reviewer APPROVE WITH CONDITIONS）**：决策 2 措辞澄清（"沿用规划名"非既有包，C-6）；影响面更新为三端点已补暴露（CR-1 守卫后续已实现）；配套两份方案同步 C-1/C-2/C-3/C-4/C-5 修订 |
| 2026-11-27 | **在用守卫落地（CR-1 P1 闭环）**：`IChannelUsageGuard`（Abstractions SPI）+ `DeleteChannelAsync` 枚举门禁（`CHANNEL_IN_USE`）+ `WeChatOperationsChannelUsageGuard`（配置引用拒删；日志类放行）+ 双测试（门禁/端到端）全绿 |
