# TKWF.Ext.WeChatOperations 微信公众号运营扩展技术规范

**状态**: 业务扩展（实体型——SG1 数据底座 + 门面 + 事件回复 + 消息网关 + **`[GenerateController]` 生成端点**） | **版本**: V0.4.0（2026-10-18——设计缺口修复：客户端 DTO 生成链路根治 + 网关 REST 端点 + 总开关 DB 化） | **框架**: .NET 10 | **依赖**: `TKWF.Federation.WeChat`（平台库——通讯走库，单向依赖）+ `TKWF.Federation.WeChat.MessageGateway`（运行库——路由判定/幂等键/转发决策/熔断三态）+ `TKWF.Ext.FeatureManagement`（V0.4.0——消息业务总开关 DB 化）+ `TKWF.BackgroundJobs`（异步抄送）+ `TKWF.Domain` + FreeSql

**核心约束**: 门面继承 `DomainServiceBase` + `AddConstructibleService` 注册（ADR90 正确路线）/ DataService 经 `User.Use<T>()` 懒加载（DI004 零豁免）/ **禁手写 Store**（数据访问红线——SG1 生成 DataService 已含全部 CRUD 原子方法）/ **凭证 AES-GCM 密文落库**（keyed `ISymmetricKeyProvider`——明文不入库不进日志）/ **不走动态加载**（回复 handler 全部编译期静态注册，DB 只存规则数据不存类型名）/ **两源并存**（DB 权威 + Options 降级——DB 命中解密返回，未命中回退 `WeChatOptions.Channels`）/ **事件回复内部组合 template 兜底**（无 DB 规则 → `Channel.Extra["news_reply"]`，不依赖编排器迭代顺序——Oracle P0 选项 A）/ **V0.4.0 生成端点**（实现类 public + `[GenerateController]` + `[ApiExpose]` 锁路由——不手写 minimal API；wire DTO 收凭证明文 body 传输）/ **总开关双源**（Options 启动默认 + FeatureManagement DB 权威——运行时切换走 `IFeatureManager`）

> ✅ **实施进度（2026-10-18）**：V0.1.0 **全部落地**（方案 `docs/WeChatOperations/微信公众号运营扩展-开发方案.md` Oracle PASS WITH CONDITIONS——P0×1/P1×7 吸收）——8 实体 SG1 数据底座 + xCodeGen 生成物入库 + **6 门面**（`IChannelConfigService`/`IMenuService`/`IReplyRuleService`/`IMaterialService`/`ISubscriptionStatService`/`ILogQueryService`）+ **`WeChatOperationsReplyHandler`**（继承平台基类二级分发 14 分支——欢迎语/关键词/Click 映射/全消息类型/Scan/统计 + 内部 template 兜底）+ **`DbWeChatCredentialResolver`**（DB 命中解密 + Options 降级两源并存）+ **`DbWeChatChannelSource`**（Insert(0) 前置——DB 源优先组合语义）+ **`WeChatOperationsWebExtension`**（13 端点 + 配置分层）+ **测试 67 用例全绿**（handler 全场景/凭证密文/两源并存/门面/Initializer 形态）。

> ✅ **实施进度（2026-10-18）**：V0.4.0 **设计缺口修复（DMP-Lite 转达四项裁定）全部落地**——① **客户端 DTO 生成链路根治（P0）**：8 门面实现类 public 化 + `[GenerateController]` + 接口方法 `[ApiExpose(RestRoute/RestMethod/ExposeRest)]` 锁 `/api/wechat-ops/*` 路由——消费方 SG1b 自动生成 REST 端点 + **SG3 客户端 DTO/代理**（对齐 IdentityAuthService/FeatureManagementApiService 先例；**手写 `WeChatOperationsWebExtension` minimal API 与 `WeChatOperationsEndpointOptions` 删除**——V0.4.0 破坏性，路由编译期常量）；**安全 wire DTO 化（Oracle P1 凭证安全）**：`SaveChannelRequest`/`SaveEndpointRequest`/`SetGatewayEnabledRequest`（凭证明文经 [FromBody] body 传输防 URL 泄漏——原 4 参实体签名多复杂参数会走 query）；非暴露方法 `[ApiExpose(ExposeRest=false)]`（GetAsync/DeleteAsync/GetSnapshotAsync/GetClickKeysAsync/DeleteRuleAsync/GetWelcomeAsync/GetDefaultAsync/DeleteAsync/GetReferencesAsync/RecordAsync/RouteAsync 等——对齐既有 15 端点映射面不扩暴露面）；② **网关 REST 端点补全（P1）**：`/gateway/*` 9 端点（`GET/POST /gateway/rules` + `DELETE /gateway/rules/{ruleId}` + `GET/POST /gateway/endpoints` + `DELETE /gateway/endpoints/{id}` + `GET /gateway/status` + `POST /gateway/enabled`（`SetGatewayEnabledRequest` 进程内覆盖）+ `GET /gateway/forward-logs`（新增 `ListForwardLogsAsync`——转发日志查询，V0.2.0 起网关页面唯缺对账列表））；`RouteAsync` 不暴露（事件路径内部入口）；③ **消息业务总开关 DB 化（P1）**：`WeChatOperationsFeatureDefinitionContributor`（`IFeatureDefinitionContributor` 声明 `WeChatOperations.MessageBusiness` Boolean DefaultValue=true）+ `WeChatOperationsFeatureNames` 常量 + `WeChatOperationsReplyHandler` 10 处读法改 `IsMessageBusinessEnabledAsync`（**双源语义**：Options `MessageBusinessEnabled` 启动期显式 false → 关闭（V0.2.1 配置语义保留）；默认 true → FeatureManager `GetValueAsync(name, user, "true")` DB 权威——`SetValueAsync` 写 DB 运行时即切换，未存值默认开；⚠️ 勿用 `IsEnabledAsync`——对未定义/未存值恒 fail-closed false 不读 DefaultValue）+ `[TKWFExtensionDependency]` 补声明（`IFeatureManager` + Federation 三契约）+ `<ImplicitUsings>enable</ImplicitUsings>`（**主框架 SG1 `BuildContributorsSource` 生成 `GeneratedContributors.g.cs` 文件头缺 `using System.Collections.Generic`——模板假设宿主开 ImplicitUsings，本扩展新增 Feature Contributor 后首次触发；已临时开 ImplicitUsings 修复，模板缺陷待报框架组**，同 EntityEmpty/DtoEmpty 缺 using 先例）；**测试 143 用例全绿**（WeChatOperations 143/2跳过 + 受影响面 Federation.WeChat 100 / MessageGateway 57 / FeatureManagement 135 / Federation 79 / TrustCenter 35——全量零回归）。

> ✅ **实施进度（2026-10-18）**：V0.2.0 **消息路由转发网关模块全部落地**（方案 §十 Oracle PASS）——**4 新实体**（`MessageRouteRuleEntity`/`DownstreamEndpointEntity`/`MessageForwardLogEntity`/`WeChatMessageIdempotencyEntity`——路由规则/下游端点/转发流水/幂等四张表）+ **2 新门面**（`IMessageGatewayService` 转发编排核心 `RouteAsync`——幂等最前短路 → 运行库匹配 → 转发决策 → 异步抄送/降级链；`IMessageForwardDispatcher` BackgroundJob 异步抄送入队）+ **handler 挂载 11 分支**（幂等最前短路 + 默认开关关零回归）+ **配置分层**（`TKWF:WeChatOperations:Gateway` 独立子节 fail-closed 默认关）+ **运行库 `TKWF.Federation.WeChat.MessageGateway`**（路由判定/幂等键规范化/转发决策与降级链/熔断三态——纯逻辑零持久化零第三方）+ **异步抄送作业 `WeChatMessageForwardJob`**（SystemActor 帧投递 + 转发日志状态回写）；SyncProxy（同步透传）本期暂缓（P0-1 平台库 RawXml 就绪后补——命中经 decider 降级 LocalFallback 本地渲染链）；**测试 67→110 用例全绿**（+43 网关测试：路由匹配/幂等重推/并发建键/Pending 超时复用/入队 fail-safe 降级/决策分支/端点密文 DTO 投影/引用保护）。

> ✅ **实施进度（2026-10-18）**：V0.3.1 **P2 对齐迭代（DMP-Lite 剩余缺口四项）全部落地**——① **状态卡聚合门面（DMP-Lite 5.8 系统状态四卡）**：新门面 `IWeChatOperationsStatusService`（impl `WeChatOperationsStatusService : DomainServiceBase` + `[DiContractIgnore]` + `AddConstructibleService` 注册——`GetStatusSummaryAsync(channelId)` 返回 `WeChatOperationsStatusDto`（Connected/ConnectedDetail/TodayMessageCount/AutoReplyHitRate/ForwardSuccessRate）；当日口径 = UTC 自然日半开区间 [UtcNow.Date, +1day)；连接 = channel 配置存在（`IChannelConfigService.ListAsync` 命中）+ 凭证可解析（HasCredentials）；命中率 = ReplyContent 非空 或 ReplyMaterialId 非空 ÷ 当日总量；转发成功率 = Success ÷ (Success+Fail+Timeout)（Queued/SkippedDuplicate/NoRule 双端排除）；DTO 零凭证安全）+ Web 端点 `GET {prefix}/channels/{channelId}/status`（`StatusEndpointEnabled` 开关，端点 13→15）；② **匹配过程明细（DMP-Lite 5.6 §4 单条详情「匹配过程」灰框逐行推演——R2 每条消息只返回一条回复）**：`InteractionLogEntity` 增可空 `MatchTraceJson`（Column Position=11 after DurationMs）——`WeChatOperationsReplyHandler` 经 `List<MatchTraceStep>`（private record: RuleId/RuleName/SceneType/MatchMode/TriggerKey/Hit/Reason）逐条推演采集（OnTextAsync 链：① Keyword Exact → ② Contains 逐候选 → ③ Default → ④ template → DefaultReplyText；TryReplyBySceneAsync：场景规则命中/template 兜底），`RecordInteractionAsync` best-effort 序列化（PascalCase——消费方前端逐行渲染"为什么命中/未命中"）；不改回复决策语义（旁路采集）；③ **统计卡聚合（DMP-Lite 5.7 §2 统计数据四卡）**：`ISubscriptionStatService.GetSummaryAsync(channelId, from, to, source?)` → `SubscriptionStatSummaryDto`（TotalSubscribe/TotalUnsubscribe/NetCount（R7 负值钳 0）+ NetCountRaw + IsNetNegative/DailyAverageSubscribe(unsubscribe)（÷含首尾天数）/DayCount/GrowthRate（区间净增 ÷ 期初累计净增 <.from.Date，负基数/null 不可判定 → GrowthRateUndeterminable））+ Web 端点 `GET {prefix}/channels/{channelId}/subscription-stats/summary`（from/to/source 查询参数）；④ **超时单位统一（DMP-Lite 5.5"超时毫秒" vs 领域秒契约）**：`DownstreamEndpointDto` 增派生只读成员 `TimeoutMs => TimeoutSeconds * 1000`（非存储列，positional arity 不变——前端展示/提交 ms、服务端按秒契约存储 TimeoutSeconds 1..3）；**测试 130→143 用例全绿**（+13：MatchTrace 6 / Status 4 / Summary 3）；运行库 57 / 平台库 100 不变。

> ✅ **实施进度（2026-10-18）**：V0.3.0 **P1 二期对齐（DMP-Lite 四项）全部落地**——① **消息类型多选（DMP-Lite 5.5）**：`MessageRouteRuleEntity.MsgType` 逗号分隔多值（"text,image,event" 命中任一——运行库 matcher `Split(',')` + OrdinalIgnoreCase 多值适配，空 = 全类型通配）；② **来源/场景维度（DMP-Lite 5.7）**：`SubscriptionStatEntity` 增 `Source`（唯一约束扩 (ChannelId, StatDate, Source)——汇总行 Source=null + 各来源行；`RecordAsync`/`GetTrendAsync` 增 source 参数；`WeChatSubscriptionSources` 常量 search/scan/qr_code/card/share/miniprogram）；③ **交互日志耗时（DMP-Lite 5.6）**：`InteractionLogEntity` 增 `DurationMs`（handler `Stopwatch` 自入口起计时、记录交互日志时止——日志详情性能指标）；④ **SyncProxy/PreprocessSync 真透传落地（DMP-Lite 5.5 策略②③——方案 §十·二 P0-1 选 A：平台库 RawXml 逃逸口就绪后补）**：`RouteSyncProxyAsync` 出站透传（`DownstreamForwardClient.SendRawXmlAsync`——`__decrypted_xml` 明文 XML 原样 POST 下游 → 下游完整响应 XML 经 `WechatReplyPayload.RawXml` **原样接管回复**（防双重信封）；2xx + `IsValidReplyXml` 合法 `<xml>` 判定）+ 运行库 `WeChatCircuitBreaker` 熔断（Initializer `AddSingleton` 单例接线——开闸期直接 LocalFallback 不转发；端点 `TimeoutSeconds` 硬超时 linked CTS）+ **Timeout/Fail 降级链**（超时/失败/非 XML/HTTP 非 2xx → 转发日志 Timeout/Fail（ErrorMsg 脱敏）→ decider `syncSucceeded=false` LocalFallback 本地渲染链）+ **幂等联动**（透传成功/失败均更新幂等表 Forwarded——键存在跳过不重复透传）+ **`DownstreamForwardClient` 出站原语补齐**（`SendRawXmlAsync` 返回完整下游 XML + TimedOut 区分；修复 StringContent mediaType 带 charset 参数 FormatException——AsyncCopy 作业未直测 HTTP 未暴露，SyncProxy 首暴露）；PreprocessSync 本期与 SyncProxy 同语义（透传+降级），前置处理差异归后续迭代；**测试 121→130 用例全绿**（+9：SyncProxy 真透传集成/多选 matcher/source 维度/DurationMs；运行库 57 测试含多值 5）。

> ✅ **实施进度（2026-10-18）**：V0.2.1 **DMP-Lite 接口对齐迭代全部落地**（P0 缺口四项）——**`MessageRouteRuleEntity` 增 `Name`**（规则显示名——DMP-Lite 5.5 网关规则列表展示）+ **`IMaterialService.GetReferencesAsync`**（素材引用反查——跨 ReplyRule 聚合 `MaterialReferenceDto` 清单含 SceneType 语义 + 规则 Id，DMP-Lite 5.2 "引用 N 处"）+ **`IMenuService.GetClickKeysAsync`**（菜单树递归收集 click 类型按钮 Key——DMP-Lite 5.4 自动生成 Click 映射行；快照空/解析失败 → 空列表）+ **`WeChatOperationsOptions.MessageBusinessEnabled`**（消息业务总开关默认 `true`——关闭后关键词/欢迎语/兜底等本地回复全部停发统一返回维护提示 `DefaultReplyText`；**转发网关不受控**（网关 `RouteAsync` 先行仍转发生效）；DMP-Lite 5.8）；**测试 110→121 用例全绿**（+10：引用反查 3/click key 3/总开关 4 + Name 校验 1——V0.2.0 111 含 Name 校验，V0.2.1 累计 121）。

---

## 一、定位

微信公众号**运营能力扩展**——为消费方提供公众号运营所需的数据底座与业务门面：

| 能力 | 说明 |
|------|------|
| **公众号渠道配置** | DB 权威（凭证 AES-GCM 密文）+ Options 降级两源并存；多公众号按 ChannelId 管理 |
| **素材库** | 文本/图文素材（图文四件套 Title/Description/Url/PicUrl），全局共享规则引用；**引用反查清单**（跨回复规则聚合引用位置——SceneType 语义/规则 Id/TriggerKey，DMP-Lite 5.2） |
| **回复规则** | 通用 `ReplyRuleEntity`（SceneType 全覆盖——Subscribe/Keyword/Click/Image/Voice/Video/ShortVideo/Location/Link/Scan/Default）；**网关路由规则消息类型多选（V0.3.0——`MessageRouteRuleEntity.MsgType` 逗号分隔多值 "text,image,event" 命中任一，空 = 全类型通配）** |
| **菜单管理** | 拉取/发布/快照/版本/回滚（两阶段底层能力；Click 映射独立于菜单结构——改映射不发布菜单）+ **click key 收集**（菜单树递归收集 click 类型按钮 Key——DMP-Lite 5.4 自动生成映射行） |
| **订阅统计** | subscribe/unsubscribe 事件驱动按 channel 按日按来源 upsert（唯一约束 (ChannelId, StatDate, Source)——汇总行 Source=null + search/scan/qr_code/card/share/miniprogram 来源行，V0.3.0 来源/场景维度）+ 趋势查询（按来源过滤）+ **统计卡聚合 `GetSummaryAsync`（V0.3.1——DMP-Lite 5.7 §2 统计数据四卡：TotalSubscribe/TotalUnsubscribe/NetCount（R7 负值钳 0）/DailyAverageSubscribe(unsubscribe)/GrowthRate）** |
| **日志** | 交互日志（OpenId/时间/类型/内容/触发类型/回复/**处理耗时 DurationMs（V0.3.0——handler Stopwatch 计时）**/**匹配过程 MatchTraceJson（V0.3.1——回复链路逐条推演采集，日志详情展示"为什么命中/未命中"）**）+ 发布审计（操作/时间/动作） |
| **事件回复** | 内部组合 template 兜底——无 DB 规则时 `Channel.Extra["news_reply"]` 模板兜底（平台库 V0.4.1 多图文） |
| **消息业务总开关** | `MessageBusinessEnabled`（默认 `true`——关闭后关键词/欢迎语/兜底等本地回复全部停发统一返回维护提示 `DefaultReplyText`；**转发网关不受控**（网关 `RouteAsync` 先行仍转发生效）；系统状态模块语义——全局业务启停，非单个回复/规则开关，DMP-Lite 5.8）+ **状态卡聚合门面 `IWeChatOperationsStatusService`（V0.3.1——DMP-Lite 5.8 系统状态四卡：Connected/TodayMessageCount/AutoReplyHitRate/ForwardSuccessRate，UTC 当日口径 + DTO 零凭证）** |
| **消息转发网关** | 消息路由规则（ChannelId+MsgType+Event+MatchMode+TriggerKey 五键匹配，同键覆盖更新）+ 下游端点（AuthToken AES-GCM 密文落库 + DTO 投影无密文）+ 消息幂等（规范化键防微信重推——幂等检查最前短路）+ 转发日志全链留痕（Queued/Success/Fail/Timeout/SkippedDuplicate/NoRule）+ BackgroundJob 异步抄送（at-least-once + 指数退避重试，幂等键透传下游自去重）+ **SyncProxy/PreprocessSync 同步透传（V0.3.0——出站透传 → 下游 XML RawXml 逃逸口原样接管回复；超时/失败/非 XML 经运行库熔断 + 转发日志 Timeout/Fail → 降级本地渲染链）** |

**GUI（阶段二）**：本扩展不含 GUI——菜单编辑器/素材管理/规则配置页面由 **DMP-Lite 消费方自研**（R2 路径直连 REST），实证后拆回本仓库为 `WeChatOperations.UI` 扩展（见方案 §八）。

## 二、安装与接线

### 1. 消费方引用

```xml
<ProjectReference Include="..\_TKWF.Extensions\_Framework\WeChatOperations\TKWF.Ext.WeChatOperations.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.WeChat\TKWF.Federation.WeChat.csproj" />
```

### 2. 启用（白名单声明）

```csharp
[TKWFEnabledExtension(typeof(WeChatOperationsExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

启用后三钩子自动接线：9 门面（7 运营 + 2 网关）`AddConstructibleService` + handler `TryAddEnumerableConstructible` + keyed `ISymmetricKeyProvider` + `DbWeChatCredentialResolver` + `DbWeChatChannelSource` + 网关装配（`DownstreamForwardClient` typed client + `WeChatMessageForwardJob` 作业注册 + 系统用户桥）。

> ⚠️ **异步抄送前置**：网关 AsyncCopy 分支经 `IBackgroundJobManager` 持久化队列投递（TKWF_JobRecord 重启续投 + 重试 MaxRetryCount + SystemActor 帧自动绑定）——消费方须在领域初始化器装配 **`AddBackgroundJobs<TUserInfo>()`**。缺接线时入队失败经网关 fail-safe 降级（本地回复不受影响不 500，转发日志 Fail 行留痕可对账）。

### 3. 配置 `TKWF:WeChatOperations`

```jsonc
{
  "TKWF": {
    "WeChatOperations": {
      "DefaultReplyText": "感谢您的关注，请稍后再试。",   // 未命中关键词默认兜底文案
      "SecretEncryptionKeyPath": "<AES-GCM 密钥文件路径>", // 生产必配（缺钥 fail-fast）；开发随机兜底
      "IsProduction": true,                             // 生产 true / 开发 false
      "Gateway": {                                      // 消息路由转发网关（配置分层——领域安全/转发决策归本节，Web 层无对应开关）
        "GatewayEnabled": false,                        // 网关全局开关——默认 false（fail-closed 默认关，未启用时 handler 零回归不查库不转发）
        "IdempotencyRetentionDays": 7,                  // 幂等保留天数（覆盖微信全部重试窗口 + 对账余量；0/负数 = 不清理；仅清理终态记录）
        "PendingTimeoutMinutes": 5,                     // 幂等 Pending 超时分钟（超时后允许新请求复用键重建 Pending+转发——防陈旧 Pending 数据丢失窗口）
        "AsyncForwardRetryCount": 3,                    // 异步抄送投递重试次数（BackgroundJob 队列 MaxRetryCount；下游须幂等——IdempotencyKey 透传自去重）
        "BackoffSeconds": 2,                            // 异步抄送失败退避秒数（指数退避 2^RetryCount 秒基础值）
        "DefaultSyncTimeoutSeconds": 2                  // 同步透传默认超时秒数（上限强制 ≤3 防运维误配吞兜底窗口；V0.3.0 SyncProxy 落地——端点 TimeoutSeconds 缺省回落本节）
      },
      "Web": {                                          // 表现层暴露面（配置分层）
        "RoutePrefix": "/api/wechat-ops"
      }
    }
  }
}
```

**凭证来源**（两源并存）：① `WeChatChannelConfigEntity`（DB——AES-GCM 密文，权威）；② `WeChatOptions.Channels`（`TKWF:Federation:WeChat` 节——降级）。DB 命中解密返回，未命中回退 Options。**下游端点鉴权令牌**（`DownstreamEndpointEntity.AuthTokenCipher`）同为 AES-GCM 密文落库——明文仅出站瞬时。

### 4. 使用门面

```csharp
// 消费方（DomainUser 帧内）
var menuService = user.Use<IMenuService>();
await menuService.PublishAsync("wechat-mp-main", menuJson, ct);   // 发布菜单（快照+版本+审计）

var ruleService = user.Use<IReplyRuleService>();
await ruleService.SaveRuleAsync(new ReplyRuleEntity { ChannelId = "wechat-mp-main", SceneType = 2, TriggerKey = "优惠", ... }, ct);

// 状态卡聚合（V0.3.1——DMP-Lite 5.8 系统状态四卡；统计卡聚合同型：user.Use<ISubscriptionStatService>().GetSummaryAsync(channelId, from, to, source, ct)）
var statusService = user.Use<IWeChatOperationsStatusService>();
var status = await statusService.GetStatusSummaryAsync("wechat-mp-main", ct);   // Connected/TodayMessageCount/AutoReplyHitRate/ForwardSuccessRate
```

## 三、组件清单

| 组件 | 形态 | 状态 |
|------|------|------|
| 12 实体（8 既有 + 4 网关新增：`MessageRouteRuleEntity` 路由规则/`DownstreamEndpointEntity` 下游端点/`MessageForwardLogEntity` 转发流水/`WeChatMessageIdempotencyEntity` 幂等键） | SG1 声明式 + xCodeGen 生成物入库 | ✅ |
| `WeChatOperationsExtensionInitializer<TUserInfo>` | `[TKWFExtension]` 三钩子——9 门面/handler/keyed 密钥/两源并存/网关装配 | ✅ |
| `IChannelConfigService` | 公众号配置 CRUD（凭证加密落库 + DTO 投影无凭证） | ✅ |
| `IMenuService` | 拉取/发布（事务：快照+版本+审计）/回滚/删除 + **click key 收集 `GetClickKeysAsync`**（菜单树递归收集 click 类型按钮 Key——快照空/解析失败 → 空列表） | ✅ |
| `IReplyRuleService` | 全场景规则 CRUD（欢迎语/关键词/Click/消息类型/Scan/Default） | ✅ |
| `IMaterialService` | 素材 CRUD（被规则引用禁止删除）+ **引用反查 `GetReferencesAsync`**（跨 ReplyRule 聚合 `MaterialReferenceDto` 清单——SceneType/规则 Id/TriggerKey） | ✅ |
| `ISubscriptionStatService` | 每日 upsert 计数 + 趋势（DateTime 半开区间查询——规避 SQLite 等值往返问题）+ **来源/场景维度（V0.3.0——`RecordAsync`/`GetTrendAsync` 增 source 参数，`WeChatSubscriptionSources` 常量 search/scan/qr_code/card/share/miniprogram，空 = 汇总行）** + **统计卡聚合 `GetSummaryAsync`（V0.3.1——`SubscriptionStatSummaryDto`：TotalSubscribe/TotalUnsubscribe/NetCount（R7 负值钳 0）+ NetCountRaw + IsNetNegative/DailyAverageSubscribe(unsubscribe)（÷含首尾天数）/DayCount/GrowthRate（期初累计净增口径，负基数/null → GrowthRateUndeterminable））** | ✅ |
| `ILogQueryService` | 交互日志/发布审计查询（交互日志含**处理耗时 `DurationMs`**（V0.3.0——handler Stopwatch 计时）+ **匹配过程 `MatchTraceJson`**（V0.3.1——回复链路逐条推演采集，日志详情展示"为什么命中/未命中"）） | ✅ |
| `IWeChatOperationsStatusService` | **状态卡聚合门面（V0.3.1 新门面——DMP-Lite 5.8 系统状态四卡）**——`GetStatusSummaryAsync(channelId)` → `WeChatOperationsStatusDto`（Connected/ConnectedDetail/TodayMessageCount/AutoReplyHitRate/ForwardSuccessRate；UTC 当日半开区间口径 + 连接=配置存在+凭证可解析 + 命中率/转发成功率口径 + DTO 零凭证安全） | ✅ |
| `WeChatOperationsOptions` | 领域配置（`TKWF:WeChatOperations` 节——DefaultReplyText/凭证密钥/交互日志保留 + **`MessageBusinessEnabled` 消息业务总开关**（默认 true——关闭停发本地回复统一维护提示，转发网关不受控）） | ✅ |
| `WeChatOperationsReplyHandler` | 事件回复二级分发 14 分支（**11 分支网关挂载**——幂等最前短路）+ 内部 template 兜底 + 统计/日志记录（**V0.3.1 匹配过程明细——`MatchTraceJson` 逐条推演采集**（OnTextAsync 链 Keyword Exact→Contains→Default→template→DefaultReplyText / TryReplyBySceneAsync 场景规则命中+template 兜底，`List<MatchTraceStep>` best-effort 序列化旁路不改回复语义）） | ✅ |
| `IMessageGatewayService` | **转发编排核心 `RouteAsync`**（幂等最前短路 → 运行库 matcher 规则匹配 → 运行库 decider 转发决策 → **SyncProxy/PreprocessSync 真透传（V0.3.0——出站 → RawXml 接管/降级）**/ AsyncCopy 入队/本地/降级链）+ 路由规则/下游端点 CRUD（同 channel 校验/引用保护/完全重复同键检测）+ 网关状态/运行时开关；**`DownstreamEndpointDto.TimeoutMs`（V0.3.1——派生只读 `TimeoutSeconds * 1000`，前端 ms/服务端秒契约）** | ✅ |
| `IMessageForwardDispatcher` | 异步抄送门面——转发日志 Queued 落库 + `IBackgroundJobManager` 入队 `WeChatMessageForwardJob`（幂等键透传下游 at-least-once） | ✅ |
| `WeChatMessageForwardJob` | 异步抄送投递作业（`[BackgroundJob]`——SystemActor 帧取系统用户 + `DownstreamForwardClient` 出站 + 转发日志 Success/Fail/StatusCode/ResponseBody 回写） | ✅ |
| `DownstreamForwardClient` | 下游端点出站 typed client（`AddHttpClient` 生命周期托管防 socket 悬挂——端点 TimeoutSeconds 硬超时；**V0.3.0 增 `SendRawXmlAsync` 同步透传原语**——完整下游 XML 返回 + TimedOut 区分，`ForwardAsync` 异步抄送共用核心） | ✅ |
| `TKWF.Federation.WeChat.MessageGateway`（运行库） | 路由判定 `WeChatMessageRouteRuleMatcher`（**V0.3.0 MsgType 逗号分隔多值任一命中——`Split(',')` + OrdinalIgnoreCase**）/幂等键规范化 `WeChatMessageIdempotencyKeyBuilder`/转发决策与降级链 `WeChatMessageForwardDecider`/熔断三态——纯逻辑零持久化零第三方（对齐 DataPort/Tagging 运行库提炼先例） | ✅ |
| `WeChatCircuitBreaker`（熔断接线） | 运行库熔断状态机——Initializer `AddSingleton` 进程内单例接线（**V0.3.0**，每 EndpointId 独立状态，进程重启即清零）；`MessageGatewayService` SyncProxy 出站熔断注入（开闸期直接 LocalFallback 不转发） | ✅ |
| `DbWeChatCredentialResolver` | 凭证两源解析（DB 解密 + Options 降级） | ✅ |
| `DbWeChatChannelSource` | 通道配置 DB 投影（`Insert(0)` 前置——组合遍历序首位） | ✅ |
| ~~`WeChatOperationsWebExtension<TUserInfo>`~~（**V0.4.0 删除**） | **V0.4.0 起由门面 `[GenerateController]` 生成端点接管**——8 门面实现类 public + 接口方法 `[ApiExpose(RestRoute/RestMethod)]` → 消费方 SG1b 生成 REST 端点 + SG3 客户端 DTO/代理；路由编译期常量 `/api/wechat-ops/*`（原手写 WebExtension 15 端点 + `WeChatOperationsEndpointOptions` 开关/RoutePrefix 已移除——破坏性变更）。**端点总表（25 端点）**：运营 15（channels/menu/rules/materials/subscription-stats×2/status/logs×2）+ 网关 9（rules×3 + endpoints×3 + status + enabled + forward-logs）；`RouteAsync`/`RecordAsync` 等内部入口 `[ApiExpose(ExposeRest=false)]` 不暴露 | ✅ |

## 四、安全边界

- **凭证密文落库**：AppSecret/Token/EncodingAESKey AES-GCM 单段密文（base64(nonce‖cipher‖tag)，keyed `ISymmetricKeyProvider`）——明文不入库不进日志；解密仅出站瞬时
- **DTO 投影**：`GetAsync/ListAsync` 返回 DTO 不含凭证密文（Oracle P1）
- **出站日志脱敏**：access_token 经 query string 传参——出站日志脱敏
- **异常消息**：解密失败/凭证缺失异常消息不含明文
- **不走动态加载**：DB 不存 handler 类型名；通道级差异经编译期 `IChannelSpecificEventReplyHandler`（W3 绑定桶）静态注册
- **权限标注**：写操作 `[RequirePermission("WeChatOperations.Manage")]` / 查询 `[RequirePermission("WeChatOperations.Query")]`

## 五、与既有资产边界

| 资产 | 归属 | 定位 |
|------|------|------|
| `WeChatApiClient` 菜单 3 API / `IWeChatCredentialResolver` / template 多图文 | Federation.WeChat 平台库（V0.4.1） | 通讯/协议（纯库） |
| `WechatEventReplyHandlerBase` 二级分发 + 图文工具 | Federation.WeChat 平台库（V0.4.0） | 事件回复底座 |
| 9 门面 + 12 实体 + Handler + Web 端点 | **本扩展** | 运营数据底座/业务门面（DB） |
| GUI（菜单编辑器/素材/规则页） | DMP-Lite 消费方（阶段二拆回 `.UI` 扩展） | 表现层（R2 路径） |

## 六、版本记录

| 版本 | 内容 |
|------|------|
| V0.4.0（2026-10-18） | **设计缺口修复（DMP-Lite 转达四项裁定）**——① **客户端 DTO 生成链路根治（P0）**：8 门面实现类 public + `[GenerateController]` + 接口方法 `[ApiExpose(RestRoute/RestMethod/ExposeRest)]` 锁 `/api/wechat-ops/*`（SG1b 生成端点 + SG3 客户端 DTO/代理；**`WeChatOperationsWebExtension`/`WeChatOperationsEndpointOptions` 删除**——破坏性）；安全 wire DTO 化（`SaveChannelRequest`/`SaveEndpointRequest`/`SetGatewayEnabledRequest` 凭证明文 body 传输防 URL 泄漏）；② **网关 REST 端点补全（P1）**：`/gateway/*` 9 端点（rules×3 + endpoints×3 + status + enabled + `ListForwardLogsAsync` forward-logs）；`RouteAsync` 排除；③ **消息业务总开关 DB 化（P1）**：FeatureManagement 消费（`WeChatOperationsFeatureDefinitionContributor` 声明 `WeChatOperations.MessageBusiness` + Options 启动默认 + DB 权威双源——`IFeatureManager.SetValueAsync` 运行时切换）；`[TKWFExtensionDependency]` 补声明；`ImplicitUsings` 开启（主框架 SG1 `GeneratedContributors` 模板缺 using 临时修复，待报框架组）；测试 143 全绿 + 受影响面 6 项目零回归 |
| V0.1.0（2026-10-18） | 阶段一基础能力全量（8 实体 + 6 门面 + Handler 14 分支 + 两源并存 + Web 13 端点 + 测试 67 全绿）；GUI 阶段二拆 DMP-Lite（方案 §八） |
| V0.2.0（2026-10-18） | 消息路由转发网关模块（4 新实体 + 2 新门面 `IMessageGatewayService`/`IMessageForwardDispatcher` + handler 挂载 11 分支幂等最前短路 + 配置分层 `TKWF:WeChatOperations:Gateway` fail-closed + 运行库 `TKWF.Federation.WeChat.MessageGateway` + BackgroundJob 异步抄送 `WeChatMessageForwardJob` + 测试 67→110 全绿）；SyncProxy 同步透传暂缓（P0-1 平台库 RawXml 就绪后补） |
| V0.2.1（2026-10-18） | DMP-Lite 接口对齐迭代（P0 缺口四项）——`MessageRouteRuleEntity` 增 `Name` 规则显示名 / `IMaterialService.GetReferencesAsync` 素材引用反查（跨 ReplyRule 聚合 `MaterialReferenceDto` 清单）/ `IMenuService.GetClickKeysAsync` 菜单树递归收集 click key（自动生成映射行）/ `WeChatOperationsOptions.MessageBusinessEnabled` 消息业务总开关（默认 true——关闭停发本地回复统一维护提示，转发网关不受控）；测试 110→121 全绿 |
| V0.3.1（2026-10-18） | **P2 对齐迭代（DMP-Lite 5.6/5.7/5.8 剩余缺口四项）**——① 状态卡聚合门面 `IWeChatOperationsStatusService`（`GetStatusSummaryAsync` → `WeChatOperationsStatusDto`——Connected/TodayMessageCount/AutoReplyHitRate/ForwardSuccessRate，UTC 当日半开区间 + 连接=配置存在+凭证可解析 + 命中率/转发成功率口径 + DTO 零凭证）+ Web 端点 `GET {prefix}/channels/{channelId}/status`（`StatusEndpointEnabled` 开关）；② 匹配过程明细（`InteractionLogEntity` 增可空 `MatchTraceJson`——handler `List<MatchTraceStep>` 逐条推演采集 OnTextAsync 链 + TryReplyBySceneAsync，`RecordInteractionAsync` best-effort 序列化旁路不改回复语义）；③ 统计卡聚合（`ISubscriptionStatService.GetSummaryAsync` → `SubscriptionStatSummaryDto`——NetCount R7 负值钳 0 + DailyAverage 含首尾天数 + GrowthRate 期初净增口径 Undeterminable）+ Web 端点 `GET {prefix}/channels/{channelId}/subscription-stats/summary`；④ 超时单位统一（`DownstreamEndpointDto.TimeoutMs` 派生只读 `TimeoutSeconds * 1000`——前端展示/提交 ms、服务端按秒契约存储）；测试 130→143 全绿（+13：MatchTrace 6 / Status 4 / Summary 3；运行库 57 / 平台库 100 不变） |
| V0.3.0（2026-10-18） | **P1 二期对齐（DMP-Lite 四项）**——① 消息类型多选（DMP-Lite 5.5：`MessageRouteRuleEntity.MsgType` 逗号分隔多值 "text,image,event" 命中任一——运行库 matcher 多值适配）；② 来源/场景维度（DMP-Lite 5.7：`SubscriptionStatEntity` 增 `Source`——唯一约束扩 (ChannelId, StatDate, Source) + `RecordAsync`/`GetTrendAsync` 增 source 参数 + `WeChatSubscriptionSources` 常量 search/scan/qr_code/card/share/miniprogram）；③ 交互日志耗时（DMP-Lite 5.6：`InteractionLogEntity` 增 `DurationMs`——handler Stopwatch 计时）；④ SyncProxy/PreprocessSync 真透传落地（DMP-Lite 5.5 策略②③——`RouteSyncProxyAsync` 出站透传 `DownstreamForwardClient.SendRawXmlAsync` → 下游 XML `WechatReplyPayload.RawXml` 原样接管回复防双重信封 + 运行库 `WeChatCircuitBreaker` 熔断（Initializer 单例接线，开闸期直接降级不转发）+ Timeout/Fail 降级链 → LocalFallback 本地渲染链 + 幂等联动 + `IsValidReplyXml` 校验 + StringContent mediaType FormatException 修复）；PreprocessSync 与 SyncProxy 同语义；测试 121→130 全绿（运行库 57 含多值 5） |

<!-- EOF -->
