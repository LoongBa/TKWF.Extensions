# 转达-微信公众号运营GUI-拆分方案-消费方适配通知

> **来源**: TKWF 扩展组
> **日期**: 2026-11-06
> **关联**: `docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（Oracle2 PASS WITH CONDITIONS C1-C6 闭环）；`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`（2026-11-05 已发布）；`docs/扩展模块通用指南.md` §四之三（UI 装配分工模式）
> **性质**: **开发任务书 + 消费方适配通知**——微信域 GUI 装配实现归 DMP-Lite（Merchant.AdminUI / Platform.Web），本篇交付边界契约、页面清单、分工规则与验收条件
> **分工裁定（2026-11-06 用户）**: 扩展模块的**业务装配 / UI 装配的设计方案与模式由框架组/扩展模块组设计**（不限于联邦/微信，常态规则）——**实现/拆分在 DMP-Lite 项目的 Merchant.AdminUI / Platform.Web 完成**，验证后回归扩展项目

---

## 一、背景

2026-10-18 公众号运营方案（§八）承诺：GUI 阶段二可视化编辑器——扩展组**出拆分方案交 DMP-Lite 实现**，磨合界面装配机制后**拆回 `WeChatOperations.UI` 扩展** + 沉淀 skill。该承诺文档现已产出并通过 Oracle2 评审（**PASS WITH CONDITIONS，C1-C6 全部闭环**）：`docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`。

**本篇是实施任务书**——DMP-Lite 可按本通知开工 GUI，无需等待其他前置（唯一依赖：`WeChatChannels` v0.1.1 / `WeChatOperations` v0.9.1 已发布，双白名单形态已就绪）。

---

## 二、分工规则速览（2026-11-06 裁定，常态规则）

| 层 | 归属 | 内容 |
|----|------|------|
| **装配模式设计** | 框架组 / 扩展模块组 | 页面贡献契约（`IPageContributor` 未落地前的 R2 路径）、装配形态（双白名单 + `PageAssemblyWebExtension`）、路由/权限/配置节约定、页面清单 |
| **装配实现** | **DMP-Lite（本篇责任主体）** | Merchant.AdminUI（租户运营面）/ Platform.Web（平台通道面）页面 UI（技术栈/布局/UX 自由） |
| **回归** | 扩展模块组 | 按回收验收标准（§六）拆回 `.UI` 扩展 + 沉淀 skill |

---

## 三、页面清单（DMP-Lite 实施范围）

### 3.1 平台侧（Platform.Web，装配 WeChatChannels——平台模式）

| 页面 | 关切 | 消费端点（REST） | 权限键 |
|------|------|-----------------|--------|
| 通道管理（列表/详情/编辑） | 公众号通道 CRUD + 凭证管理 + 能力状态 | `IChannelConfigService` `/api/wechat-channels/channels` | `WeChatChannels.Query/Manage` |

### 3.2 租户侧（Merchant.AdminUI，双白名单——运营模式）

| 页面 | 关切 | 消费端点（REST） | 权限键 |
|------|------|-----------------|--------|
| 素材库 | 素材 CRUD + 引用反查（跨 ReplyRule 聚合） | `IMaterialService` `/api/wechat-ops/*` | `WeChatOperations.Manage` |
| 回复规则 | 全消息类型规则编辑器（14 分支映射 + 排序 + 启用） | `IReplyRuleService` | 同上 |
| 菜单管理 | 菜单树可视化编辑（raw JSON）+ 发布链（快照/版本/审计）+ 当前菜单反查 | `IMenuService` | 同上 |
| 订阅统计 | 按日趋势/汇总卡（新增/取消/净增/累计） | `ISubscriptionStatService` / `IWeChatOperationsStatusService` | 同上 |
| 交互日志 | 日志检索 + 匹配过程明细（MatchTraceJson）+ 转发日志 | `ILogQueryService` / 转发对账面 | `WeChatOperations.Query` |
| **网关管理** | **路由规则编辑器（CRUD+排序+启用）/ 转发端点管理 / 熔断状态 / 运行时开关** | `IMessageGatewayService` `/gateway/*`（rules×3 + endpoints×3 + status + enabled + forward-logs——Oracle2 C1 补齐） | `WeChatOperations.Manage` |
| 状态卡 | 网关状态/规则命中/端点健康聚合 | `IWeChatOperationsStatusService` | `WeChatOperations.Query` |

---

## 四、边界契约（实施必须遵守，违反即返工）

1. **只消费 REST**：GUI 仅经 `[GenerateController]` 端点（`User.Use<T>` 同一路径，DI004）——**不直连微信、不读 DB、不引平台库 DLL、不注 ORM**
2. **自备前端资产**：SPA 静态资源（StaticWebAssets/RCL——ADR106 决策 5 许可路径）
3. **装配一行**：`UseWebExtensions(e => e.Add<PageAssemblyWebExtension>())` + 页面静态路由；Domain 白名单双声明（WeChatChannels + WeChatOperations——**漏声明 WeChatChannels → keyed provider 缺失 → `MessageGatewayService` 首解析运行时 throw**，WeChatChannels 拆分方案 O4 警示）
4. **R2 路径**：`IPageContributor` 接口本体未落地（含 TS codegen / reference-components）→ GUI 用**消费方自研前端直连 REST** 起步；落地后新页面必须声明式贡献（**R2 增长上限**，防漂移为事实标准——Oracle2 C4/C5）
5. **菜单 poco 共享**：GUI 不引平台库 DLL——菜单结构 raw JSON（编辑器自由形态，不类型化）；如需类型化拆 Abstractions 契约包（待触发）
6. **安全红线**：凭证即错即脱敏（DTO 投影不含凭证 + 出站日志脱敏）；页面不落明文
7. **配置节**：平台侧 `TKWF:WeChatChannels`；租户侧 `TKWF:WeChatOperations`——GUI 经既有 Options 透出，**不新开配置节**
8. **❌ GUI 禁止发明新 API 面**：缺能力走**转达环**（反馈扩展组 → 扩展组补契约原语 → 下方新版本）——并行面会破坏门面契约单一事实源（P2）

---

## 五、路由 / 权限 / 白名单速查

| 项 | 平台侧（Platform.Web） | 租户侧（Merchant.AdminUI） |
|----|------------------------|---------------------------|
| 扩展装配 | `WeChatChannelsExtensionInitializer<>`（+ Federation 已有） | **双声明**：WeChatChannels + WeChatOperations |
| 端点前缀 | `/api/wechat-channels/*` | `/api/wechat-ops/*` |
| 权限键 | `WeChatChannels.Query/Manage` | `WeChatOperations.Query/Manage`（沿用） |
| 页面装配 | Platform.Web 页面 | Merchant.AdminUI 页面（`/m/{mid}/` 租户路径下） |

---

## 六、回收验收（DMP 实现后扩展组拆回 `WeChatOperations.UI` 的条件）

1. **IPageContributor 落地**（框架侧页面贡献机制可用）
2. **GUI 稳定**：(a) §三 全量页面（含网关管理页）已实现并上线生产；(b) 无 P0/P1 UI 缺陷 open；(c) **最近 2 次迭代转达环静默**（无契约缺口返工）
3. **资产可迁移**：前端资产与扩展包 StaticWebAssets 形态对齐（转换路径验证）
4. **转达闭环**：实现中暴露的模式缺口（装配机制/契约原语）已转达并按需修复
5. **契约稳定性**：最近 K 次迭代门面契约无破坏性变更
6. **权限键定稿**：`WeChatChannels.Query/Manage` / `WeChatOperations.Query/Manage` 回归前定稿

> 拆回动作本身 = 新建 `WeChatOperations.UI` 扩展（独立方案 + Oracle 评审 + code-reviewer 审核，循 AGENTS §3）——**此后续动作由扩展组执行，DMP 无需等待**。

---

## 七、DMP-Lite 动作清单（本期）

| # | 动作 | 归属 | 前置 |
|:-:|------|------|------|
| 1 | Platform.Web 装配 WeChatChannels + 通道管理页（§三 3.1） | Platform.Web | 引用 `TKWF.Ext.WeChatChannels` v0.1.1+ |
| 2 | Merchant.AdminUI 双白名单 + 7 页实现（§三 3.2） | Merchant.AdminUI | 引用 WeChatChannels + WeChatOperations v0.9.1+（+ WeChatChannels.Abstractions 显式） |
| 3 | 遵守边界契约 8 条（§四）——不发明 API 面 | 两侧 | — |
| 4 | R2 自研前端直连 REST；IPageContributor 落地后新页面声明式 | 两侧 | 框架侧机制 |
| 5 | **转达环**：契约缺口/装配机制缺口 → 反馈扩展组（不再自行发明） | 两侧 | — |
| 6 | 网关管理页（含 9 `/gateway/*` 端点对接）——**V0.4.0 设计缺口裁定任务的完成面** | Merchant.AdminUI | — |

---

## 八、参考

- GUI 拆分方案全文：`docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（边界契约/页面清单/回收验收/skill 计划——实施以该文为准）
- UI 装配分工模式（常态规则）：`docs/扩展模块通用指南.md` §四之三
- WeChatChannels 拆分（2026-11-05 已发布）：`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`

---

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-06 | 初稿：GUI 拆分方案（Oracle2 C1-C6 闭环）转达 DMP-Lite——分工规则/页面清单（含网关管理页 C1）/边界契约 8 条/回收验收 6 条件/动作清单 6 项 |