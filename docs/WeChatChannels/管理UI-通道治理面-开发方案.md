# 管理UI-通道治理面-开发方案（TKWF.Ext.WeChatChannels.UI.BlazorAntDesign）

> **版本**：v0.1 ｜ **日期**：2026-11-27 ｜ **状态**：已评审通过（Oracle PASS WITH CONDITIONS + code-reviewer APPROVE WITH CONDITIONS，C-1/C-2/CR-1~CR-4 已闭环）
> **类型**：UI 扩展（零持久化 / 零配置 / R2→IPageContributor 演进）｜ **边界裁定**：`docs/WeChatOperations/ADR/ADR-WeChatOperations-管理UI单双通道扩展边界.md`（归 WeChatOperations 名下，本篇交叉引用）
> **取代**：混装方案 `docs/WeChatOperations/微信公众号运营GUI-拆分方案.md`（标注 superseded，平台通道页集由本篇承接；租户运营页集由 `docs/WeChatOperations/管理UI-运营面-开发方案.md` 承接）
> **关联**：常态分工 `docs/扩展模块通用指南.md` §四之三；路径所有权 `docs/端点契约化与路径所有权-开发方案.md` §D1；后端能力 `docs/WeChatChannels/微信通道底座-使用指南.md`；通道底座拆分 `docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`
> **分工**：装配**模式**设计归框架组/扩展组（本篇）；UI **实现**归消费方 DMP-Lite Platform.Web；IPageContributor 落地并稳定后拆回本仓库为 `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`（独立版本线）

---

## 一、目的与目标

为**多通道平台治理面**提供一套独立、可按需装配的管理 UI 扩展 `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`。目标状态：

1. 消费方（DMP-Lite Platform.Web）只接本 UI 包即得通道治理页面（通道列表/详情/新建/编辑/凭证/能力状态/启停）。**不含**任何运营页（素材/回复/菜单/统计/日志/网关）——那是运营面 `WeChatOperations.UI.BlazorAntDesign` 的职责。
2. UI 只消费既有 `/api/wechat-channels/*` REST 端点，不发明新 API 面、不直连 DB/ORM/微信、不引运营扩展 DLL。
3. 通道列表天然按多实例呈现（`ListChannelsAsync` 返回全量），通道上下文 = 平台显式选择（与运营面的"后端自动绑定"语义相反，见 §四）。
4. 页面契约在 R2 阶段冻结，IPageContributor 落地后零破坏收敛。

## 二、边界与范围

**In scope**：通道配置门面所支撑的治理页面；平台侧通道选择上下文；R2 消费方托管形态 + IPageContributor 声明式形态。

**Out of scope**：
- **全部运营页**（素材/回复规则/菜单/统计/日志/网关/运营配置）——归 `WeChatOperations.UI.BlazorAntDesign`。
- 后端门面/端点/DTO 新增或修改——缺能力走转达环；特别是**通道 REST 薄面缺口**（见 §三.2）须转达扩展组评估，不由 UI 侧发明平行端点。
- UI 技术栈/布局/UX——消费方自治。

## 三、页面清单与端点映射

模块前缀 `/api/wechat-channels`（=`basePath /api` + `[assembly: ExtensionModule("WeChatChannels","wechat-channels")]`）。

### 3.1 端点与权限

`IChannelConfigService` 契约实证（`_Framework/WeChatChannels.Abstractions/IChannelConfigService.cs:29-60`）：

| 方法 | REST | 权限键 | UI 可用性 |
|------|------|--------|-----------|
| `ListChannelsAsync()` | `GET /api/wechat-channels/channels` | `WeChatChannels.Query` | ✅ 通道列表 |
| `SaveChannelAsync(SaveChannelRequest)` | `POST /api/wechat-channels/channels` | `WeChatChannels.Manage` | ✅ 新建/编辑（凭证明文 body 传输，AES-GCM 落库） |
| `GetByChannelIdAsync(channelId)` | `GET /api/wechat-channels/channels/{channelId}` | `WeChatChannels.Query` | ✅ 详情（2026-11-27 补暴露） |
| `GetAsync(appId)` | `GET /api/wechat-channels/channels/by-app/{appId}` | `WeChatChannels.Query` | ✅ 按 AppId 单查（补暴露） |
| `DeleteChannelAsync(id)` | `DELETE /api/wechat-channels/channels/{id}` | `WeChatChannels.Manage` | ⚠️ 删除（补暴露，**受在用守卫门禁**，见 §3.2） |

> 5 方法全部暴露 REST（后三者 `ExposeGraphQL=false` 只开 REST——ADR 纪律：`ExposeRest=false ≠ 不暴露`，GraphQL 默认开须显式关闭）；`by-app` 段靠**段数**（4 段）与 `/{channelId}`（3 段）隔离，无路由歧义。

### 3.2 端点暴露状态与在用守卫（原"薄面缺口"已闭环）

**2026-11-27 扩展组已补暴露**：`GetByChannelIdAsync`/`GetAsync`/`DeleteChannelAsync` 三方法由 `ExposeRest=false` 改为显式 `[ApiExpose(RestRoute, RestMethod, ExposeGraphQL=false)]`（`IChannelConfigService.cs` + 契约测试 `GovernanceEndpoints_ExposeRest_GraphQLClosed` + Abstractions README "待发布"登记）——**原"UI v1 只有 list+save 两个端点可调"的事实已不成立**。处置更新：

1. **详情页可入 v1**：`GET /channels/{channelId}` 已可用；亦可继续用列表载荷承载（`WeChatChannelConfigDto` 含 12 字段，列表足供编辑回填）——**详情入口为范围选择，非能力限制**。
2. **删除页 = 显式 v1 范围决策 + 在用守卫（✅ 已实现 2026-11-27）**：`DELETE /channels/{id}` 为物理删；运营数据属主经契约 `IChannelUsageGuard`（`WeChatChannels.Abstractions`）实现守卫，`DeleteChannelAsync` 枚举全部守卫，任一报告在用即抛 `DomainException`（ErrorCode=`CHANNEL_IN_USE`）。
   - **实现（已落地）**：`WeChatOperationsChannelUsageGuard : DomainServiceBase, IChannelUsageGuard` 检查运营**配置**引用（回复规则/菜单内容（快照 `MenuJson` 非空）/网关路由规则/下游端点）；菜单版本（回滚历史，无清理入口）与日志类（交互/发布/转发日志、幂等键）为历史/瞬态**不作门禁**（否则下线通道永不可清理）。经 `TryAddEnumerableConstructible<IChannelUsageGuard, …>()` 注册；`ChannelConfigService` 构造期注入 `IEnumerable<IChannelUsageGuard>`（集合版守卫工厂帧内供给，对齐 `SsoChannelFactory`）。
   - **语义**：零守卫实现（平台独用、未装配 WeChatOperations）= 放行；存在配置引用 = 拒绝并提示来源；先清除引用（或走停用）后再删。
   - **兜底**：即便有守卫，治理 UI 仍建议默认走"停用（`IsEnabled=false`）"而非物理删；物理删仅在确认无引用时提供。
3. **版本依赖**：三端点补暴露 + `IChannelUsageGuard` 契约随 `WeChatChannels.Abstractions` 下次发布 bump（加法变更）；**删除/详情页若纳入 v1，消费方须锁该版本及以上**。
4. **不发明平行端点**：缺口一律走转达环；本方案不自行发明端点。

### 3.3 页面清单

| 页面 | 关切 | 消费端点 | 权限键 |
|------|------|---------|--------|
| 通道列表 | 全量通道 + 能力状态列（AppId/Alias/形态/认证状态/凭证已配置/启停） | `GET /api/wechat-channels/channels` | `WeChatChannels.Query` |
| 通道新建/编辑 | 通道元数据 + 凭证（AppSecret/Token/EncodingAESKey 明文 body）+ 启停 | `POST /api/wechat-channels/channels` | `WeChatChannels.Manage` |
| 通道详情 | 单通道详情（可入 v1；亦可由列表载荷承载） | `GET /api/wechat-channels/channels/{channelId}` | `.Query` |
| 通道删除 | 物理删（**在用守卫已实现**——有运营配置引用时拒删`CHANNEL_IN_USE`；UI 默认走停用） | `DELETE /api/wechat-channels/channels/{id}` | `.Manage` |

> 通道列表天然多实例（`ListChannelsAsync` 全量投影，注释"通道数量级个位数~几十，平台手工维护"）——平台侧显式选择通道，与运营面后端自动绑定语义相反（§四）。

## 四、多通道 UI 语义（平台显式选择）

| 面 | 通道上下文来源 | 选择器 | CRUD |
|----|---------------|--------|------|
| 平台治理面（本 UI） | 平台显式选中的通道（列表行 / 编辑目标） | 列表即选择器（点行进入编辑） | 有（新建/编辑/详情/删除——删除受在用守卫，被引用即拒） |
| 运营面（WeChatOperations.UI.BlazorAntDesign） | 后端 B 守卫自动绑定（N==1）；N>1 错误驱动选择器 | N==1 无 / N>1 有 | 无通道 CRUD |

**纪律**：本 UI 不做身份选区（那是后端 `IChannelRegistry` / B 守卫的职责），只做治理对象的显式选择；不复制后端选区逻辑到 UI。平台"代运营"场景 = 本 UI + `WeChatOperations.UI.BlazorAntDesign` 双挂，运营页的选择器由后端 `CHANNEL_REQUIRED` 驱动。

## 五、消费方装配规格

### 5.1 平台管理端（只接治理面）

```text
NuGet:  TKWF.Ext.WeChatChannels.UI.BlazorAntDesign  +  TKWF.Ext.WeChatChannels   // + Federation（平台已有）
```
```csharp
// 领域初始化器——后端白名单【单声明】（平台模式，无运营依赖）
[TKWFEnabledExtension(typeof(WeChatChannelsExtensionInitializer<>))]
public class PlatformDomainInitializer : DomainHostInitializerBase<PlatformUserInfo> { ... }

// Web 层
.UseWebExtensions<PlatformUserInfo>(e => e.Add<PageAssemblyWebExtension>());  // IPageContributor 落地后
```
> 与商户端的关键差异：平台端**单白名单**（WeChatChannels），商户端**双白名单**（+ WeChatOperations）——白名单集合由后端域依赖决定，与 UI 选择解耦（ADR 决策 6）。

### 5.2 路由 / 权限 / 配置

| 项 | 值 |
|----|----|
| 页面路由前缀（预留） | `/admin/wechat-channels/*`（对齐后端 modulePrefix；PAGE003 门控） |
| 端点前缀 | `/api/wechat-channels/*`（三段拼接 §D1：`RestRoute`=契约编译期常量；`modulePrefix`/`basePath`=消费方部署旋钮 `TKWF:Api:Modules:{Key}:Prefix`/`TKWF:Api:RestBasePath`。UI **只读消费**拼接后的路由、不自带 RoutePrefix 开关） |
| 权限键 | `WeChatChannels.Query` / `WeChatChannels.Manage`（非匿名，401 fail-closed） |
| 配置节 | **无新增**（UI 包零配置；凭证密钥路径 `TKWF:WeChatChannels` 由后端读取，UI 不新开节） |

## 六、演进路径（R2 → IPageContributor）

同运营面 §六：Phase 1 R2（消费方 Platform.Web 自研前端直连 REST）；Phase 2 声明式贡献拆回 `TKWF.Ext.WeChatChannels.UI.BlazorAntDesign`（RCL/StaticWebAssets）。页面契约 Phase 1 冻结，迁移纯接线、零破坏；IPageContributor 落地后新页面必须声明式贡献。

## 七、验收标准

| ID | 条件 | 验证方式 |
|----|------|---------|
| C1 | 只消费 `/api/wechat-channels/*`，零运营端点、零 DB/ORM/运营 DLL 引用 | grep + 代码审查 |
| C2 | v1 页面含列表+新建/编辑（含启停）+详情；删除入口存在且被 `IChannelUsageGuard` 门禁（被引用拒删） | 页面清单 diff |
| C3 | 三端点补暴露已落地（源码 + 契约测试）；在用守卫已实现（`WeChatOperationsChannelUsageGuard` + 门禁/端到端测试全绿） | 守卫测试全绿 |
| C4 | 平台单白名单到位；未误加运营白名单 | 装配冒烟 |
| C5 | 通道列表能力状态列完整（Alias/HasCredentials/ChannelForm/Verification 等） | 消费方集成测试 |
| C6 | 页面路由无冲突（PAGE003）、资源键无冲突（PAGE004） | SG1b 门控 0 Error |
| C7 | 权限键仅 `WeChatChannels.*`，两域隔离 | 权限审计 |
| C8 | slnx 构建 0 错误 + 受影响面定向测试全绿 | dotnet build/test |

## 八、风险与转达

| 风险 | 影响 | 对策 |
|------|------|------|
| 在用通道硬删（WeChatOperations 9 张表按 ChannelId 关联 → 悬空） | 数据完整性 / 历史链路断裂 | ✅ 已实现 `IChannelUsageGuard`（配置引用即拒删 `CHANNEL_IN_USE`；日志类放行）；UI 默认停用 |
| **守卫覆盖范围**（当前仅 WeChatOperations 配置表；`Federation` FollowEvent/FollowState/SsoChannelRegistry、`TrustCenter` AuthorizationCode 等按 ChannelId 关联但**未门禁**） | 删通道仍可能悬空这些域行 | **已知限制（已文档化）**；下一迭代：`Federation`/`TrustCenter` 各自实现 `IChannelUsageGuard`（契约已是多实现 SPI，天然支持）；不扩 WeChatOperations 到日志表（避免阻断下线清理） |
| 守卫 best-effort + 版本耦合 | 并发窗口残留 / 旧主包静默失效 | 已知限制文档化；`WeChatChannels`+`WeChatOperations` 同批发布；守卫为软保护非 DB 级不变量 |
| 三端点补暴露待发布 bump | 消费方锁版本 | §3.2.3 版本依赖；发布前按 C-4 跑受影响面测试 |
| 误把运营页塞进本面 | 权限域混装（复辟 Mode 方案） | §二 Out of scope + ADR 决策 4 |
| 单白名单误加运营扩展 | 平台被迫打包运营全链（后端拆分否决理由） | §5.1 单声明 + C4 |
| 双 UI 组合路由冲突 | PAGE003 | 路由前缀隔离 + C6 |
| IPageContributor 未落地 | 只能 R2 | 契约冻结，落地后纯接线 |

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-27 | 初稿——承接 oracle5 D1–D7 裁定：能力域为轴、治理面多通道显式选择、通道 REST 薄面缺口收敛 v1 范围 + 转达、单白名单装配、R2→IPageContributor 演进、8 验收项 |
| 2026-11-27 | **code-reviewer 审核闭环（APPROVE WITH CONDITIONS）**：P1 菜单误杀修复——快照按 `MenuJson` 非空判定、`MenuVersion`(历史)移出门禁；补测试(多守卫/零守卫/菜单清空+版本历史/日志放行/菜单内容拒)；文档补 并发 best-effort + 版本耦合 已知限制 |
| 2026-11-27 | **在用硬删守卫落地（CR-1 P1 闭环）**：`IChannelUsageGuard` 契约（Abstractions）+ `DeleteChannelAsync` 枚举门禁（`CHANNEL_IN_USE`）+ `WeChatOperationsChannelUsageGuard`（回复规则/菜单快照/菜单版本/网关规则/下游端点引用即拒；日志类放行）；门禁 stub 测试 2 + 端到端测试 2 全绿 |
| 2026-11-27 | **Oracle 正式评审 PASS WITH CONDITIONS + code-reviewer APPROVE WITH CONDITIONS 闭环**：C-1/CR-1 §3.1/§3.2/§3.3/C3/§八 更新为"三端点已补暴露（待发布 bump）+ 详情可入 v1 + 删除受在用守卫门禁"；C-2 §5.2 D1 引用纠偏；在用硬删守卫登记为受跟踪后续项（对齐 DeleteMaterialAsync 先例）；CR-2/CR-3/CR-4 同步 Abstractions 与主包 README（GraphQL 收口措辞 + 5 端点表 + 测试义务） |
