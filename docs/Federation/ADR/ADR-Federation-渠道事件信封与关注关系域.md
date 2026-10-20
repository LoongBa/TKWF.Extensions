# ADR-Federation-渠道事件信封与关注关系域

> **版本**：v0.1（随开发方案评审定稿）
> **状态**：草案（随 `docs/Federation/I2-渠道关注关系域-开发方案.md` v0.5.1 评审；Oracle3 `APPROVE WITH CONDITIONS`（P0-1/P1-1/P1-2/P1-3/P2-2），条件已全部吸收于方案，本 ADR 记定型决策）
> **关联**：开发方案 `docs/Federation/I2-渠道关注关系域-开发方案.md`（v0.5.1，权威源）；Iteration 1 开发方案 `docs/AuthCenter/v0.11.0-认证关系模型与冻结解绑-开发方案.md`（`FreezeAsync`/`ReleaseFreezeAsync` + `IExternalIdentityService`，代码 `6ab7857`）；ADR-Federation-平台特定事件回复Handler基类与二级分发（事件链既有决策，本 ADR 在其上收口 `SsoChannelEventContext`/`HandleEventAsync`）；框架 `ISealedTicketService<T>`（`TKW.Framework.Domain.Ticket`）/ `ILocalEventBus`（`TKW.Framework.Domain.Events`，oracle6 C1 入口校正）
> **日期**：2026-10-20

---

## 目的与目标

把「渠道协议」与「关注业务」彻底切开：**平台层只产 `ChannelEvent` 事实，关注域只维护状态与事件，业务层只消费事件做决策**。目标状态：① 渠道协议层（`Federation.{平台}`）只做验签/解密/解析，经 `HandleEventAsync` 返回 `ChannelEventOutcome`，语义事实不再塞进传输字典；② 关注关系域（Federation 主包）以 `FollowEventEntity`（事实日志）+ `FollowStateEntity`（聚合根）+ `FollowChangedEvent`（域事件）承载关注状态，`WeChatOperations` 订阅统计改为派生；③ 业务决策层只订阅域事件（冻卡/停权益），不碰任何平台协议；`Federation.Abstractions` 收口通道契约，信任内核包回归纯信任。读者 3 句话内明白：关注/取关不再只回复不落状态，状态与事实落在关注域并广播域事件，平台协议与关注业务两个关注点互不越界。

## 问题

### 问题现象

1. **魔法字典传语义**：`SsoChannelEventContext.Parameters`（`IDictionary<string,string?>`）承载 `uid`/`msg_type`/`event`/`__decrypted_xml` 四个魔法键，消费方只能靠字符串约定取值，键名即语义且无清单约束，平台可随意造裸键。
2. **关注状态无处存放**：`SubscriptionStatEntity` 是日粒度累计（`StatDate` 维度），答不了「某 openid 当前是否关注」这个单用户粒度问题。
3. **无任何域事件**：消费方无法订阅「某人取关」，业务决策（冻结账号、停权益）没有信号源。
4. **「关注即登录」不可用**：`{ticket}` 占位符在模板 handler 中不替换（V0.5.0 已知缺口），免登链路断在回复渲染。
5. **契约归属反向依赖**：`ISsoChannel.cs`（7 records + 1 interface）寄居 `TrustCenter.Abstractions` 信任内核包，而 `Federation.Abstractions → TrustCenter.Abstractions` 这条引用使连接层反向依赖内核，每次通道协议演进都要动内核包版本。

### 触发场景

- 微信关注/取关事件推送：希望落状态并发 `FollowChangedEvent`，供业务层冻结决策（如取关 → `FreezeAsync(uid, "wechat_unfollow")`，Iteration 1 能力）。
- `WeChatOperations` 订阅统计读取：现有日粒度表只记累计，无法按事件事实派生「新增/取消/净增」口径。
- 免登闭环：关注/发消息/点击菜单 → 签 `{ticket}` → 图文链接 → 落地页兑换建 session。
- 未来钉钉/企微接入关注链：按同一契约产出 `ChannelEvent`。

### 现有方案的不足

- **传输字典承载语义**：`Parameters` 本应只放协议原始字段，`uid`（语义）/`msg_type`/`event`（语义）/`__decrypted_xml`（解密正文）混入后，传输层与语义层职责不分；MessageGateway SyncProxy 透传依赖字典化 `__decrypted_xml`，是「被字典化的教训」。
- **日粒度表替代不了状态**：无单用户粒度当前状态，也无法表达「现在关不关注」。
- **契约寄居内核**：`Federation.Abstractions` 定位（2026-10-18 ④ 裁定）是「通道编排契约收口」，`ISsoChannel` 却在信任内核并由内核转述；`TrustCenter` 主包对 `ISsoChannel` 零功能消费（仅注释与描述文本），反向语义依赖成立。

## 使用场景

### 适用场景

- **微信关注/取关落状态并发事件（首批）**：`subscribe`/`unsubscribe`/`SCAN` → `ChannelEvent` → `FollowStateService.RecordAsync` → `FollowChangedEvent`（仅状态变化时），业务层订阅做冻结决策（E7：示例 handler 收 `FollowChangedEvent(UId 非 null, IsFollowing=false)` → `FreezeAsync(uid,"wechat_unfollow")`）。
- **订阅统计派生**：`WeChatOperations.GetTrendAsync` 委托 `IFollowStateService.GetDailyStatsAsync`，口径与事件日志一致。
- **未来平台接入**：钉钉/企微（钉钉事件解析移出本轮，见方案 O6；契约键已预留 `dingtalk.*`）/其他事件通道按同契约接入。
- **免登入口**：`{ticket}` 链路（仅 `follow`/`message`/菜单点击路径）。

### 不适用边界

- **非事件通道（QQ/Alipay/OIDC/Google/Microsoft）**：`HandleEventAsync` DIM 默认 → `NotApplicable`，`IChannelSource.Capabilities` 默认 `None`，编排零副作用、零记录、零事件。
- **WeCom**：无「关注」概念，不声明 `Follow` 能力。
- **「把关注当认证因素」（明确否决）**：AuthCenter 不增 `IsFollowing`/`UnfollowedAt` 列；取关是否冻结账号由**业务层**决策后调用 AuthCenter 冻结能力，AuthCenter 不感知「关注」（用户裁定边界，方案 §背景与边界）。
- **钉钉关注事件解析（本轮移出，O6）**：`DingTalkEventChannel` 现仅 `AuthenticateAsync`（事件回调→验签/解密/取 `FromUserId`，属「事件换身份」），无事件语义产出路径；本轮仅随契约归属做 `using`/引用迁移（patch 0.4.1）。

---

## 决策

> 详细设计见开发方案 §三/§四/§五/§六/§八。本 ADR 记架构决策要点，引用的 oracle 序号为 Oracle 评审条件编号。

### 1. 语义层契约：`ChannelEvent` / `ChannelEventOutcome`（§3.3）

- `ChannelEvent` 为平台无关的**语义事实** record：`ChannelId`/`ChannelType`/`ExternalUserId`/`UnionId`/`EventType`/`EventKey`/`Source`/`MessageId`/`OccurredAt`/`RawPayload`/`Attributes`。`ExternalUserId` **不可空**（关注域主键的一半，平台解析不出 openid 时应返回 `NotApplicable`/`Rejected`，而不是造半个事件）。
- `HandleEventAsync` 返回 `ChannelEventOutcome`，三态枚举 `ChannelEventOutcomeState`：`NotApplicable = 0`（非事件通道，DIM 默认）/ `Parsed = 1`（解析成功）/ `Rejected = 2`（信任根失败/解密失败，编排**短路**：不记录、不回复、不回退 handler）。
- **不用异常表达 `Rejected`**：微信回调必须 200 空回复（5 秒 SLA），异常在装配层被吞成 null，无法区分「信任根拒绝」与「代码 bug」；状态可精确断言。`ChannelEventOutcome` 不携带平台回复（V0.4.0 起平台不再自回 XML，恒 null 字段 = 死接口）。
- **`Attributes` 键强制 `{platform}.{Name}` 前缀**（oracle6 C6）：平台库不得自行命名裸键，跨平台键冲突由此消除；只放「不足以升为一等属性」的协议补充字段，**禁止**放业务字段（业务字段走 `ChannelEvent.Source`/`EventKey` 等一等属性）。
- **`Attributes` 键清单在本 ADR 固化**（新增平台/新键须先补清单再实施）：

| 平台 | 键 | 语义 |
|------|-----|------|
| WeChat | `wechat.MsgType` | 原始消息类型（text/event/image/...） |
| WeChat | `wechat.Event` | 微信事件原文（subscribe/unsubscribe/SCAN/CLICK/...，与一等属性 `EventType` 并存，保留原文对齐协议诊断） |
| WeChat | `wechat.ToUserName` | 公众号原始 id（回复信封需要） |
| WeChat | `wechat.FromUserName` | 发送方 openid（= `ExternalUserId`，保留原文对齐协议诊断） |
| WeChat | `wechat.Content` | 文本消息原文保留 |
| WeChat | `wechat.CreateTime` | 协议秒级时间戳原文（`OccurredAt` 为其 UTC 换算） |
| DingTalk | `dingtalk.CorpId` / `dingtalk.AgentId` / `dingtalk.EventId` | 预留，本轮暂不产出（O6，钉钉事件解析移出本轮） |
| WeCom | `wecom.CorpId` / `wecom.AgentId` / `wecom.MsgType` | 预留 |

- **`RawPayload` 放语义层**（承载解密后的原始报文，微信 XML）：MessageGateway SyncProxy 需要完整透传数据源；放语义层后 handler 无需再接触传输字典（替代被字典化的 `__decrypted_xml`）。

### 2. 能力声明：`ChannelCapabilities`（§3.4）

- `[Flags]` 枚举：`None`/`OAuth`/`Event`/`Follow`/`FollowQuery`/`SilentAuthorize`/`Menu`。
- **落点 `IChannelSource`**（新增 DIM `Capabilities => ChannelCapabilities.None`），**不是 `ISsoChannel`**：能力是渠道实例（`ChannelConfig`）投影层的声明，不污染通道契约接口。
- **默认 `None` 刻意 fail-closed**：新增平台库忘声明 → 关注域静默不记录（而非降级尝试「试着调一下看报不报错」）。
- 判据：能力不足 → 编排层**直接跳过**（不报错、不降级、不打 try/catch 兜底）。消费点：`EventOrchestrator` 记录前检查 `Follow`；订阅统计派生检查 `Follow`；免登入口检查 `SilentAuthorize`。
- 首批声明：微信 `OAuth|Event|Follow|FollowQuery|SilentAuthorize|Menu`；QQ/Alipay/OIDC/Google/Microsoft 仅 `OAuth`；WeCom `OAuth|Event`（不声明 `Follow`）。

### 3. 关注域模型：`FollowEventEntity` + `FollowStateEntity`（§4）

- **`FollowEventEntity`（`TKWF_FollowEvent`）事实追加写**：不可变事件日志。`DedupeKey`（UNIQUE）幂等键收到 `{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey ?? "-"}|{messageId ?? "-"}`；超 256 字符 → `SHA256(key)` 十六进制（保留可判定性，不截断，截断会产生假碰撞）。索引：`(ChannelId, ExternalUserId, OccurredAt)` 与 `(ChannelId, OccurredAt)`（日统计下推）。
- **`FollowStateEntity`（`TKWF_FollowState`）聚合根**：`(ChannelId, ExternalUserId)` UNIQUE 定位当前状态（`IsFollowing`/`SubscribedAt`/`UnsubscribedAt`/`LastEventType`/`LastEventAt`/`LastReceivedAt` 等）。
- **R8：状态是领域事实，非派生投影**。`FollowState` 不属于 Iteration 1 D9「禁派生投影列」范围，判据四要素：① 不能在查询期从同域数据算出（跨渠道异构事件流，且需 O(1) 点查）；② 单一写者（仅 `IFollowStateService` 事件驱动路径）；③ 可重建校验（`RebuildAsync` 从 `FollowEvent` 重建并逐行比对）；④ 并发控制（唯一索引 + 排序守卫）。`RebuildAsync` 只用于修复/对账，不是常规查询投影。
- **排序守卫两段比较（oracle6 C4，不可用元组 `>`）**：C# `ValueTuple<T1,T2>` 未定义 `>` 运算符，且该形式无法翻译为 SQL，必须写成：
  ```csharp
  newer = evt.OccurredAt > state.LastEventAt
       || (evt.OccurredAt == state.LastEventAt && evt.ReceivedAt > state.LastReceivedAt);
  // 仅当 newer 时覆盖状态字段；事件行无论是否 newer 都落库（事实不可丢）
  ```
  两处实现必须同型：`RecordAsync` 的 LINQ `Where`（条件更新 `EntityUpdateWhereAsync`）+ 更新前内存判定（返回 0 行时区分「被排序守卫挡下」而非「行不存在」）。
- 数据访问经 SG1 生成的 DataService（`FollowEventEntityDataService`/`FollowStateEntityDataService`）组合，**零手写 Store**（AGENTS §8 红线）。

### 4. 记录在后：R3（§5.2）

编排时序（`EventOrchestrator.OrchestrateAsync`）：通道实例化 → `HandleEventAsync` 产出 `ChannelEventOutcome` → `Rejected` 短路返回 null / `NotApplicable` 跳过记录 → `Parsed`：`IExternalIdentityService.GetByExternalIdAsync`（**只读，不建号**，`uid = dto?.UId`）→ 构造 `ChannelEventDispatch(evt, uid)` → 双桶迭代 handler 出回复 → **`RecordAsync` 在后** → 返回 reply。

- **为什么记录在后**：`OrchestrateAsync` 是 AOP 帧最外层，所有域服务写共享同一 UoW/物理事务；PostgreSQL 任意语句失败即 abort 整个事务，`DedupeKey` 唯一约束冲突会让事务进入 aborted 状态。若记录在前：冲突 → 事务中毒 → 后续 handler 的业务写（如 `WeChatOperations` 的 `InteractionLog`/统计写）全部失败。记录在后：handler 业务写先成功，记录冲突只影响自身与提交阶段，回复内容已生成。同时 try/catch 分段保留「回复失败不影响状态」：handler 异常被吞，记录照常执行。
- **记录失败处置**：catch + 日志，回复照常返回（微信重投补齐；失败隔离矩阵见方案 §5.3）。并发冲突：`RecordAsync` 先按 `DedupeKey` 查存在（常规路径零异常）→ 插入仍冲突（真并发）→ 捕获唯一约束异常 → 立即返回 `Recorded=false`（**不在同一事务内继续写任何东西**，避免二次中毒）。
- **R3 附条件明示（oracle 复审要求必须写明）**：`RecordAsync` 的 `DedupeKey` 冲突**仍会 abort 同一物理事务**，try/catch 只能保住「回复内容已生成」，保不住 handler 的业务写。即：并发重投时，`WeChatOperations` 的 `InteractionLog`/订阅统计写（以及与关注事件同事务的任何扩展写）**会被一起回滚**。这是本迭代**接受的已知代价**：正常路径上「回复优先于记录」且微信 5 秒 SLA 不因幂等查询变慢，概率 = 同事件真并发重投，被回滚的是审计日志而非状态。落地前确认 DMP-Lite「审计日志偶尔缺一条（限于并发重投窗口）」可接受。
- **根治方向（不在本迭代）**：把 `RecordAsync` 移到独立事务/独立连接（需框架 `RequiresNew` 或事件后置补偿），已在方案 §十七 O5 登记；**不做扩展侧事务 hack**（AGENTS §8 数据访问红线）。

### 5. 域事件：`FollowChangedEvent`（§3.7）

- record 承载 `ChannelId`/`ChannelType`/`ExternalUserId`/`UnionId`/`UId`/`IsFollowing`/`EventType`/`EventKey`/`Source`/`OccurredAt`。
- **仅在 `StateChanged == true` 时发布**：重复投递/乱序回落不产生噪音；重放同事件（`Recorded=false`）不重复发。
- **`UId` 由编排层解析注入**：经 `IExternalIdentityService.GetByExternalIdAsync(channelId, externalUserId)`（只读不建号）；未绑定时 null，**照常发布**，业务层自行决定「未绑定用户是否忽略」。
- **发布经 `User.GetService<ILocalEventBus>().PublishAsync(evt)`**（oracle6 C1）：**不能写 `User.Use<ILocalEventBus>()`**，`Use<T>` 约束 `where TDomainService : IDomainService`，而 `ILocalEventBus` 不继承 `IDomainService` → 编译失败；`GetService<T>`（`where TService : notnull`）是框架为「非领域服务的基础设施服务」提供的标准入口（同型先例：`AuthController` 的 `User.GetService<IAmbientContext>()`、`DomainDataServiceBase` 的 `User.GetService<IIdGenerator>()`）。`PublishAsync` 只把事件放入帧内收集队列，真正派发由 `EventDispatchFilterAttribute` 在 **UoW 提交后**触发（天然满足「状态落库成功才发事件」）。
- 消费方形态：`[DomainEventHandler]` 特性 + SG1 编译期注册（框架标准路径，零新依赖）。

### 6. 免登入口：`ILoginEntryService`（§3.8/§6）

- `ILoginEntryService` 提供 `BuildTicketUrlAsync`/`BuildSilentAuthorizeUrlAsync`，**建立在框架既有原语 `ISealedTicketService<FederatedIdentity>` 之上**（`TKW.Framework.Domain.Ticket`，TrustCenter `SealedTicketService<T>` DB 后端），与 `EventReplyHandlerBase` 签票用**同一原语**，**不引入第二套票据实现**（R4 单一签票点，否则正是要消灭的签票逻辑漂移）。
- **基址复用单一事实源**：复用 `FederationEventReplyOptions.TestTicketEndpointBaseUrl`/`RoutePrefix`，**不新增 `BaseUrl` 配置项**（避免基址双源）；仅 `ChannelBaseUrls` 按 `ChannelId` 覆盖（可选）。`FederationLoginEntryOptions` 只留开关与 TTL（`Enabled` 默认 false / `TicketPath` 默认 `/login/ticket` / `TicketTtlSeconds` 默认 300，须 ≤ AuthCenter 密封票据 TTL，启动期超限 fail-fast）；配置节 `TKWF:Federation:LoginEntry`。
- **`{ticket}` 占位符 fail-closed**：未启用/未配置基址/签票失败 → 替换为**空串**，其余文本照常渲染，回复结构不变。`WechatEventReplyHandlerBase.ReplacePlaceholders` 是 static（内部无法触碰 `User`）→ 新增**实例** helper `ResolveTicketAsync`（帧内 `User.Use<ILoginEntryService>()`）+ **静态重载** `ReplacePlaceholders(text, uid, openId, channelId, ticket)`，调用点各解析一次（每次回复只签一次票，不是每字段一次）；模板 `NewsReplyTemplate.TtlSeconds` 优先。
- 主包 `EventReplyHandlerBase`（签票基类）**不改行为**：`BuildTestTicketUrl`/`BuildTemplateUrl` 改为委托 `ILoginEntryService` 的同一 URL 组装实现（**逐字节等价，测试锁定**），URL 拼装在全文仅一处。
- **安全**：一次性 + 短 TTL + 仅 null-claimant 票（AuthCenter V0.10.0 保证）；链接可转发但不可重放；`ticket` 全串不入日志（只记 `channelId` + `externalUserId` 前 6 位）。
- **协议门控「取关时带票不可能」**：`unsubscribe` 属微信 `NoReplyEvents`，平台基类返回 null 回复，注册表**记录照常发生**（记录与回复完全解耦）；免登入口只存在于 `follow`/`message`/菜单点击路径。

### 7. 契约归属：`ISsoChannel` 迁入 `Federation.Abstractions`（§8/I2-1）

- `ISsoChannel.cs` **整文件**（6 records + 1 interface）从 `TrustCenter.Abstractions` 迁入 `Federation.Abstractions`；命名空间改为 `TKWF.Ext.Federation`（**跨程序集同 namespace**，与既有 `Federation.Abstractions` 契约一致，消费方**零 using 迁移**，仅 csproj 改引 + 删旧 `using TKWF.Ext.TrustCenter;`）。
- 理由：信任内核对 `ISsoChannel` **零功能消费**（仅 XML 注释 + `AssemblyInfo` 描述文本，§1.4 证据）；`Federation.Abstractions` 定位即「通道编排契约收口」；迁出后 `Federation.Abstractions` 可**完全解除**对 `TrustCenter.Abstractions` 的项目引用，依赖图变单向（TrustCenter（内核）⟂ Federation（连接层））；且本次本来就破坏 `HandleEventAsync` 签名，0.x 阶段一次做完，避免二次破坏。
- 否决备选：① 保留内核（内核继续承载协议类型，与其「纯信任内核」定位矛盾）；② 拆分 `ISsoChannel`（一半在内核一半在连接层，实现方要引两个包）；③ 只换程序集保 namespace（包名与命名空间错位，排障地狱）。
- 传输层更名：`SsoChannelEventContext` → `ChannelInboundContext`，语义收窄为「原始入站报文」（只允许协议原始字段：`__raw`/`msg_signature`/`timestamp`/`nonce`/`echostr`/`channel_id`），**禁止**再写入业务语义（`uid`/`msg_type`/`event`/`__decrypted_xml` 全部迁到 `ChannelEvent`）。
- 迁移面：8 平台库（WeChat/QQ/DingTalk/WeCom/Oidc/Google/Microsoft/Alipay）+ `WeChatOperations` + Federation 主包 + 测试；Google/Microsoft 为**替换**引用（TC→FA，实查两库不引 FA）。
- **破坏性版本矩阵（按 2026-10-20 实际 tag 校正）**：

| 包 | 版本 | 级别 |
|----|------|------|
| TrustCenter.Abstractions | **V0.4.0**（`0.3.1` → 删 `ISsoChannel.cs`） | 破坏 |
| Federation.Abstractions | **V0.2.0**（通道契约收口 + 关注域契约） | 破坏 |
| Federation | **V0.11.0**（关注域 + 编排 + 免登） | 破坏 |
| Federation.WeChat | **V0.6.0** | 破坏 |
| WeChatOperations | **V0.5.0** | 破坏 |
| Federation.DingTalk | V0.4.1（仅 `using`/引用迁移，无行为变更） | patch |
| 其余 6 平台库（WeCom/QQ/Alipay/Oidc/Google/Microsoft） | patch（仅 `using`/引用迁移或引用替换，零行为变更） | patch |
| TrustCenter | V0.3.3（依赖对齐补丁，公共 API 零变更，不升 minor） | patch |

发布顺序严格：① TC.Abstractions 0.4.0 → ② FA 0.2.0 → ③ 平台库 → ④ TrustCenter 0.3.3 → ⑤ Federation 0.11.0 → ⑥ WeChatOperations 0.5.0；tag **逐个 push**（多 tag 单命令会丢事件，已有先例）。

### 8. 统计派生：删 `SubscriptionStatEntity`（§8.2 C9/I2-10）

- **删除 `SubscriptionStatEntity`**（含其 DataService/Conditions/`.g.cs` 与 `RecordStatAsync` 写路径），`WeChatOperations` **12 → 11 实体**（8 运营 + 4 网关 → 7 运营 + 4 网关）。
- 决策依据（Q4「直接派生 or 双写」）：**直接派生并删本地实体**，双写会造出「两份真相」，且跨扩展读他域表违反红线；开发阶段无生产数据，直接切换不保留双写。
- `ISubscriptionStatService` 三成员改造（oracle6 C9 事实校正，接口实为 3 成员）：
  1. `RecordAsync` **删除**：写路径唯一归编排层 `IFollowStateService.RecordAsync`，保留即双写双源；
  2. `GetTrendAsync` 返回类型改为 `IReadOnlyList<SubscriptionStatPointDto>`（新增 WeChatOperations 侧 DTO：`Date`/`NewFollows`/`Unsubscribes`/`NetGrowth`/`Source`），与契约包 `FollowDailyStatDto` **同形但不直接外露契约包类型**（避免主包 API 签名穿透 `Federation.Abstractions`），实现委托 `IFollowStateService.GetDailyStatsAsync` + 映射；
  3. `GetSummaryAsync` **签名不变**，实现改为对同一次委托结果的区间聚合（新增/取消/日均/净增/累计增长率口径不变）。
- **破坏性**：`GetTrendAsync` 返回类型变更 = **REST 响应体形状变化**（entity 9 字段 → DTO 5 字段，字段名亦变化：`SubscribeCount`→`NewFollows`/`UnsubscribeCount`→`Unsubscribes`/`NetCount`→`NetGrowth`/`StatDate`→`Date`）→ **SG3 客户端 .ts 代理 + DTO 重生成** + DMP-Lite 前端字段引用适配；REST 路由路径不变。
- 统计口径与事件日志一致（同一事实源，订阅统计不再是独立写入的「另一份真相」）。

---

## 破坏性变更

| # | 变更 | 级别 | 消费方适配 |
|---|------|------|-----------|
| 1 | `ISsoChannel.cs` 迁出 `TrustCenter.Abstractions`（类型移除） | 破坏（类型） | 改引 `Federation.Abstractions` + `using TKWF.Ext.Federation`（跨程序集同 namespace，零 using 迁移语义，仅 csproj 改引/删除旧 using） |
| 2 | `SsoChannelEventContext` → `ChannelInboundContext` | 破坏（改名） | 引用处替换；参数只放协议原始字段 |
| 3 | `ISsoChannel.HandleEventAsync` 返回 `ChannelEventOutcome` | 破坏（签名） | 事件通道实现重写解析段（微信首批；钉钉仅引用迁移，解析实现本轮移出） |
| 4 | `IEventReplyHandler.HandleAsync(ChannelEventDispatch)` | 破坏（签名） | 3 微信 handler + `WeChatOperations` handler 经 `WechatEventContext.FromChannelEvent` 一次性适配（14 个 `OnXxxAsync` 零改动） |
| 5 | `IEventOrchestrator.OrchestrateAsync(ChannelInboundContext)` | 破坏（签名） | 装配层 + 测试 |
| 6 | `WeChatOperations` 删 `SubscriptionStatEntity` + `GetTrendAsync` 返回类型变更 | 破坏（类型 + REST 响应体形状） | SG3 客户端重生成 + DMP-Lite 前端字段适配（REST 路径不变） |
| 7 | `IChannelSource.Capabilities` | 非破坏（DIM，默认 `None`） | 建议平台库显式声明；未声明 = fail-closed（关注域静默不记录） |
| 8 | 新配置节 `TKWF:Federation:LoginEntry` | 非破坏（新增节，默认值兜底） | 无 |

## 生命周期

- 本 ADR 是**永久记录**（AGENTS §4）：即便后续决策被推翻，也须在本文件标注「已废弃」并引用新 ADR，不得删除原文件。
- `Attributes` 键清单在 §决策 1 固化为契约，键名变更/新增平台须先更新本文件（或另立 ADR），不可仅记在使用指南。
- R3 附条件（决策 4）与「根治方向 = 框架 `RequiresNew`/独立连接」为已知代价登记点，主导语义变更时须先回读本 ADR。

---

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-10-20 | 初稿（随 `I2-渠道关注关系域-开发方案.md` v0.5.1 评审定稿；Oracle3 `APPROVE WITH CONDITIONS` 条件 P0-1/P1-1/P1-2/P1-3/P2-2 已全部吸收于方案，本 ADR 记录定型决策） |

<!-- EOF -->