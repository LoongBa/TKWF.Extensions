# TKWF.Federation.WeChat.MessageGateway 微信消息网关核心判定运行库技术规范

**状态**: 运行库 (Runtime Library——纯逻辑零实体零持久化，用户三部分分层裁定 ①) | **版本**: V0.1.1（2026-10-18——四组件全量落地 + matcher MsgType 多值适配） | **框架**: .NET 10 | **依赖**: `TKWF.Federation.WeChat`（协议类型 `WechatEventContext`/`WechatReplyPayload` 单向依赖）+ 纯 BCL（`System.Threading`）

**核心约束**: 运行库判据（AGENTS §8：`TKWF.*`=库——纯逻辑/无状态）/ **零实体零持久化零 DB**（无 FreeSql 无 `IEntityDAC`——熔断状态三态纯内存，进程重启即清零）/ **零 DI 零装配**（无 Initializer 无 `[TKWFExtension]` 无 Options 绑定）/ **零 AspNetCore**（端点归扩展/消费方装配层）/ **扩展实体零耦合**（扩展把 `MessageRouteRuleEntity` 投影为 descriptor 传入，运行库不引用扩展实体）/ **只决策不执行**（转发/渲染/落库归扩展——降级链为决策步进 `ForwardDegradeStage`）

> ✅ **实施进度（2026-10-18）**：V0.1.0 **全部落地**——`WeChatMessageRouteRuleMatcher`（路由规则判定）+ `WeChatMessageIdempotencyKeyBuilder`（幂等键规范化）+ `WeChatMessageForwardDecider`（转发决策 + 降级链步进）+ `WeChatCircuitBreaker`（三态熔断状态机）+ 常量类（`WeChatMessageTypes`/`WeChatMessageMatchModes`/`WeChatMessageForwardModes`/`WeChatEndpointAuthTypes`）+ 支撑模型（`MessageRouteRuleDescriptor`/`RouteRuleCriteria`/`ForwardDecision`/`ForwardAction`/`ForwardDegradeStage`/`CircuitBreakerStateInfo`）+ **测试 57 用例全绿**（matcher 25 / keyBuilder 8 / forwardDecider 12 / circuitBreaker 12——路由判定全分支/幂等键双形态/转发决策与降级链步进/三态熔断状态机）+ slnx 已接线；**V0.1.1（2026-10-18）matcher MsgType 多值适配**——`WeChatMessageRouteRuleMatcher.Matches` 的 MsgType 匹配升级为**逗号分隔多值任一命中**（`"text,image"` 任一命中——`Split(',', TrimEntries|RemoveEmptyEntries)` + `OrdinalIgnoreCase`；空=全类型通配）——替代 P1-5 单值方案（领域扩展 V0.3.0 P1 二期对齐 ① 消息类型多选）；**测试 52→57**（+5 多值用例：任一命中 / 非含值不命中 / 值 trim+大小写不敏感——matcher 25）。

---

## 一、定位

微信公众号消息网关**核心判定运行库**（用户三部分分层裁定 ①）——把消息网关的**纯逻辑判定**从领域扩展中提炼为独立运行库（对齐 DataPort/Tagging 运行库提炼先例）：输入路由规则投影 + 消息特征，产出命中规则 / 规范化幂等键 / 转发决策与降级链阶段 / 熔断放行判定，**不执行转发/渲染/持久化**——执行与装配归领域扩展与消费方。

| 分层 | 归属 | 说明 |
|------|------|------|
| **① 核心判定运行库** | **本库（`TKWF.Federation.WeChat.MessageGateway`）** | 纯逻辑零实体零持久化零 DI——路由规则判定 / 幂等键规范化 / 转发决策与降级链 / 熔断状态机 |
| **② 领域扩展（装配）** | `TKWF.Ext.WeChatOperations` V0.3.0 | 数据底座 + 门面编排 + Web 端点——消费运行库判定，执行转发/渲染/落库 |
| **③ 消费方** | 业务系统 | 白名单启用 + `User.Use<门面>()` |

**被消费**：`TKWF.Ext.WeChatOperations` V0.3.0（领域扩展 ② 装配——`MessageGatewayService` 门面编排：路由判定/幂等键/转发决策与降级链/熔断状态机落地）。

**不包含**：实体/持久化（运行库判据——零 FreeSql 零 `IEntityDAC`）；转发/渲染执行（决策步进由扩展/消费方按 `ForwardDegradeStage` 执行）；DI/装配（无 Initializer 无 `[TKWFExtension]` 无 Options 绑定）；端点映射（归扩展/消费方装配层）。

## 二、组件清单

| 组件 | 形态 | 说明 |
|------|------|------|
| `WeChatMessageRouteRuleMatcher` | 静态路由规则判定 | `Match(rules, criteria)`——按 `SortOrder` 升序取首个命中（输入顺序无关，排序语义由运行库持有）；`Matches(rule, criteria)` 单条判定：IsEnabled=false 跳过 / ChannelId 过滤 / MsgType 空=全类型通配 or **逗号分隔多值任一命中**（`"text,image"` 任一命中——值 trim + `OrdinalIgnoreCase`，V0.1.1 消息类型多选）/ **Event 仅 `MsgType=event` 时参与**（规则 Event 空=全事件通配）/ MatchMode 关键词（None 忽略 TriggerKey 恒命中 / Exact `TriggerKey==content` / Contains 内容包含；**未知模式 fail-safe 不命中**）；无命中返回 null |
| `WeChatMessageIdempotencyKeyBuilder` | 静态幂等键规范化 | `BuildKey(channelId, msgId, fromUserName, createTime)`——MsgId 非空 → `msg:{channelId}:{msgId}`（消息形态）；MsgId 空 → `evt:{channelId}:{fromUserName}:{createTime}`（事件形态——FromUserName+CreateTime 键来源，原样留存不做时区转换）；**前缀类型隔离**（msg:/evt: 永不互相碰撞）+ **ChannelId 入键**（跨通道独立去重）；纯字符串组合不可变纯函数 |
| `WeChatMessageForwardDecider` | 静态转发决策器 | `Decide(rule, syncSucceeded)`——LocalOnly/AsyncCopy（同步结果不参与）/ SyncProxy/PreprocessSync（成功→透传 / 失败→`LocalFallback` 降级链起步）/ **未知模式 fail-safe 降级本地兜底**（不阻断回复）；`NextDegradeStage(stage)` 降级链**决策步进**：LocalRender→Template→DefaultReplyText→None（链尽）——**只步进阶段不执行渲染** |
| `WeChatCircuitBreaker` | 熔断状态机（实例类） | 三态机：关→开→半开→关；每 `EndpointId` 独立状态（`ConcurrentDictionary` 隔离，锁粒度=端点互不阻塞）；`RecordFailure` 关态连续失败 ≥ `FailureThreshold`（默认 3）→ 开闸（开窗期=本次+`OpenWindow`）/ 半开探测失败→重置开窗（回开态）/ 开窗期持续失败不延长窗口；`RecordSuccess` 任意状态回关 + 计数清零；`TryCall` 关放行 / 开窗期内拒绝（默认 60s）/ 窗口期满放行**单次半开探测**（转 HalfOpen）/ 半开期间拒绝（单次探测唯一性）；`GetState` 状态快照（`CircuitBreakerStateInfo`——诊断/测试断言）；**进程重启即清零**（无持久化），多实例各计数为已知限制（方案 P2-1 显式声明） |
| 常量类 | 协议/枚举常量 | `WeChatMessageTypes`（event/text/image/voice/video/shortvideo/location/link 小写协议串）/ `WeChatMessageMatchModes`（None=0/Exact=1/Contains=2）/ `WeChatMessageForwardModes`（LocalOnly=1/AsyncCopy=2/SyncProxy=3/PreprocessSync=4）/ `WeChatEndpointAuthTypes`（None=0/Bearer=1/Header=2） |
| 支撑模型 | record 数据契约 | `MessageRouteRuleDescriptor`（Id/ChannelId/MsgType/Event/MatchMode/TriggerKey/ForwardMode/EndpointId/SortOrder/IsEnabled——扩展把 `MessageRouteRuleEntity` 投影传入）/ `RouteRuleCriteria`（ChannelId/MsgType/Event/Content）/ `ForwardDecision`（Action/DegradeStage/EndpointId + `IsDegraded`）/ `ForwardAction`（LocalOnly/AsyncCopy/SyncProxy/PreprocessSync/LocalFallback）/ `ForwardDegradeStage`（None/LocalRender/Template/DefaultReplyText）/ `CircuitBreakerStateInfo`（State/ConsecutiveFailures/OpenUntilUtc） |

## 三、安全边界

- **数据访问红线合规**：零持久化零 Store（运行库判据）——熔断状态纯内存三态，进程重启即清零；多实例各计数为已知限制（方案 P2-1 显式声明）。
- **扩展实体零耦合**：输入为 descriptor/record 投影（`MessageRouteRuleDescriptor`/`RouteRuleCriteria`），不引用扩展实体——运行库可独立测试/独立演进，扩展实体变更不影响运行库。
- **只决策不执行**：转发/渲染/落库归扩展/消费方——运行库无任何副作用（无 HttpClient/无 DB/无文件 IO）。
- **判定 fail-safe**：未知 MatchMode/ForwardMode 一律不命中/降级本地兜底——不静默全量放行、不阻断回复。

## 四、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-18） | 四组件全量落地（用户三部分分层裁定 ①——核心判定运行库）——`WeChatMessageRouteRuleMatcher`（路由规则判定）/ `WeChatMessageIdempotencyKeyBuilder`（幂等键规范化）/ `WeChatMessageForwardDecider`（转发决策 + 降级链步进）/ `WeChatCircuitBreaker`（三态熔断状态机）+ 常量类 + 支撑模型；纯 BCL 零第三方依赖运行库；测试 **52 用例全绿**（matcher 20 / keyBuilder 8 / forwardDecider 12 / circuitBreaker 12）+ slnx 已接线 |
| V0.1.1（2026-10-18） | matcher MsgType **多值适配**（领域扩展 V0.3.0 P1 二期对齐 ①——消息类型多选，替代 P1-5 单值方案）：`WeChatMessageRouteRuleMatcher.Matches` 的 MsgType 匹配升级为**逗号分隔多值任一命中**（`"text,image"` 任一命中——`Split(',', TrimEntries\|RemoveEmptyEntries)` + `OrdinalIgnoreCase`；空=全类型通配）；测试 **52→57 用例全绿**（matcher 25——+5 多值用例：任一命中 / 非含值不命中 / 值 trim+大小写不敏感） |

<!-- EOF -->
