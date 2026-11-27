# 转达-DMP-Lite · 微信管理 UI 双面实施任务书（实现 → 拆回扩展）

> **来源**：TKWF 扩展模块组 ｜ **日期**：2026-11-27 ｜ **性质**：开发任务书 + 拆分回仓契约
> **命名定案**：`TKWF.Ext.WeChatOperations.UI.BlazorAntDesign` / `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`（`.UI`=表现层 + `.BlazorAntDesign`=Blazor RCL + AntDesign）
> **边界裁定**：`docs/WeChatOperations/ADR/ADR-WeChatOperations-管理UI单双通道扩展边界.md`（能力域为轴，非通道数）
> **配套方案**：`docs/WeChatOperations/管理UI-运营面-开发方案.md`（运营面）+ `docs/WeChatChannels/管理UI-通道治理面-开发方案.md`（通道治理面）
> **取代**：`转达-微信公众号运营GUI-拆分方案-消费方适配通知.md`（已 superseded）
> **分工（2026-11-06 用户裁定，常态规则）**：装配**模式**设计归框架组/扩展组（本篇 + 两份方案）；UI **实现**归 DMP-Lite；DMP 实证稳定后**拆回本仓库**为两个 `.UI.BlazorAntDesign` 扩展包。

---

## 一、目的与背景

微信/联邦管理端 GUI 按**能力域**拆为两面（非按"通道数"）：

| 面 | 后端能力 | 消费方 | 最终扩展包 |
|----|---------|--------|-----------|
| **运营面**（单通道语境） | `WeChatOperations`（素材/回复/菜单/统计/日志/网关/运营配置） | DMP `Merchant.AdminUI`（Blazor WASM + AntDesign） | `TKWF.Ext.WeChatOperations.UI.BlazorAntDesign` |
| **通道治理面**（多通道语境） | `WeChatChannels`（通道配置 CRUD/凭证/能力状态） | DMP `Platform.Web`（Blazor Server） | `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign` |

**为什么先 DMP 后拆回**：UI 技术栈/布局/UX 归消费方自治（分工裁定）；DMP 先用真实页面磨合装配与契约，稳定后按「回收标准」（§七）迁回 RCL 扩展，沉淀通用参考实现。**命名即终态**——DMP 实现时即按最终包名组织页面（RCL 友好），拆回成本仅为打包。

> **单/多通道不是开关**：后端无模式开关，是"活跃通道数"的运行时形态（`active.Count>1` 且无通道段 → `CHANNEL_REQUIRED`；`==1` → 自动绑定）。UI 侧以"错误驱动的通道选择器"处理，**不在 UI 复制后端 B 守卫逻辑**。

---

## 二、页面清单与端点/权限（实现范围）

### 2.1 运营面 —— `WeChatOperations.UI.BlazorAntDesign`（Merchant.AdminUI）

消费端点前缀 `/api/wechat-ops/*`（模块前缀 `wechat-ops`）；权限 `WeChatOperations.Query`（读）/ `WeChatOperations.Manage`（写）。共 35 端点。

| 页面 | 关切 | 消费端点（`/api/wechat-ops` 下） |
|------|------|--------------------------------|
| 素材库 | 素材 CRUD + 引用反查 | `GET/POST /materials`、`DELETE /materials/{id}`、`GET /materials/{id}/references` |
| 回复规则 | 全消息类型规则编辑（14 分支 + 排序 + 启用） | `GET/POST /channels/{cid}/rules` |
| 菜单管理 | 菜单树可视化编辑（raw JSON）+ 发布链 + click key | `GET/POST /channels/{cid}/menu`、`GET /channels/{cid}/menu/versions`、`POST /channels/{cid}/menu/rollback`、`GET /channels/{cid}/menu/click-keys` |
| 订阅统计 | 按日趋势 + 汇总卡 | `GET /channels/{cid}/subscription-stats`、`GET .../summary` |
| 交互日志 | 检索 + 匹配过程明细 + 发布审计 | `GET /channels/{cid}/logs/interactions`、`GET .../logs/publishes` |
| 网关管理 | 路由规则/转发端点/熔断状态/运行时开关/转发日志 | `GET/POST/DELETE /gateway/rules`、`GET/POST/DELETE /gateway/endpoints`、`GET /gateway/status`、`POST /gateway/enabled`、`GET /gateway/forward-logs` |
| 状态卡 | 连接/今日消息量/自动回复命中率/转发成功率 | `GET /channels/{cid}/status` |
| 运营配置 | 5 域单行配置（图文/回复文案/降级/弱身份/票据） | `GET/PUT /ops-config/{article-reply,reply-text,degrade,weak-identity,ticket}` |

> **排除**：通道 CRUD/凭据/能力开关（属通道治理面，见 2.2）；运营面不碰 `/api/wechat-channels/*`（避免为看状态给商户授 `WeChatChannels.Query`）。

### 2.2 通道治理面 —— `WeChatChannels.UI.BlazorAntDesign`（Platform.Web）

消费端点前缀 `/api/wechat-channels/*`（模块前缀 `wechat-channels`）；权限 `WeChatChannels.Query`（读）/ `WeChatChannels.Manage`（写）。共 5 端点。

| 页面 | 关切 | 消费端点（`/api/wechat-channels` 下） |
|------|------|-------------------------------------|
| 通道列表 | 全量通道 + 能力状态列（AppId/Alias/形态/认证状态/凭据已配置/启停） | `GET /channels` |
| 通道新建/编辑 | 元数据 + 凭证（AppSecret/Token/EncodingAESKey 明文 body）+ 启停 | `POST /channels` |
| 通道详情 | 单通道详情 | `GET /channels/{channelId}` |
| 按 AppId 单查（可选） | 运维核对 | `GET /channels/by-app/{appId}` |
| 通道删除 | 物理删（**受在用守卫，见 §五**） | `DELETE /channels/{id}` |

> ⚠️ **端点版本依赖**：后三者（`GET /channels/{channelId}`、`GET /channels/by-app/{appId}`、`DELETE /channels/{id}`）为 **2026-11-27 新增、待发布**——消费方须锁 `WeChatChannels.Abstractions` ≥ 该 bump 版本（发布前用部署根 `local-feed`/refs 或等 NuGet 上架）。

---

## 三、装配契约

### 3.1 后端白名单（**UI 选择 ≠ 后端白名单集合**）

| 消费端 | 后端白名单 | 说明 |
|--------|-----------|------|
| Merchant.AdminUI（运营面 UI） | **双声明** `WeChatChannelsExtensionInitializer<>` + `WeChatOperationsExtensionInitializer<>` | 运营域硬依赖通道基座——漏声明 `WeChatChannels` → keyed provider/门面缺失 → 运行时抛 |
| Platform.Web（通道治理面 UI） | 单声明 `WeChatChannelsExtensionInitializer<>`（+ Federation 已有） | 平台模式，无运营依赖 |

> ⚠️ 只接某一个 UI 包，**不等于**只白名单对应后端扩展——白名单集合由域依赖拓扑决定。

### 3.2 Web / 路由 / 配置

| 项 | 运营面 | 通道治理面 |
|----|--------|-----------|
| 页面装配（Phase 2） | `UseWebExtensions<TUserInfo>(e => e.Add<PageAssemblyWebExtension>())` | 同 |
| 页面路由前缀（预留） | `/admin/wechat-ops/*` | `/admin/wechat-channels/*` |
| 端点前缀 | `/api/wechat-ops/*`（三段拼接：`RestRoute` 契约常量 + `modulePrefix`/`basePath` 部署旋钮——UI 只读消费，不自带路由开关） | `/api/wechat-channels/*` |
| 权限键 | `WeChatOperations.Query/Manage` | `WeChatChannels.Query/Manage` |
| 配置节 | 无新增（领域 `TKWF:WeChatOperations` 由后端读取） | 无新增（`TKWF:WeChatChannels`） |

---

## 四、实现约束（违反即返工）

1. **只消费 REST**：页面仅经 `[GenerateController]` 生成的端点（与 `User.Use<T>` 同一路径）——不直连微信、不读 DB/ORM、不引平台库 DLL、不注 `IEntityDAC`。
2. **不发明新 API 面**：缺能力走**转达环**（反馈扩展组补契约原语），禁止在 UI 侧平行造端点（P2 单一事实源）。
3. **技术栈**：运营面 = Blazor WASM + **AntDesign（Pro）**（对齐 `Merchant.AdminUI`）；通道治理面 = Blazor Server 页面（对齐 `Platform.Web`）。两者共用同一套 Razor 组件亦可（RCL 宿主无关），拆回时统一为 RCL。
4. **R2 起步**：`IPageContributor` 接口本体框架侧未落地，Phase 1 用**消费方自研前端直连 REST**；落地后新页面必须声明式贡献（R2 增长上限）。
5. **安全**：凭证即错即脱敏（DTO 投影不含凭证 + 出站日志脱敏）；页面不落明文。

---

## 五、关键行为契约

### 5.1 单/多通道（运营面）

- **N==1**（商户典型）：无通道选择器、无通道 CRUD；请求不带通道段，后端自动绑定。
- **N>1**：UI 捕获后端 `CHANNEL_REQUIRED`（或预检）→ 弹出**一次性通道选择器** → 选定后注入 `selected channel id`。
- **通道上下文 = UI 本地轻量状态**；**禁止 UI 自行"数活跃通道再决策"**（唯一通道数感知 = `CHANNEL_REQUIRED` 错误驱动）。

### 5.2 在用守卫（通道治理面删除）

- `DELETE /channels/{id}` 由后端 `IChannelUsageGuard` 门禁：通道仍被**运营配置**引用（回复规则 / 菜单内容 / 网关路由规则 / 下游端点）时抛 `DomainException`（ErrorCode=`CHANNEL_IN_USE`，消息含来源）。
- **不阻塞**（历史/瞬态）：菜单版本（回滚历史）、交互/发布/转发日志、幂等键——**查看/发布过菜单或产生过日志不阻止删除**；菜单"已清空"（`MenuJson` 空）亦放行。
- **UI 建议**：默认走「停用（`SaveChannelAsync` 携 `IsEnabled=false`）」，物理删仅在确认无引用时提供；捕获 `CHANNEL_IN_USE` 提示"先清除引用或改停用"。
- **已知限制**：守卫为 **best-effort**（检查-删除非原子，无 DB 外键）；跨域数据（Federation 关注/账号映射/通道注册表、TrustCenter 授权码）暂未门禁——删除前自行确认。

---

## 六、拆分回仓标准（DMP 实现稳定后 → 扩展组迁回）

DMP 侧落地并上线后，按下列标准拆回本仓库为两个 `.UI.BlazorAntDesign` RCL 扩展：

1. **契约稳定**：页面契约（路由前缀 / 权限键 / REST 消费面）与本文一致，最近 K 次迭代无破坏性变更。
2. **形态对齐**：页面资产迁为 **RCL**（`Microsoft.NET.Sdk.Razor`，零持久化/零配置），可被 `PageAssemblyWebExtension` 装配；宿主（WASM/Server）留在消费方。
3. **装配零破坏**：拆回后消费方仅改"引用包 + 白名单"（页面路由/权限键不变）。
4. **可迁移验证**：DMP 前端资产（AntDesign 组件/页面）到 RCL 的转换路径跑通。
5. **转达闭环**：实现期暴露的模式缺口（装配/契约原语）已转达并修复。
6. **权限键定稿**：`WeChatOperations.Query/Manage` / `WeChatChannels.Query/Manage` 回归前冻结。

> 拆回动作 = 新建两个扩展（独立方案 + Oracle 评审 + code-reviewer 审核，循 `Agents_TKWF_Ext.md` §3/§4），独立版本线（`MinVerTagPrefix {扩展名}.UI.BlazorAntDesign/v`）。

---

## 七、验收清单（DMP 侧）

| ID | 条件 |
|----|------|
| D1 | 运营面 8 页 + 通道治理面 4 页按 §二 端点消费；零直连 DB/ORM/平台库 DLL |
| D2 | Merchant.AdminUI 后端**双白名单**、Platform.Web **单白名单**；启动无守卫异常 |
| D3 | 运营面 N==1 无选择器 / N>1 错误驱动选择器，无 UI 侧通道计数逻辑 |
| D4 | 通道删除正确处理 `CHANNEL_IN_USE`（默认停用路径可用；查看/发布过菜单、有日志的通道可删） |
| D5 | 通道详情/删除页依赖 `WeChatChannels.Abstractions` 待发布版本（锁版本） |
| D6 | 三端点 404 排除（消费方白名单声明 + 锁版本到位） |
| D7 | 凭证页面不落明文、DTO 不含凭证；权限键 `WeChat*.Query/Manage` 生效 |

---

## 八、变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-27 | 初稿：双面实施任务书——命名定案 `{扩展}.UI.BlazorAntDesign`；运营面/通道治理面页面·端点·权限；装配契约（双/单白名单）；单多通道选择器 + 在用守卫（`CHANNEL_IN_USE`，菜单版本/日志放行）；拆分回仓 6 标准 + 7 验收 |
