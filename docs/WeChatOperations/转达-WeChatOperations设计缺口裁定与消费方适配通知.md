# 转达-WeChatOperations设计缺口裁定与开发任务书

> **来源**: TKWF 扩展组（响应 DMP-Lite 消费方反馈）
> **日期**: 2026-10-18
> **关联**: `TKWF.Ext.WeChatOperations` V0.3.1 / `TKWF.Federation.WeChat` V0.4.2 / `TKWF.Ext.Federation`
> **响应对象**: DMP-Lite `v0.10-公众号运营-扩展组转达.md`（4 项设计缺口）
> **性质**: 设计缺口裁定 + **开发任务书**（裁定通过后据此实施）。**消费方适配通知不在此文**——按流程：开发实施 → 审核报告 → **适配通知随审核报告一并编写**（消费方按新版本适配的产物），开发完成后另行产出。

---

## 一、裁定总览

| 项 | 反馈 | 裁定 | 实施归属 | 消费方影响 |
|----|------|------|---------|-----------|
| ① | P0 client DTO 自动生成链路缺口（`[GenerateController]`） | **采纳**（含两处修正：证据引用过时 / 路由契约须锁定） | 扩展组（后续版本） | 手写 DTO/Client 临时保留；扩展侧修复后切换 SG3 生成物 |
| ② | P1 网关管理无 REST 端点 | **采纳**——与 ① 合并为"第 9 门面 `[GenerateController]`"，不另写 minimal API | 扩展组（后续版本） | 网关页面等待新端点；路由契约不变 |
| ③ | P1 消息业务总开关无写端点 | **现象属实、方案修正**——改 **FeatureManagement 消费（DB 化）**，**不做 Options 热更新**（论证见 §三） | 扩展组（后续版本）+ 消费方配合 | **消费方须额外白名单 `FeatureManagementExtensionInitializer<>`**（详见 §四） |
| ④ | P2 通道编排运行时归属 + 命名 | **症状属实、方案修正**——不存在 `TKWF.Ext.ChannelRegistry` 库；契约/编排实现混在 `TKWF.Ext.Federation` 扩展包内；**走 `TKWF.Federation.Abstractions` 契约包路线**（非并入 WeChat 平台库，论证见 §三） | 扩展组（技术债清理，排期） | Merchant 域最终仅白名单 WeChatOperations（死注册消除）；本轮不受影响 |

---

## 二、裁定详述

### ① P0｜client DTO 自动生成链路 —— 采纳（含修正）

**核实结论**：反馈的 4 个事实点全部属实。

- `WeChatOperationsWebExtension` 15 端点全部手写 minimal API（`endpoints.MapGet/MapPost` + private static handler，`WeChatOperationsWebExtension.cs` L59-101），零 `[GenerateController]`；
- 8 个运营门面（`ChannelConfigService`/`MenuService`/`ReplyRuleService`/`MaterialService`/`SubscriptionStatService`/`LogQueryService`/`MessageGatewayService`/`WeChatOperationsStatusService`）均为 `public interface : IDomainService` + `internal sealed class : DomainServiceBase`，无 `[GenerateController]`；
- 消费方手写 312 行 `ClientDtos.cs` + 280 行 `WechatOpsClient.cs` 属实（文件头自注"临时权宜…扩展组修复后应被 SG3 生成物替换"）；
- SG3 对 Merchant 自身 `[GenerateController]` 服务生成全套 `DtoStubs.*.g.cs`/`*Controller.g.cs`，对 WeChat 零生成——链路缺口机制成立。

**修复方向（采纳）**：8 门面实现类 **public 化 + 标 `[GenerateController]`**，SG1b→SG2→SG3 全链路自动生成。机制可行性由本仓 3 个成熟先例实证：`IdentityAuthService`（Identity）/`FeatureManagementApiService`（FeatureManagement）/`UserNotificationViewQueryService`（Notifications）——均 public 类 + 继承 `DomainServiceBase` + 被消费方 SG1b 收集生成控制器。

**两处修正**：

1. **证据引用过时**：反馈引 `TKWF0020` 证实"扩展控制器机制可行"——该诊断码属 **ADR46 时代，已被 ADR47（v4.9.85）取代**（D18A 诊断码总表 §七："代码保留 TKWF0020（SG2 侧兼容），新代码路径以 TKWF0021/0032 为准"；`ExtensionGateTests.cs:511` 断言 SG2 不再报 TKWF0020）。机制本身成立，正确依据是 **SG1b 聚合 + 上述 3 先例**。`GUARD_010`（领域自治）引用无误。
2. **路由契约须锁定**：生成控制器默认按类名/子域路由（先例 AuditLogging 走 `/AuditLogging`），**不继承 `/api/wechat-ops` 前缀**——直接替换会断裂消费方既有 15 条调用路径。修复须经 `[ApiExpose(RestRoute = "/api/wechat-ops/...", RestMethod = "GET/POST")]`（先例 `IdentityAuthService.cs:28` 精确控制 `/auth/register`）逐方法锁定既有路由。另须验证框架统一错误管道产出与现 `ErrorResponse(code/message)` 形状一致（消费方 `WechatErrorResponseDto` 已对齐），否则 `INVALID_ARGUMENT`/`CHANNEL_CONFLICT` 等错误码语义失效。

**实施方式**：替换而非并存（并存 = 路由双暴露）。扩展组后续版本落地。

### ② P1｜网关管理无 REST 端点 —— 采纳（与 ① 合并处理）

**核实结论**：属实。README 组件清单原文"**网关走 handler 帧内，不暴露 REST 端点**"；`WeChatOperationsWebExtension` 15 端点零 gateway/*；`IMessageGatewayService`（`MessageGatewayService.cs:38`）方法全集与反馈所列一致。

**实施方式**：`IMessageGatewayService` 作为**第 9 个 `[GenerateController]` 门面**纳入 ① 同一批次——`List/Save/Delete rules×2组 + status + forward-logs` 即反馈端点表 8 项（`RouteAsync` 转发编排入口**不暴露**——事件路径内部能力，反馈端点表已正确排除）。一并解决：SG3 同时为网关生成客户端类型（AdminUI 网关页面零手写），路由经 `[ApiExpose]` 定到 `{prefix}/gateway/*`。

### ③ P1｜消息业务总开关 —— 现象属实，方案修正为 FeatureManagement 消费（DB 化）

**核实结论**：`MessageBusinessEnabled` 定义于 `WeChatOperationsOptions.cs:24`（`TKWF:WeChatOperations` 节，默认 true），唯一消费点 `WeChatOperationsReplyHandler`（10 处 `!_options.MessageBusinessEnabled` 分支）；15 端点无 toggle 写端点。

**方案修正**（详见 §三论证）：不做 Options 热更新，改 **FeatureManagement 消费**：

1. WeChatOperations 增 `IFeatureDefinitionContributor` 声明 `WeChatOperations.MessageBusiness`（`FeatureValueType.Boolean`，`DefaultValue="true"` 对齐现状）；
2. `WeChatOperationsReplyHandler` 读法改 `await User.Use<IFeatureManager>().IsEnabledAsync("WeChatOperations.MessageBusiness", user, ct)`（fail-closed 语义不变）；
3. 运行时切换经 FeatureManagement 自带 `[GenerateController]` 管理 API（DB 权威、可审计、版本号缓存失效 + 分布式事件传播）——扩展组不新造端点。

**对消费方的 2 个注意点**：

1. **白名单增加**：`FeatureManagementExtensionInitializer<>` 须与 `WeChatOperationsExtensionInitializer<>` 同时白名单（见 §四）；
2. **语义边界**：当前总开关为**全局**业务启停（转发网关不受控——`RouteAsync` 先行仍转发生效，该语义保留）。反馈提议端点路径含 `{channelId}`——若消费方确有按 channel 维度的诉求，属新需求，请另行提出对齐，本轮按全局语义落地。

### ④ P2｜通道编排运行时归属 —— 症状属实，方案修正为 Abstractions 契约包

**核实结论（对反馈的 3 处修正）**：

1. **不存在 `TKWF.Ext.ChannelRegistry` 库**——全仓 glob 零命中；`CompositeChannelRegistry`/`StaticChannelRegistry`/`EventOrchestrator` 及契约 `IChannelRegistry`/`IChannelSource`/`IEventReplyHandler`/`IEventOrchestrator` 全部定义在 `_Framework\Federation\`（namespace `TKWF.Ext.Federation`，**Federation 扩展主包**）。真实问题是"**契约 + 纯逻辑编排实现混在扩展程序集内**"（归层问题），而非"存在命名错误的新库"。措辞修正：Federation 主包未做契约收口。
2. **反馈推荐 A（并入 `TKWF.Federation.WeChat` 平台库）有缺陷**：`IChannelRegistry`/`IChannelSource`/`ChannelConfig` 是**跨平台通用契约**——QQ/DingTalk/WeCom/Oidc/Google/Microsoft/Alipay 平台库均已实现 `{Platform}ChannelSource`；塞进 WeChat 平台库会产生"QQ 引 WeChat 才拿得到 IChannelSource"的反向依赖。事件回复链契约（`IEventReplyHandler`/`IEventOrchestrator`）虽现实偏微信，接口形态亦为通用形。**反馈备选 C（`TKWF.Federation.Abstractions` 契约包，对齐 TrustCenter.Abstractions 先例）才是正确方向**——"零新增包"应让位于跨平台契约语义正确性。
3. **"被迫白名单 Federation → SSO 门面死注册"属实**：`DmpDomainInitializer.cs` L44-46 白名单三扩展（Analytics + WeChatOperations + Federation）；`WeChatOperations.csproj` 直接 ProjectReference `TKWF.Ext.Federation` 为拿契约。**`[TKWFExtensionDependency]` 应声明未声明属实**——`WeChatOperationsExtensionInitializer.cs` L46 仅 `[TKWFExtension("WeChatOperations")]`，其 ConfigureServices 消费 `IChannelRegistry`/`IEventReplyHandler`/`IChannelSource`（DbWeChatChannelSource/ReplyHandler 注册）。

**实施方向（技术债清理，排期）**：契约（`IChannelRegistry`/`IChannelSource`/`ChannelConfig`/`IEventReplyHandler`/`IEventOrchestrator`）→ 新建 `TKWF.Federation.Abstractions` 契约包（Federation 各平台库改引契约包）；编排实现（`CompositeChannelRegistry`/`StaticChannelRegistry`/`EventOrchestrator`——纯逻辑零持久化，符合"运行时纯库应 TKWF.Federation.*"判据）随契约包同层归置；WeChatOperations 改引 Abstractions；`[TKWFExtensionDependency]` 补声明。最终 Merchant 域白名单降为仅 WeChatOperations（死注册消除）。本轮验收不受影响。

---

## 三、为何"FeatureManagement 消费（DB 化）"而非"Options 热更新"（裁定③ 论证）

**结论先行**：消费方诉求本质是"**运行时可切换**"——这是**领域状态的运行时变化**，正确落点是 **DB 化领域开关**，而非给 Options 引入热更新。六点论证：

1. **`IOptions<T>.Value` 是启动期快照，写端点根本不生效**。`WeChatOperationsReplyHandler` ctor 注入 `IOptions<WeChatOperationsOptions>?`，`_options = options?.Value ?? new()`——构造期一次性取值；且全仓 grep `IOptionsMonitor`/`OnChange`/`IConfigurationReloadToken` **零命中**（27 处配置读取全走 `IOptions<T>.Value` 快照模式）。即便加写端点"改 Options"，运行时也不生效——必须同时改 IOptionsMonitor + 热重载改造，改动面更大，且破坏仓库统一"配置=启动期决策"契约。

2. **违背 AGENTS §8 配置分层原则（领域自治）**。判据："开关改变**领域认证行为/安全策略** → 领域配置，表现层不得覆盖"。`MessageBusinessEnabled` 决定"公众号是否对外提供回复能力"——是**领域行为开关**，非 HTTP 暴露面（路由/端点开关）。Web Options 无对应镜像开关是**刻意设计**（先例 `AuthCenterWebExtensionTests.cs:359-361` 断言：Web Options 物理上无 `EnabledAuthTypes` 属性，防"Web 层关掉频控/口令校验"绕过）。给 Options 加 Web 写端点 = 表现层侵入领域决策 = 违反分层红线。

3. **Options 无运行时写路径（无权威事实源）**。配置来源多样（appsettings/环境变量/用户 secrets），运行时改写无可审计、无权限控制、无单一事实源；DB 是唯一可审计、可权限控制、可 fail-closed 的运行时状态载体。

4. **FeatureManagement 是本仓唯一成熟"DB 化动态开关"先例**，能力完全匹配诉求且无重复造轮子：`FeatureValueEntity`（DB 权威）→ `IFeatureDefinitionContributor` 编译期定义 → `IFeatureValueProvider` 链（User→Role→Tenant→Global）→ `IFeatureManager.SetValueAsync`（缓存失效 + Global 唯一性 + 写时校验）→ `FeatureValueChangedEvent` 分布式广播（跨实例即时生效）→ `[GenerateController]` 管理 API（REST 自动）。布尔全局开关正是其标准语义。

5. **正确读法改造成本低且语义不变**。Handler 读法从 `_options.MessageBusinessEnabled` 改为 `IFeatureManager.IsEnabledAsync(name, user, ct)`——**fail-closed 语义兼容**（FeatureManagement 未定义/解析失败 → false，对齐"关闭即统一维护提示"）；`DefaultValue="true"` 对齐现状默认开。

6. **FeatureManagement 自带管理 API，消费方零新端点**。运行时切换经 `SetValueAsync("WeChatOperations.MessageBusiness", "true"/"false", FeatureProviders.Global, null)` 即达，扩展组不需要为"总开关"专门新造 toggle 端点（避免端点膨胀）。自建开关实体（备选 B）需自实现缓存/失效/审计/传播，重复 FeatureManagement 已解决的能力——不采纳。

**判定**：反馈的"DB 化或配置热更新"两个选项里，**"DB 化"方向正确、"配置热更新"方向错误**。扩展组按 DB 化（FeatureManagement 消费）落地，消费方配合白名单。

---

## 四、消费方适配通知（DMP-Lite——配合审核报告交付，开发已完成）

> **适配对象**：`TKWF.Ext.WeChatOperations` **V0.4.0**（设计缺口修复——①②③ 已实施，④ P2 独立批次未启动）。配置 `TKWF:WeChatOperations:Web`（RoutePrefix/端点开关）节点移除——路由编译期常量 `/api/wechat-ops/*`。

| # | 适配项 | 说明 | 时机 |
|:-:|--------|------|------|
| 1 | **白名单增补** `[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer<>))]`（DmpDomainInitializer 类级 + `ConfigureExtensions` 中 `registry.Enable("FeatureManagement")` 双保险对齐既有模式） | ③ 总开关 DB 化依赖——FeatureManagement 管理 API 承载运行时切换（`IfeatureManager.SetValueAsync`） | V0.4.0 升级时 |
| 2 | **移除 WebExtension 装配**：Program.cs `UseWebExtensions(e => e.Add<WeChatOperationsWebExtension<DmpUserInfo>>(...))` 段删除 | ① 已删 `WeChatOperationsWebExtension`（生成端点接管）——保留该装配行会编译失败 | V0.4.0 升级时 |
| 3 | ~~**手写 `ClientDtos.cs`/`WechatOpsClient.cs` 替换为 SG3 生成物**~~ **⚠️ 撤销（2026-10-27 纠正）** | ① 端点部分成立（B4 后经 MapGenerated 挂载），**SG3 客户端部分 B4 下不成立**——扩展带 `[assembly: ExtensionEndpointRegistrations]` → SG2 让路（`ApiServiceGenerator.cs:709`）→ 扩展门面不进 ApiMetadata → SG3 无输入即无输出（DMP 编译实证零生成）。**裁决前手写客户端保留为过渡**（文件头自注 A3 标记 TO-BE-REPLACED-ON-D）；恢复路径 = 框架组议题主案 D（SG2 让路精细化 + ApiMetadata REST 路由元数据 + SG3 REST 客户端分支：`_TKWF/docs/03_扩展模块/转达/转达-ADR112让路守卫破坏SG2-SG3元数据传播-请框架组裁决.md`） | 待框架组裁决 |
| 3′ | **✅ v4.10.92 修复后恢复替换（2026-10-28，ADR117/ADR118 闭环）**：框架组根治——SG1b `GenerateControllerCatalog.InterfaceNames` 符号回填真名（扩展门面真实命名空间无 `.Controllers` 假名）+ 手写门面判定放宽（`IDomainService` 接受）→ **扩展门面重新进消费方 ApiMetadata → SG3 客户端 DTO/代理恢复生成**。消费方 CPM lockstep 4.10.92 + 重编 Domain/WebApi 后验证（`ApiMetadata.g.cs` 含 8 WeChat 门面），**手写客户端可退役**（替换标准动作：`AddRestApiClient` + `ModulePrefixResolver` + `RestTypeInfoRegistrar` v4.10.90 起零配置） | ✅ 已闭环 |
| 4 | **总开关语义切换**：原 `TKWF:WeChatOperations:MessageBusinessEnabled` 配置（若已用）语义保留（Options 启动默认——显式 false 仍关闭）；**运行时切换改走 FeatureManagement** | `manager.SetValueAsync("WeChatOperations.MessageBusiness", "true"/"false", FeatureProviders.Global, null)`——DB 权威、重启不丢；未存值默认开 | V0.4.0 升级时 |
| 5 | **网关页面对接新端点**：转发网关页 `GET/POST/DELETE /api/wechat-ops/gateway/rules` + `endpoints` + `status` + `forward-logs` + `enabled`（9 端点）；系统状态页总开关切换经 FeatureManagement 管理 API | ② 网关端点补全——验收路径 8/11 阻塞解除 | V0.4.0 升级时 |
| 6 | **命令式请求改用 wire DTO**（若直接调门面）：`SaveChannelRequest`/`SaveEndpointRequest`/`SetGatewayEnabledRequest`/`PublishMenuRequest`/`RollbackMenuRequest`（`TKWF.Ext.WeChatOperations.DTOs`） | 门面签名破坏性变更（实体→DTO）——DMP-Lite 若仅走 REST 则无感，仅扩展测试内部适配 | V0.4.0 升级时 |
| 7 | **④ 前置验证降级**（后续批次）：Merchant 域白名单降为仅 WeChatOperations（+ FeatureManagement）时，Federation 白名单可移除——届时前置验证改为"验证 WeChatOperations 白名单 + WeChat 平台库引用的启动行为" | ④ P2 独立批次（本轮不触发） | ④ 落地后 |

> **升级步骤（推荐顺序）**：① 升级 `TKWF.Ext.WeChatOperations` V0.4.0 → ② 白名单增补 FeatureManagement + Program.cs 移除 WebExtension 装配（⚠️ **V0.6.0 起改回保留装配**——B1-c 修复重建 `WeChatOperationsWebExtension`，`e.Add<WeChatOperationsWebExtension>()` 必须存在否则 25 端点 404）→ ③ 编译 + 启动（生成端点 `/api/wechat-ops/*` 可用——验证 `GET /channels` 返回既有形态）→ ④ ~~手写 ClientDtos/WechatOpsClient 替换为 SG3 生成物~~ **改为保留**（2026-10-27 纠正：B4 分支下 SG3 不生成扩展客户端，见适配项 3——待框架组主案 D 裁决后替换）→ ⑤ 总开关经 FeatureManagement 管理 API 切换（验收路径 8/11）。**✅ v4.10.92 修复后（2026-10-28，ADR117/ADR118 闭环）**：④ 恢复为**替换为 SG3 生成物**——消费方 CPM lockstep 4.10.92 + 重编 Domain/WebApi 后（`ApiMetadata.g.cs` 含 8 WeChat 门面实证）手写客户端可退役（替换标准动作：`AddRestApiClient` + `ModulePrefixResolver` + `RestTypeInfoRegistrar` v4.10.90 起零配置；协议型端点仍手写，C-6 排除范围）。**FeatureValue 表**由框架 SyncTables 自动建（`TKWF_FeatureValue`——无手工 DDL 前置，存量库 ALTER 由 SyncTables 托管）。
>
> **行为差异（相对 V0.3.1）**：路由前缀不可配置（编译期常量 `/api/wechat-ops`，原默认值不变）；端点开关不可逐组关闭（原来 `WeChatOperationsEndpointOptions.*EndpointEnabled`——默认全开，变更为编译期声明）；`DOMAIN` 帧内门面调用签名变更（DTO 参数）。

---

## 五、开发任务书（实施状态——① ② ③ 已完成，④ 待排期）

> 裁定已通过（①②③④），据此实施。**①+②+③ 已全部实施闭环（代码 + 测试 + 文档 + 验证，2026-10-18）**——详见审核报告（主框架 `_TKWF/docs/03_扩展模块/WeChatOperations/WeChatOperations-设计缺口修复-审核报告.md`）；**④（Abstractions 契约包）为 P2 独立批次，迁移量化已评估，待单独排期**。流程：开发完成 ✓ → 审核报告 ✓ → 适配通知 ✓（本文件 §四）→ 提交/tag（征得同意后）。

### ⑤ 验证结果（2026-10-18 定稿）

- ✅ **slnx 构建 0 错误**
- ✅ **WeChatOperations 测试 143 通过 / 2 跳过 / 0 失败**（签名适配后全绿，含 `MessageBusinessSwitchTests` Options 双源语义用例）
- ✅ **受影响面 6 项目全绿**：Federation.WeChat 100 / MessageGateway 57 / FeatureManagement 135 / Federation 79 / TrustCenter 35
- ⚠️ 本地构建曾被 `dotnet-analyzer mcp serve` 常驻进程锁定部署根 refs DLL（MSB3027 复制失败非代码错误）——释放后复验 0 错误；构建前如遇此错误先 `Stop-Process -Name dotnet-analyzer`

### 已实施（代码 + 测试 + 文档全闭环）

- [x] **①（P0）**：8 门面实现类 public 化 + `[GenerateController]` + 接口方法 `[ApiExpose(RestRoute/RestMethod/ExposeRest)]` 锁定 `/api/wechat-ops/*` 路由；**安全 wire 改造**（`SaveChannelRequest`/`SaveEndpointRequest`/`SetGatewayEnabledRequest` 凭证明文 body 传输防 URL 泄漏）；11 内部能力方法 `[ApiExpose(ExposeRest=false)]`；`WeChatOperationsWebExtension.cs` 删除（生成端点接管）
- [x] **②（P1）**：`IMessageGatewayService` 网关 8 端点（rules×3 + endpoints×3 + status + forward-logs）+ `POST /gateway/enabled`；`RouteAsync` 排除
- [x] **③（P1）**：`WeChatOperationsFeatureDefinitionContributor` 声明 `WeChatOperations.MessageBusiness` + `WeChatOperationsReplyHandler` 10 处读法改 `IsMessageBusinessEnabledAsync`（Options 启动默认 + FeatureManager DB 权威双源）+ csproj 引 FeatureManagement + `[TKWFExtensionDependency]` 补声明 + `<ImplicitUsings>enable</ImplicitUsings>`（主框架 SG1 模板缺 using 临时修复，待报框架组）
  - ✅ **ImplicitUsings 绕行回退已闭环（2026-10-18，提交 1eaa72b）**：主框架 v4.10.81 已修复（`EntityMetadataGenerator.Generation.cs`——`BuildContributorsSource` 文件头补 `using System.Collections.Generic;`）并发布 + 部署根 refs 同步新 DLL → 扩展侧 CPM lockstep `TKWF.CodeGeneration 4.10.80 → 4.10.81` + 删除本 csproj `<ImplicitUsings>enable</ImplicitUsings>` 绕行 → WeChatOperations 构建 0 错误复验（`GeneratedContributors.g.cs` 自包含 `using System.Collections.Generic;`，无需宿主 ImplicitUsings）→ 提交 `1eaa72b`（chore: CPM lockstep + 回退绕行）。DMP-Lite 消费方侧同步验证：双模式编译 0 错误。
- [x] **测试同步**：7 测试文件签名适配（SaveAsync/PublishAsync/RollbackAsync/SaveEndpointAsync/SetGatewayEnabledAsync）+ 宿主 FeatureManagement 装配（MetaContext + FeatureValue 表/DAC/DataService）
- [x] **文档**：使用指南 §七 端点总表（15→25 端点）/装配段/配置段/总开关段/版本记录 + README 技术规范 + 根 README
- [x] **适配通知**：本文件 §四（配合审核报告交付）

### ④ 独立批次（P2 技术债，未启动——迁移量化已评估）

- [ ] **④（P2）**：新建 `TKWF.Federation.Abstractions` 契约包（对齐 TrustCenter.Abstractions 先例——契约继承 `IDomainService` + 引主框架 `TKWF.Domain`，非"零框架引用"）：
  - 迁入契约：`IChannelRegistry`/`IChannelSource`/`ChannelConfig`/`IEventReplyHandler`/`IEventOrchestrator`（定义于 Federation 主包 `_Framework/Federation/`，namespace `TKWF.Ext.Federation`）
  - **依赖图（已核实，2026-10-18）**：`IChannelSource`/`ChannelConfig` 纯 BCL（仅 System.Collections.Generic）；`IChannelRegistry`/`IEventReplyHandler`/`IEventOrchestrator` : `IDomainService`（引主框架 Domain.Interfaces）+ `IEventReplyHandler` 签名用 `SsoChannelEventContext`/`EventReplyResult`——**定义在 TrustCenter.Abstractions**（纯 BCL 契约包，非 TrustCenter 主包！）→ 契约包最小引用集 = **TKWF.Domain（IDomainService）+ TrustCenter.Abstractions（事件上下文/回复结果）**，可行性优于裁定预估
  - 编排实现（`CompositeChannelRegistry`/`StaticChannelRegistry`/`EventOrchestrator`——依赖 `DomainServiceBase`/Options/AuthCenter）**留在 Federation 主包实现 Abstractions 接口**（契约在 Abstractions、实现在主包的标准分层：Federation 主包 ProjectReference Abstractions）
  - **迁移面（已量化）**：9 平台库（WeChat/QQ/DingTalk/WeCom/Oidc/Google/Microsoft/Alipay/Template）全部 `using TKWF.Ext.Federation`（3-6 处 each）→ `using TKWF.Federation.Abstractions`，csproj 改引契约包；**平台库仅用契约类型（IChannelSource/ChannelConfig/IEventReplyHandler），纯换包可行**；WeChatOperations csproj 直引 `TKWF.Ext.Federation`（为拿契约）改引 Abstractions；Federation 主包减负（契约迁出后剩编排实现 + SSO 门面 + FederationWebExtension）
  - `[TKWFExtensionDependency]`：WeChatOperations 对通道编排的依赖声明改指 Abstractions 契约（Implementor 归 Federation 白名单——消费方仍须白名单 Federation 以装配编排实现的 DI？需核对：Abstractions 无 Initializer，编排实现 DI 装配仍由 FederationExtensionInitializer 完成 → 消费方仍需白名单 Federation，但 WeChatOperations 引用面降为契约包）
  - 发布：契约包新 tag（`Federation.Abstractions/v0.1.0`）+ 9 平台库 + Federation 主包 + WeChatOperations 新版本联动发布——**破坏性跨包变更，须用户确认后启动**
- [ ] 文档：使用指南 §七 网关"无 REST 端点"段更新（②落地后）；总开关段更新（③落地后）；组件清单/依赖表更新（④落地后）

<!-- EOF -->
