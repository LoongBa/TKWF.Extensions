# TKWF.Federation.Abstractions — 联邦通道编排契约包（已发布 **V0.5.0**，2026-10-26；功能线 V0.2.0 Iteration 2——通道契约归属收口 + 关注域契约，随 V0.2.0~V0.5.0 发布链落地）

> **定位**：联邦对外连接层的**通道编排契约抽象**（对齐 TrustCenter.Abstractions 先例——契约继承 `IDomainService` + 引主框架 `TKWF.Domain`，非"零框架引用"）。**零实体、零 SG1 分析器、零 Initializer**——纯契约 + DTO。

## 一、背景（裁定依据）

DMP-Lite 消费方反馈 §四（P2）：`TKWF.Ext.Federation` 主包承载通道编排契约（`IChannelRegistry`/`IChannelSource`/`ChannelConfig`/`IEventReplyHandler`/`IEventOrchestrator`）但**无 Abstractions 契约包收口**——WeChatOperations 扩展为拿契约被迫直接引用 Federation 扩展主包（`WeChatOperations.csproj` 自述），Merchant 域须白名单 Federation 引入 SSO 门面"死注册"。（消费者误称"TKWF.Ext.ChannelRegistry 命名不合规"——无该库，契约在 Federation 主包；判定走 Abstractions 契约包路线，非并入 WeChat 平台库。）

**裁定**：契约（5 接口）迁入本契约包；编排实现（`CompositeChannelRegistry`/`StaticChannelRegistry`/`EventOrchestrator`——依赖 `DomainServiceBase`/Options/AuthCenter）**留在 Federation 主包实现 Abstractions 接口**（契约在 Abstractions、实现在主包的标准分层）。

## 二、契约清单（均自 Federation 主包迁入，namespace 保持 `TKWF.Ext.Federation`——跨程序集同 namespace，实现/消费方零 using 迁移）

| 契约 | 说明 | 依赖 |
|------|------|------|
| `IChannelRegistry : IDomainService` | 通道注册表门面（`GetAsync`/`GetByAliasOrIdAsync`/`GetAllAsync`/`GetDefaultAsync`——选区内核） | `TKWF.Domain`（IDomainService） |
| `IChannelSource` | 静态通道来源（平台库 `XxxOptions.Channels` 投影统一 `ChannelConfig`——`PlatformType`/`GetChannels()`） | 纯 BCL |
| `ChannelConfig` | 通道配置统一模型（公共列 ChannelId/PlatformType/AppId/AppSecret/IsDefault/IsEnabled + 扩展字典 Extra） | 纯 BCL |
| `IEventReplyHandler : IDomainService` | 事件回复处理器契约（消费方实现 + `TryAddEnumerableConstructible` 注册——first-non-null-wins）；**V0.2.0（I2-2）`HandleAsync(ChannelEventDispatch)`**（原 `SsoChannelEventContext` 参数改语义事件 + 已解析 UId） | `TKWF.Domain`（`EventReplyResult`/`ChannelEventDispatch` 已随 `ISsoChannel.cs` 迁入本包） |
| `IEventOrchestrator : IDomainService` | 事件编排门面（平台回复 → handler 迭代 → null 兜底）；**V0.2.0（I2-3）`OrchestrateAsync(channelId, channelType, ChannelInboundContext)` + 记录在后（R3）+ 陈旧注释修复** | `TKWF.Domain`（`ChannelInboundContext` 本包） |
| `ISsoChannel : IDomainService`（**I2-1 迁入**——自 `TrustCenter.Abstractions`，namespace 改 `TKWF.Ext.Federation`） | IdP 适配器契约——`AuthenticateAsync`/`BuildAuthorizeUrlAsync`/`HandleEventAsync`（V0.2.0 返回 `ChannelEventOutcome`）/`VerifyUrlAsync`；随迁 6 records（`SsoChannelAuthContext`/`SsoChannelAuthResult`/`SsoChannelAuthorizeContext`/`SsoChannelAuthorizeResult`/`EventReplyResult`/`EventReplyNewsArticle`） | `TKWF.Domain` |
| `ChannelInboundContext`（I2-2，替代 `SsoChannelEventContext`） | 传输层入站报文上下文（原始 body/query/路径参数——不承载业务语义；业务语义迁 `ChannelEvent`） | 纯 BCL |
| `ChannelEvent` / `ChannelEventOutcome` / `ChannelEventDispatch`（I2-2） | 平台无关语义事件 + 解析结果状态（`NotApplicable`/`Parsed`/`Rejected`）+ handler 输入（`Event` + 已解析 `UId`） | 纯 BCL |
| `ChannelCapabilities`（I2-2） | `[Flags]` 能力声明（`None`/`OAuth`/`Event`/`Follow`/`FollowQuery`/`SilentAuthorize`/`Menu`）——`IChannelSource.Capabilities` DIM 默认 `None` fail-closed（能力不足编排层直接跳过） | 纯 BCL |
| `IFollowChannelQuery` + `FollowStateSnapshot`（I2-2） | 平台关注状态反查（`QueryFollowStateAsync`——实现方如 `WeChatEventChannel`；状态修复/对账用，**不在事件路径调用**） | `TKWF.Domain` |
| `IFollowStateService : IDomainService` + `FollowRecordResult`/`FollowStateDto`/`FollowDailyStatDto`（I2-2） | 关注域门面（`RecordAsync` 幂等/`GetAsync`/`GetByExternalUserIdAsync`/`GetDailyStatsAsync`/`RebuildAsync`/`PurgeEventsAsync`）——实现留 Federation 主包 `FollowStateService` | `TKWF.Domain` |
| `FollowChangedEvent`（I2-2） | 关注域事件（仅 `StateChanged==true` 发布——经 `User.GetService<ILocalEventBus>()` 提交后派发；`[DomainEventHandler]` 消费） | 纯 BCL |
| `ILoginEntryService : IDomainService` + `FederationLoginEntryOptions`（I2-2） | 免登入口（`BuildTicketUrlAsync`/`BuildSilentAuthorizeUrlAsync`——建立在框架 `ISealedTicketService<FederatedIdentity>` 之上，不新造票据原语；`Enabled` 默认 false fail-closed） | `TKWF.Domain` |

> **依赖核实（2026-10-18 ④ / 2026-10-20 I2-1 修订）**：V0.1.0 首发时 `SsoChannelEventContext`/`EventReplyResult` 定义于 `TrustCenter.Abstractions\ISsoChannel.cs`（纯 BCL 契约包）；**I2-1 已将 `ISsoChannel.cs` 整文件迁入本包**（namespace 改 `TKWF.Ext.Federation`），csproj **删除** `ProjectReference → TrustCenter.Abstractions`——本契约包最小引用集收敛为 **`TKWF.Domain`** 单依赖（对齐 Permissions.Abstractions 先例；依赖图单向：TrustCenter（内核）⟂ Federation（连接层））。

## 三、消费方迁移面（改引本包）

| 项目 | 改引 | using 变更 |
|------|------|-----------|
| 9 平台库（WeChat/QQ/DingTalk/WeCom/Oidc/Google/Microsoft/Alipay/Template） | csproj `ProjectReference ..\Federation\TKWF.Ext.Federation.csproj` → `..\Federation.Abstractions\TKWF.Federation.Abstractions.csproj` | **零变更**（namespace 保持 `TKWF.Ext.Federation`） |
| `TKWF.Ext.WeChatOperations` | 同上（为拿契约直引 Federation 主包 → 改引本包） | **零变更** |
| `TKWF.Ext.Federation` 主包 | ProjectReference 本包（实现 Abstractions 接口） | **零变更**（实现类仍在 namespace `TKWF.Ext.Federation`） |

> **I2-1 增补（2026-10-20，V0.2.0）**：Google/Microsoft 平台库原直引 `TrustCenter.Abstractions`（不引 FA）——I2-1 起**替换**为引本包（`using TKWF.Ext.TrustCenter;` → `TKWF.Ext.Federation;`）；其余平台库已在 V0.1.0 改引。WeChatOperations 随 I2-10 经本包关注域契约（`IFollowStateService`）消费派生统计；Federation 主包实现本包新增契约（`FollowStateService`/`LoginEntryService`）。

## 四、版本与发布

- MinVerTagPrefix：`Federation.Abstractions/v`（独立版本，对齐 `Account.Abstractions`/`TrustCenter.Abstractions`/`AuthCenter.Abstractions` 先例）
- 发布：契约包新 tag + 9 平台库 + Federation 主包 + WeChatOperations 新版本联动（tag 须征得用户同意——AGENTS §5）
- **V0.2.0（Iteration 2）发布顺序（方案 §14，已全部执行——2026-10-21~26 发布链）**：① TrustCenter.Abstractions 0.4.0（删 `ISsoChannel.cs` 破坏）→ ② 本包 0.2.0（收口通道契约 + 关注域契约，破坏）→ ③ 平台库（Federation.WeChat 0.6.0 / DingTalk·WeCom·QQ 0.4.1 / Alipay·Oidc·Google·Microsoft 0.3.1）→ ④ TrustCenter 0.3.3（依赖对齐补丁）→ ⑤ Federation 0.11.0 → ⑥ WeChatOperations 0.5.0；每个 tag **逐个 push**（多 tag 单命令会丢事件——已有先例）

<!-- EOF -->
