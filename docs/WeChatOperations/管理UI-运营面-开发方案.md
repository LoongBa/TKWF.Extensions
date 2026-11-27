# 管理UI-运营面-开发方案（TKWF.Ext.WeChatOperations.UI）

> **版本**：v0.1 ｜ **日期**：2026-11-27 ｜ **状态**：提议（待 Oracle 方案评审；架构裁定 oracle5 D1–D7 已给出）
> **类型**：UI 扩展（零持久化 / 零配置 / R2→IPageContributor 演进）｜ **边界裁定**：`docs/WeChatOperations/ADR/ADR-WeChatOperations-管理UI单双通道扩展边界.md`
> **取代**：混装方案 `docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（标注 superseded，租户页集由本篇承接；平台页集由 `docs/WeChatChannels/管理UI-通道治理面-开发方案.md` 承接）
> **关联**：常态分工 `docs/扩展模块通用指南.md` §四之三；路径所有权 `docs/端点契约化与路径所有权-开发方案.md` §D1；后端能力 `docs/WeChatOperations/微信公众号运营扩展-使用指南.md`；通道底座双白名单 `docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`
> **分工**：装配**模式**设计归框架组/扩展组（本篇）；UI **实现**归消费方 DMP-Lite Merchant.AdminUI；IPageContributor 落地并稳定后拆回本仓库为 `TKWF.Ext.WeChatOperations.UI`（独立版本线）

---

## 一、目的与目标

为**单通道商户运营面**提供一套独立、可按需装配的管理 UI 扩展 `TKWF.Ext.WeChatOperations.UI`。目标状态：

1. 消费方（DMP-Lite Merchant.AdminUI）只接本 UI 包即得全套运营页面（素材/回复规则/菜单/统计/日志/网关/状态/运营配置），**不含**通道 CRUD/凭据页（那是平台治理面 `WeChatChannels.UI` 的职责）。
2. UI 只消费既有的 `/api/wechat-ops/*` REST 端点，**不发明新 API 面**、不直连 DB/ORM/微信、不引平台库 DLL。
3. "单通道/多通道"作为本 UI 内的**形态变体**处理（N=1 无选择器；N>1 错误驱动选择器），不新增模式开关、不复制后端 B 守卫逻辑。
4. 页面契约（路由/权限键/REST 消费面）在 R2 阶段冻结，IPageContributor 落地后零破坏收敛为声明式贡献。

## 二、边界与范围

**In scope**：运营域 8 个门面所支撑的全部管理页面；UI-local 通道上下文（selected channel id）；R2 消费方托管形态 + IPageContributor 落地后的声明式形态。

**Out of scope**：
- **通道治理页**（通道列表/新建/编辑/凭据/能力状态/删除）——归 `WeChatChannels.UI`，本面只以只读 status 卡展示当前通道状态，**不碰** `/api/wechat-channels/*`（避免为看状态给商户授 `WeChatChannels.Query`）。
- 任何后端门面/端点/DTO 的新增或修改——缺能力走转达环（反馈扩展组补契约原语），UI 侧禁止发明平行 API 面（P2 单一事实源）。
- UI 技术栈/布局/UX——消费方自治（分工裁定）。

**边界判据全文**见配套 ADR §决策。

## 三、页面清单与端点映射

模块前缀 `/api/wechat-ops`（=`basePath /api` + `[assembly: ExtensionModule("WeChatOperations","wechat-ops")]`）；端点为 `[ApiExpose]` 编译期契约，UI 经 REST 消费。端点清单实证于各 Service 源码 `[ApiExpose(RestRoute)]`（共 35 端点，含运营 16 + 网关 9 + 运营配置 10）。

| 页面 | 关切 | 消费端点（REST，`/api/wechat-ops` 下） | 权限键 |
|------|------|--------------------------------------|--------|
| 素材库 | 素材 CRUD + 引用反查 | `GET/POST /materials`、`DELETE /materials/{id}`、`GET /materials/{id}/references` | `WeChatOperations.Manage` / `.Query` |
| 回复规则 | 全消息类型规则编辑（SceneType 14 分支 + 排序 + 启用） | `GET/POST /channels/{cid}/rules` | `.Manage` / `.Query` |
| 菜单管理 | 菜单树可视化编辑（raw JSON）+ 发布链 + click key | `GET/POST /channels/{cid}/menu`、`GET /channels/{cid}/menu/versions`、`POST /channels/{cid}/menu/rollback`、`GET /channels/{cid}/menu/click-keys` | `.Manage` / `.Query` |
| 订阅统计 | 按日趋势 + 汇总卡（新增/取消/净增/累计） | `GET /channels/{cid}/subscription-stats`、`GET .../summary` | `.Query` |
| 交互日志 | 日志检索 + 匹配过程明细（MatchTraceJson）+ 发布审计 | `GET /channels/{cid}/logs/interactions`、`GET .../logs/publishes` | `.Query` |
| 网关管理 | 路由规则编辑 / 转发端点管理 / 熔断状态 / 运行时开关 / 转发日志 | `GET/POST/DELETE /gateway/rules`、`GET/POST/DELETE /gateway/endpoints`、`GET /gateway/status`、`POST /gateway/enabled`、`GET /gateway/forward-logs` | `.Manage` / `.Query` |
| 状态卡 | 当前连接 / 今日消息量 / 自动回复命中率 / 转发成功率 | `GET /channels/{cid}/status` | `.Query` |
| 运营配置 | 5 域单行配置（图文/回复文案/降级/弱身份/票据）+ 乐观并发 | `GET/PUT /ops-config/{article-reply,reply-text,degrade,weak-identity,ticket}` | `.Manage` / `.Query` |

> 页面清单以既有门面契约为准（不新增图面 API——缺能力走转达环）。`{cid}` = channelId（见 §四 通道上下文）。

## 四、单/多通道形态变体（UI 语义）

后端无单/多通道开关：单/多是运行时数据形态，由 B 守卫裁决（`active.Count>1` 且无通道段 → `CHANNEL_REQUIRED`；`==1` → 降级绑定，`FederationWebExtension.cs:599-618`）。本 UI 的处置：

1. **N==1（商户典型）**：UI **不实现绑定逻辑**——请求不带 channelId 段（或按端点契约带唯一 `{cid}`），由后端 B 守卫自动绑定；**无通道选择器、无通道 CRUD**。
2. **N>1**：UI 捕获后端 `CHANNEL_REQUIRED`（或选择器预检）→ 弹出一次性通道选择器 → 用户选定后将 selected channel id 注入后续请求路径/参数。
3. **通道上下文 = UI 本地轻量状态**（selected channel id），按三段路径所有权拼进调用；**不建扩展级 scope 契约服务**——绑定语义唯一权威在后端 B 守卫，UI 侧再造 service 即双源真相（对齐"表现层不镜像领域配置"红线）。
4. **禁止 UI 自行"数活跃通道再决策"**：唯一合法的通道数感知 = 错误驱动（后端 `CHANNEL_REQUIRED`）。

## 五、消费方装配规格

### 5.1 单通道商户端（只接运营面）

```text
NuGet:  TKWF.Ext.WeChatOperations.UI  +  TKWF.Ext.WeChatOperations  +  TKWF.Ext.WeChatChannels
```
```csharp
// 领域初始化器——后端白名单【双声明】（UI 选择 ≠ 后端白名单集合）
[TKWFEnabledExtension(typeof(WeChatChannelsExtensionInitializer<>))]      // 不可省：漏声明→keyed provider 缺失→运行时抛
[TKWFEnabledExtension(typeof(WeChatOperationsExtensionInitializer<>))]
public class MerchantDomainInitializer : DomainHostInitializerBase<MerchantUserInfo> { ... }

// Web 层
.UseWebExtensions<MerchantUserInfo>(e => e.Add<PageAssemblyWebExtension>());  // IPageContributor 落地后
```
> ⚠️ **红色警示**：商户端即使只接运营 UI，后端仍须双白名单——运营域硬依赖通道基座（`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md:123` O4）。

### 5.2 平台"代运营"组合（同时接两面）

在平台端既有 `WeChatChannels.UI` 装配基础上，追加本 UI 包 + 后端 `WeChatOperations` 白名单；运营页出现通道选择器（§四），属能力轴的自然组合，**不是第三个扩展**。

### 5.3 路由 / 权限 / 配置

| 项 | 值 |
|----|----|
| 页面路由前缀（预留） | `/admin/wechat-ops/*`（对齐后端 modulePrefix；PAGE003 路由冲突门控） |
| 端点前缀 | `/api/wechat-ops/*`（三段拼接 §D1：`RestRoute`=契约编译期常量；`modulePrefix`/`basePath`=消费方部署旋钮 `TKWF:Api:Modules:{Key}:Prefix`/`TKWF:Api:RestBasePath`。UI **只读消费**拼接后的路由、不自带 RoutePrefix 开关） |
| 权限键 | `WeChatOperations.Query` / `WeChatOperations.Manage`（所有端点非匿名，401 fail-closed） |
| 配置节 | **无新增**（UI 包零配置；领域 `TKWF:WeChatOperations` 由后端 Options 绑定，UI 经既有 Options 透出，不新开节） |

## 六、演进路径（R2 → IPageContributor）

| 阶段 | 形态 | 页面资产归属 | 消费 |
|------|------|-------------|------|
| Phase 1（当前） | **R2**：消费方自研前端直连 REST | 消费方 DMP-Lite Merchant.AdminUI | 本方案 §三 端点 + §五 装配 |
| Phase 2（机制落地后） | **声明式贡献**：`IPageContributor` + `PageDescriptor` + spec 词汇 | 拆回本仓库 `TKWF.Ext.WeChatOperations.UI`（RCL/StaticWebAssets） | `Tkwf.Page.load(Pages.X)`，业务层零修改 |

**约束**：Phase 1 冻结页面契约（路由/权限键/REST 消费面），Phase 2 迁移 = 纯接线、零破坏。IPageContributor 落地后**新页面必须声明式贡献**（R2 增长上限，`docs/扩展模块通用指南.md:190`），R2 不得用于新页面。

## 七、验收标准

| ID | 条件 | 验证方式 |
|----|------|---------|
| U1 | 只消费 `/api/wechat-ops/*`，零命中 `/api/wechat-channels/*`、零 DB/ORM/平台库 DLL 引用 | grep 静态检查 + 代码审查 |
| U2 | 通道上下文仅 UI 本地状态，无扩展级 scope 服务、无 UI 侧通道计数决策 | 代码审查 |
| U3 | 商户端后端双白名单到位，漏声明有红色警示 | 装配冒烟（漏 channels → 运行时抛为预期） |
| U4 | N==1 无选择器 / N>1 错误驱动选择器 | 消费方集成测试（双形态各一） |
| U5 | 页面路由无冲突（PAGE003）、资源键无冲突（PAGE004） | SG1b 门控 0 Error |
| U6 | 页面契约（路由/权限键/端点面）与后端 35 端点清单对账一致 | 端点清单 diff = 空 |
| U7 | 权限键两域隔离（本面仅 `WeChatOperations.*`） | 权限审计 |
| U8 | slnx 构建 0 错误 + 受影响面定向测试全绿 | dotnet build/test |

## 八、风险与转达

| 风险 | 影响 | 对策 |
|------|------|------|
| IPageContributor 未落地 | Phase 1 只能 R2，UI 包无法自挂页 | 页面契约先冻结；落地后纯接线迁移；不阻塞 |
| UI 复制后端 B 守卫 | 双源真相、语义漂移 | §四 错误驱动，后端唯一权威；验收 U2 锁 |
| 双白名单陷阱 | 商户端漏 channels → 运行时抛 | §5.1 红色警示 + 装配冒烟 U3 |
| 后端 35 端点变更 | 页面契约漂移 | 转达环 + U6 对账；缺能力反馈扩展组 |
| 误把通道治理页塞进本面 | 权限域混装（复辟被否决的 Mode 方案） | §二 Out of scope + ADR 决策 4 |

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-27 | 初稿——承接 oracle5 D1–D7 裁定：能力域为轴、运营面单/多通道形态变体、8 页 × 35 端点清单、双白名单装配、R2→IPageContributor 演进、8 验收项 |
| 2026-11-27 | **Oracle 正式评审 PASS WITH CONDITIONS 闭环**：C-2 §5.3 端点前缀 D1 引用纠偏（三段模型：RestRoute=常量、modulePrefix/basePath=部署旋钮）；C-3 §三 菜单路由补全 `/channels/{cid}/menu/` 父段 |
