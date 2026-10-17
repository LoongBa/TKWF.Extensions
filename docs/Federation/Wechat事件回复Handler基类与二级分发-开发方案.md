# Wechat 事件回复 Handler 基类与二级分发——开发方案

> **状态**：✅ Oracle PASS WITH CONDITIONS（2026-10-17，P0×2/P1×4 全部吸收 + P2×7 顺带落实） · **日期**：2026-10-17 · **扩展**：Federation.WeChat / Federation
> **关联**：`EventReplyHandlerBase与内置测试Handler-开发方案.md`（既有，已演进）、`ADR-Federation-事件回复handler基类与内置测试设施.md`（已废弃标记）、`ADR-Federation-外部身份联邦与密封票据适配.md`（E3 事件链底座）、`外部身份联邦与会话绑定守卫-开发方案.md`（E1-E3）
> **v4.10.80 联动**：稳健性修复批次适配核查——零代码适配（见 §八），CPM lockstep 待 tag+NuGet 发布后
> **Oracle 评审**：PASS WITH CONDITIONS——P0-1（Template 默认注册违反禁令→改不默认注册）、P0-2（默认行为矛盾→统一无模板返回 null）、P1-1（平台基类不继承 EventReplyHandlerBase）、P1-2（随 P0-1 解决）、P1-3（Probe 无条件注册+IOptions 惰性判断）、P1-4（通道存 `__decrypted_xml` 供 Parse）、P2-1~7（顺带）

---

## 一、背景与现状

### 1.1 现状链路（E3 事件链）

```
微信 POST → /feberation/{platformId}/event[/{channelId}]
  → ResolveChannelAsync → VerifyUrlAsync（GET echostr 握手）
  → AuthenticateAsync（msg_signature + AES 解密，信任根一票否决）
  → OrchestrateAsync（uid 只读查询富化）
      → WeChatEventChannel.HandleEventAsync
          ├─ 提取 FromUserName/ToUserName/MsgType/Event/Content → 富化 Parameters
          ├─ 内部迭代 IEventReplyHandler（仅 ChannelType 匹配，first-non-null-wins）
          │    → 非 null 回复包 XML 信封（text / news）
          └─ 无回复 → EventReplyResult("", "text/xml")  ← 恒非 null
      → 编排器 handler 双桶迭代（W3 绑定桶）← 对 wechat_event 实际不可达
  → Results.Text(reply.Content, reply.ContentType)（200 空回复兜底）
```

### 1.2 既有抽象（Federation 主包）

| 类型 | 职责 | 现状 |
|---|---|---|
| `IEventReplyHandler` | 契约（`ChannelType` 匹配 + `HandleAsync`） | 已存在 |
| `EventReplyHandlerBase` | 高内聚基类（入口反查 ChannelConfig → `BuildReplyAsync` 业务规则 → 签密封票据 → 图文链接） | 已存在，平台无关 |
| `TestEventReplyHandler` | 内置测试 handler（模板驱动第一级 + 测试端点联动） | 已存在，将被替换删除 |
| `FederationEventReplyOptions` | 领域配置（`TestEventReplyHandlerEnabled`/`TestTicketEndpointBaseUrl`/`RoutePrefix`） | 已存在，开关将迁移 |
| `IChannelSpecificEventReplyHandler` | W3 多通道绑定缝（可选标记接口 + `ChannelId`） | 已存在，微信通道不可达（见 1.3） |

### 1.3 缺口清单（本方案解决的问题）

| # | 缺口 | 现象 | 根因 |
|---|---|---|---|
| G1 | **无 MsgType/Event 二级分发** | 业务 handler 无法按事件/消息类型分流——需自行 re-parse `__raw` XML | 平台层仅富化扁平 `Parameters`，不提供类型化虚方法 |
| G2 | **Unsubscribe/MASSSENDJOBFINISH 禁回复未强制** | handler 可对这两个事件回复（微信平台拒绝/报错） | 通道对任何 Event 一视同仁委托 handler |
| G3 | **EventKey/场景值/经纬度等字段未类型化** | CLICK 菜单 key、SCAN 场景值、location 经纬度需从 `__raw` 重新提取 | `WeChatEventCrypto` 仅提取 FromUserName/ToUserName/MsgType/Event/Content |
| G4 | **W3 绑定桶对微信生产不可达** | `IChannelSpecificEventReplyHandler`（精确 ChannelId 绑定）在微信通道永不触发 | `WeChatEventChannel.HandleEventAsync` 恒非 null → 编排器双桶短路；通道内部迭代仅 ChannelType 匹配 |
| G5 | **handler 迭代权分裂** | 通道内部迭代 + 编排器双桶并存，平台间行为不一致（WeCom/DingTalk 走编排器，WeChat 走通道内部） | 通道"抢"了迭代权且只实现单桶 |
| G6 | **XML 信封在通道内硬编码** | 通道职责超出"信任根+取身份"（当前也包信封） | `BuildTextReplyXml`/`BuildNewsReplyXml` 在 `WeChatEventChannel` 内 |

---

## 二、目标

1. **平台特定 handler 两层架构**（Federation.WeChat 平台库内）：
   - `WechatEventReplyHandlerBase`：二级分发基类（MsgType/Event → 强类型虚方法）+ 图文/文本构造工具方法（支持多图文）+ 协议门控（unsubscribe/MASSSENDJOBFINISH 强制禁回复）+ **`__decrypted_xml` 字段类型化解析**；
   - `WechatTemplateEventReplyHandler`：模板驱动默认 handler（消费方显式注册，零代码模板配置）；
   - `WechatProbeEventReplyHandler`：诊断探针 handler（全分支可区分回复/日志，零外部配置，IOptions 开关门控）。
2. **事件链迭代权统一**：`WeChatEventChannel.HandleEventAsync` 改为"验签+解密+富化 → 返回 null"，handler 迭代唯一归口编排器 `EventOrchestrator` —— W3 绑定桶对微信可达（G4 修复），通道形态与 WeCom/DingTalk 一致化（G5 修复）。
3. **协议门控内置**：unsubscribe/MASSSENDJOBFINISH 在回复组装层强制 null（G2 修复），业务副作用（如记录取关）仍可经虚方法执行。
4. **微信字段类型化**：EventKey/场景值/经纬度/PicUrl/MediaId 等在平台基类提取（来自 `__decrypted_xml`），子类虚方法收强类型参数（G3 修复）。
5. **TestEventReplyHandler 直接替换**：不保留过渡，删除（用户裁定 2026-10-17）。
6. 既有 `IEventReplyHandler` 消费方**零破坏**（契约不变，仅迭代位置变化——语义等价）。

---

## 三、设计

### 3.1 分层模型

> **Oracle P1-1 采纳**：`WechatEventReplyHandlerBase` **不继承** `EventReplyHandlerBase`——两个基类**并列**（职责不同：签票 vs 二级分发），消费方按需选。

```
契约层（Federation 主包——已存在）
├── IEventReplyHandler（契约）                        ── 不变
└── EventReplyHandlerBase（签票→图文高内聚，平台无关）  ── 保留不动（供"事件→登录链接"业务 handler）
     └── TestEventReplyHandler                         ── 删除（能力迁平台层 Template/Probe）

编排层（Federation 主包——已存在）
└── EventOrchestrator（handler 双桶迭代：绑定桶优先 → ChannelType 桶回退）  ── 不变（微信可达化）

平台层（Federation.WeChat 平台库——新增）
├── WechatEventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
│   ├── HandleAsync 模板：解析 Parameters + __decrypted_xml → WechatEventContext
│   │   → 二级分发虚方法 → 协议门控（强制 null）→ 业务载荷 → BuildXml 包信封
│   ├── 图文工具方法（多图文）：BuildTextReply/BuildNewsReply/ReadNewsTemplate/ReplacePlaceholders
│   ├── 协议门控：NoReplyEvents（unsubscribe/MASSSENDJOBFINISH + TEMPLATESENDJOBFINISH）
│   └── public const NewsReplyTemplateKey（承接 TestEventReplyHandler 删除后的常量）
├── WechatTemplateEventReplyHandler（模板驱动，消费方显式注册）
│   └── 继承平台基类 + 覆盖 OnDefaultAsync 读模板（无模板 → null）
└── WechatProbeEventReplyHandler（诊断探针，无条件注册 + IOptions 门控）
    ├── 继承平台基类 + 覆盖全部分支
    ├── 开关关 → 全分支返回 null（不回复不干预）
    ├── 开关开 → 可回复分支 PROBE:branch=... 文本；门控分支基类强制 null + ILogger
    └── 与业务 handler 互斥使用（使用指南声明）
```

### 3.2 WechatEventReplyHandlerBase（平台基类）

**位置**：`_Framework/Federation.WeChat/WechatEventReplyHandlerBase.cs`

**继承**：`DomainServiceBase, IEventReplyHandler`（`ChannelType => "wechat_event"`）——**不继承 EventReplyHandlerBase**（Oracle P1-1）。

> 签票能力（`ISealedTicketService<FederatedIdentity>` 图文链接）不再经继承获得——业务 handler 需要签票时经 `User.Use<ISealedTicketService<FederatedIdentity>>()` 懒加载直接调（DI004 合规），或选用主包 `EventReplyHandlerBase`（签票专用基类）。两基类职责分离：签票 → 主包；二级分发 → 平台库。

**HandleAsync 模板方法**：

```csharp
namespace TKWF.Federation.WeChat;

public abstract class WechatEventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
{
    public virtual string ChannelType => "wechat_event";

    public async Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct)
    {
        // ① 解析：Parameters（msg_type/event/...）+ __decrypted_xml（EventKey/经纬度/PicUrl/MediaId 等未富化字段）
        var wechat = WechatEventMessage.Parse(context.Parameters);

        // ② 二级分发（MsgType/Event → 强类型虚方法；未覆盖分支 → OnDefaultAsync）
        var payload = await DispatchAsync(wechat, ct);

        // ③ 协议门控：NoReplyEvents（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）强制 null
        //    ——虚方法已执行（业务副作用可做），回复被吞（Oracle P2-3 对齐：异常路径哨兵见 §3.4）
        if (IsNoReplyEvent(wechat))
        {
            LogProbeBranch(wechat);   // ILogger "PROBE branch=..."（决策 A——日志区分门控分支）
            return null;
        }

        // ④ 业务载荷 → 微信 XML 信封（text/news 图文）
        return payload is null ? null : BuildReplyResult(wechat, payload);
    }
}
```

**二级分发虚方法集（分支全集**——Senparc 范式实证 + 微信官方普通消息页确证 + 公开事件知识）：

| 类别 | 虚方法 | 强类型参数 | 基类默认 |
|---|---|---|---|
| 事件 | `OnSubscribeAsync` | `WechatEventContext`（含 EventKey=带参二维码场景 `qrscene_` 前缀） | 委托 `OnDefaultAsync` |
| 事件 | `OnUnsubscribeAsync` | 基础字段 | 委托 `OnDefaultAsync`（回复被门控吞掉——副作用可做） |
| 事件 | `OnScanAsync` | 场景值 `scene_id`（EventKey 去 `qrscene_` 前缀） | 委托 `OnDefaultAsync` |
| 事件 | `OnLocationEventAsync` | `Latitude`/`Longitude`/`Precision` | 委托 `OnDefaultAsync` |
| 事件 | `OnClickAsync` | 菜单 key（EventKey） | 委托 `OnDefaultAsync` |
| 事件 | `OnViewAsync` | 菜单 URL（EventKey） | 委托 `OnDefaultAsync` |
| 事件 | `OnViewMiniProgramAsync` | 小程序参数（EventKey） | 委托 `OnDefaultAsync` |
| 事件 | `OnMassSendJobFinishAsync` | `Status`/`TotalCount`/`FilterCount`/`SentCount`/`ErrorCount` | 委托 `OnDefaultAsync`（回复被门控吞掉） |
| 消息 | `OnTextAsync` | `Content` | 委托 `OnDefaultAsync` |
| 消息 | `OnImageAsync` | `MediaId`/`PicUrl` | 委托 `OnDefaultAsync` |
| 消息 | `OnVoiceAsync` | `MediaId`/`Format` | 委托 `OnDefaultAsync` |
| 消息 | `OnVideoAsync` | `MediaId`/`ThumbMediaId` | 委托 `OnDefaultAsync` |
| 消息 | `OnShortVideoAsync` | `MediaId`/`ThumbMediaId` | 委托 `OnDefaultAsync` |
| 消息 | `OnLocationAsync` | `Latitude`/`Longitude`/`Scale`/`Label` | 委托 `OnDefaultAsync` |
| 消息 | `OnLinkAsync` | `Title`/`Description`/`Url` | 委托 `OnDefaultAsync` |
| 兜底 | `OnDefaultAsync` | `WechatEventContext`（含原始 `msg_type`/`event`） | **返回 null**（不回复） |

> **默认实现语义（Oracle P0-2 统一稿）**：精确分支未覆盖 → 委托 `OnDefaultAsync` → **默认 null**（不回复不拦截）。Template handler 只覆盖 `OnDefaultAsync`——**在显式配置模板 + enabled=true 时才返回非 null**，否则 null（不干预业务 handler）。

**返回载荷类型**：

```csharp
/// <summary>业务回复载荷（基类负责包 XML 信封——子类零 XML 感知）。</summary>
public sealed record WechatReplyPayload(
    string? Content = null,                              // 文本回复（优先）
    IReadOnlyList<EventReplyNewsArticle>? News = null);  // 图文回复（多图文，Content 为空时用）
```

**图文工具方法**（基类 protected——模板/探针/业务 handler 共用）：

| 方法 | 职责 | 来源 |
|---|---|---|
| `BuildTextReply(WechatEventContext, string)` | text 信封组装 | 自 `WeChatEventChannel.BuildTextReplyXml` 上移 + 参数携带 |
| `BuildNewsReply(WechatEventContext, IReadOnlyList<EventReplyNewsArticle>)` | news 信封组装（`<ArticleCount>{n}</ArticleCount>` + `<Articles>` 多图文） | 自 `WeChatEventChannel.BuildNewsReplyXml` 上移 |
| `BuildReplyResult(WechatEventContext, WechatReplyPayload)` | 载荷 → `EventReplyResult`（XML 信封）——新闻优先/文本回退 | 新增 |
| `ReadNewsTemplate(Channel.Extra)` | 读取 `news_reply` 模板 JSON（**缺失/损坏 → null**，不回退内置——Oracle P0-2） | 自 `TestEventReplyHandler` 上移并改语义 |
| `ReplacePlaceholders(text, uid, openId, channelId)` | 占位符替换 | 自 `TestEventReplyHandler` 上移 |
| `public const NewsReplyTemplateKey = "news_reply"` | 模板键常量（承接 TestEventReplyHandler 删除后的编译破坏——Oracle P2-2） | 自 `TestEventReplyHandler` 迁移 |

**协议门控**（Oracle P2-1 采纳：OrdinalIgnoreCase）：

```csharp
// 禁回复事件（微信平台规则——回复不送达/禁回复）；TEMPLATESENDJOBFINISH 为 Oracle P2-4 补充核查项
private static readonly HashSet<string> NoReplyEvents = new(StringComparer.OrdinalIgnoreCase)
{
    "unsubscribe", "MASSSENDJOBFINISH", "TEMPLATESENDJOBFINISH",
};
```

**`WechatEventContext` / `WechatEventMessage.Parse`**（Oracle P1-4 采纳——数据源 `__decrypted_xml`）：

```csharp
/// <summary>微信消息/事件上下文（平台字段类型化——子类零 XML 解析、零 Parameters 裸读）。</summary>
public sealed record WechatEventContext( /* MsgType/Event/FromUserName/ToUserName/EventKey/经纬度/PicUrl/MediaId/... 全字段 */ );

/// <summary>
/// 解析：① Parameters 基础字段（msg_type/event/from_user_name/to_user_name/external_user_id/content 等——通道已富化）；
/// ② __decrypted_xml（通道存解密后明文——EventKey/经纬度/PicUrl/MediaId/Scale/Label/Title/Description/Url/群发统计等未富化字段）。
/// 一次性平台侧解析——业务 handler 零裸解析、零 __raw 密文处理。
/// </summary>
internal static WechatEventContext Parse(IReadOnlyDictionary<string, string?> parameters)
{
    // msg_type/event 从 Parameters（通道富化）；未富化字段从 __decrypted_xml XDocument 提取（CDATA 感知）
}
```

### 3.3 两个具体 handler（消费方显式注册）

**`WechatTemplateEventReplyHandler`**：

```csharp
/// <summary>模板驱动 handler（消费方显式注册——Oracle P0-1：平台库不默认注册）。
/// 覆盖 OnDefaultAsync 读 Channel.Extra["news_reply"] 模板构造图文（占位符 {uid}/{openid}/{channel_id}/...）；
/// 模板缺失 / 损坏 / Enabled=false → 返回 null（不回复不拦截——Oracle P0-2）。</summary>
public sealed class WechatTemplateEventReplyHandler : WechatEventReplyHandlerBase
{
    protected override Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct)
    {
        var template = ReadNewsTemplate(ctx.Channel.Extra);   // 缺失/损坏 → null
        if (template is null or { Enabled: not true }) return null;
        return BuildNewsPayload(template, ctx);               // 模板标题/描述/link/pic/ttl
    }
}
```

> 与既有 `TestEventReplyHandler` 的模板驱动能力对齐，但：无签密封票据耦合（纯图文）；无内置测试端点联动；模板缺失返回 null（不干预）。

**`WechatProbeEventReplyHandler`**：

```csharp
/// <summary>诊断探针（无条件注册 TryAddEnumerableConstructible + IOptions<WechatEventReplyOptions>
/// 惰性判断 ProbeEventReplyHandlerEnabled——Oracle P1-3；开关关 → 全分支 null 不干预）。
/// 开关开：可回复分支 → "PROBE:branch=subscribe key=..." 可区分文本；门控分支 → 基类强制 null + ILogger。</summary>
public sealed class WechatProbeEventReplyHandler : WechatEventReplyHandlerBase
{
    private readonly IOptions<WechatEventReplyOptions> _options;
    // 覆盖全部分支虚方法 → 各自返回带分支标识的文本（开关关时全 null）
}
```

### 3.4 WeChatEventChannel 改造（G4/G5/G6 修复）

```csharp
// WeChatEventChannel.HandleEventAsync 改造：
// ① 保留：验签（msg_signature）+ AES 解密 + FromUserName/ToUserName/MsgType/Event/Content 提取富化
// ② 新增富化：Parameters["__decrypted_xml"] = 解密后明文 XML（Oracle P1-4——Parse 数据源）
// ③ 删除：内部 handler 迭代（L346-374）+ XML 信封构建（BuildTextReplyXml/BuildNewsReplyXml）
//    + 恒非 null 空回复兜底（L376-378）
// ④ 返回：验签解密成功富化后 → null（"无平台回复"——编排器接收后转 handler 双桶迭代）
// ⑤ 异常（解密失败/格式非法）→ 返回空哨兵 new EventReplyResult("", "text/xml")（Oracle P2-3——
//    与正常路径 null 区分：异常不触发 handler 双桶，编排器短路空回复）
public override Task<EventReplyResult?> HandleEventAsync(SsoChannelEventContext context, CancellationToken ct = default)
{
    // ...验签/解密/富化（保留）+ __decrypted_xml 富化...
    return Task.FromResult<EventReplyResult?>(null);       // 正常路径：平台化回复归 handler
    // catch 异常 → return Task.FromResult<EventReplyResult?>(new EventReplyResult("", "text/xml"));  // 哨兵
}
```

效果：
- 编排器 `EventOrchestrator.OrchestrateAsync` 的 W3 双桶对 wechat_event **变为可达**（G4 修复）；
- handler 迭代唯一归口编排器（G5 修复——对齐 WeCom/DingTalk）；
- 通道职责收敛为"信任根+取身份+富化+存明文 XML"（G6 修复——信封上移平台基类）；
- 5 秒兜底不变：无 handler 回复 → 编排器 null → 端点 `Results.Text("", "text/xml")` 200 空回复。

**ctor 形态**（Oracle P2-6 核查前置）：现状 `(IDomainUser, IEnumerable<IEventReplyHandler>, ChannelConfig?, ILogger?)` → 删 `IEnumerable<IEventReplyHandler>` → `(IDomainUser, ChannelConfig?, ILogger?)`（对齐 WeCom/DingTalk）。**实施前 grep `SsoChannelFactory` 的 `ActivatorUtilities.CreateInstance` 调用点确认参数列表**——工厂传 `(User, cfg)` 显式参数 + 其余 DI 解析，删参数后无调用点改动。

### 3.5 注册形态（平台库）

```csharp
public static IServiceCollection AddWeChatFederationChannels(
    this IServiceCollection services,
    Action<WeChatOptions>? configure = null)
{
    // ...既有：AddOptions<WeChatOptions> + AddHttpClient<WeChatApiClient> +
    //    TryAddEnumerable<IChannelSource, WeChatChannelSource> +
    //    TryAddEnumerableConstructible<ISsoChannel, WeChatOauthChannel/WeChatEventChannel>

    // 新增：平台层 Options（EventReply 节——探针开关）
    services.AddOptions<WechatEventReplyOptions>();

    // 探针 handler：无条件注册（Oracle P1-3——TryAddEnumerableConstructible），
    //    内部 IOptions<WechatEventReplyOptions> 惰性判断 ProbeEventReplyHandlerEnabled：
    //    关 → 全分支 null（不回复不干预）；开 → PROBE 可区分文本
    services.TryAddEnumerableConstructible<IEventReplyHandler, WechatProbeEventReplyHandler>();

    return services;
}

// ⚠️ Template handler 不在此注册（Oracle P0-1——平台库不注册 handler 实例，防过度设计）：
//    消费方显式 services.TryAddEnumerableConstructible<IEventReplyHandler, WechatTemplateEventReplyHandler>();
```

**平台库 Options**（新增 `WechatEventReplyOptions`，`TKWF:Federation:WeChat:EventReply` 节）：

| 配置 | 默认 | 语义 |
|---|---|---|
| `ProbeEventReplyHandlerEnabled` | **false**（P1-1 fail-safe） | 探针 handler 启用开关——开发期开 true 验证全分支；接入业务后保持 false（与业务 handler 互斥——Oracle P2-5） |

> `TestTicketEndpointBaseUrl`/`RoutePrefix` **继续归属主包** `FederationEventReplyOptions`（测试端点是 Federation 主包编排面能力）。主包 `FederationEventReplyOptions.TestEventReplyHandlerEnabled` **移除**（探针开关迁移平台库）。

### 3.6 主包删除（TestEventReplyHandler 替换）

| 文件 | 动作 |
|---|---|
| `_Framework/Federation/TestEventReplyHandler.cs` | **删除**（能力被平台层 Template/Probe 替代；`NewsReplyTemplateKey` 常量持平台层——Oracle P2-2） |
| `_Framework/Federation/FederationEventReplyOptions.cs` | `TestEventReplyHandlerEnabled` **移除**（迁移平台库 `ProbeEventReplyHandlerEnabled`）；`TestTicketEndpointBaseUrl`/`RoutePrefix` 保留 |
| `_Framework/Federation/FederationExtensionInitializer.cs` | `TryAddEnumerableConstructible<IEventReplyHandler, TestEventReplyHandler>` 条件注册（L111-112）**移除** |
| `_Framework/Federation/EventReplyHandlerBase.cs` | **保留不动**（平台无关签票基类——平台基类不继承它，Oracle P1-1）；`TestEventReplyHandlerEnabled` protected 属性（L39）处置：仅 TestEventReplyHandler 消费 → 一并移除（无其他消费方，grep 核查） |

### 3.7 消费方业务 handler 形态（使用指南更新后）

```csharp
// 形态 1：仅二级分发 + 文本/图文回复（不签票）——继承平台基类
public sealed class MyBizHandler : WechatEventReplyHandlerBase
{
    protected override Task<WechatReplyPayload?> OnClickAsync(WechatEventContext ctx, CancellationToken ct)
        => Task.FromResult<WechatReplyPayload?>(new(News: [new EventReplyNewsArticle("菜单入口", "...", url)]));

    protected override Task<WechatReplyPayload?> OnSubscribeAsync(WechatEventContext ctx, CancellationToken ct)
        => Task.FromResult<WechatReplyPayload?>(new(Content: "欢迎关注！"));
    // 其余分支不覆盖 → OnDefaultAsync → null（不回复）
}
services.TryAddEnumerableConstructible<IEventReplyHandler, MyBizHandler>();   // 消费方注册

// 形态 2：需要签票（事件→登录链接）——继承主包 EventReplyHandlerBase（既有，不变）
// 形态 3：纯模板驱动零代码——消费方注册 WechatTemplateEventReplyHandler + 配置 news_reply 模板
// 覆盖 IChannelSpecificEventReplyHandler（声明 ChannelId）→ W3 绑定桶生效（G4 修复后可达）
```

---

## 四、与既有设计的承接关系

| 既有 | 承接 |
|---|---|
| `EventReplyHandlerBase`（"机制归基类、决策归子类"） | 保留不动——平台基类**不继承**它（Oracle P1-1：两基类并列，职责分离） |
| 两级模型（模板驱动第一级 + W3 绑定第二级） | 演进为三级阶梯：模板（Template handler，显式注册零代码）→ 二级分发（业务继承平台基类覆盖分支）→ W3 绑定（特定 channelId） |
| Oracle P2-3 记录（"两处迭代设计可接受"） | **收口**——迭代权统一编排器（G4/G5 修复） |
| "平台库不注册 handler，防过度设计" | **遵守**（Oracle P0-1）——平台库提供类型与可选注册（Probe 受 IOptions 开关门控；Template 消费方显式注册） |
| `TestEventReplyHandler` | 直接替换删除（用户裁定 2026-10-17——不保留过渡） |
| `WeChatEventChannel` 通道边界（"只管验签+解密+取 openid"） | 回归宣言（信封上移平台基类；新增存明文 XML 供 Parse） |

---

## 五、影响面与破坏性

| # | 变更 | 级别 | 说明 |
|---|---|---|---|
| 1 | `WeChatEventChannel` ctor 删 `IEnumerable<IEventReplyHandler>` | 内部（非公共契约——工厂 ActivatorUtilities 构造，P2-6 核查后无调用点改动） | 无消费方感知 |
| 2 | `WeChatEventChannel.HandleEventAsync` 行为：恒非 null → 返回 null（异常哨兵空回复） | **平台库 v0.2.0 破坏性**（行为） | 消费方 handler 迭代路径从"通道内部"移到"编排器"——语义等价（first-non-null-wins + ChannelType 匹配 + 空回复兜底），但依赖编排器双桶（已存在） |
| 3 | `TestEventReplyHandler` 删除 + `TestEventReplyHandlerEnabled` 开关移除 + `EventReplyHandlerBase.TestEventReplyHandlerEnabled` 属性移除 | **Federation 主包 v0.9.0 破坏性** | 消费方若注册/继承测试 handler → 编译失败；改平台层 Template/Probe。`NewsReplyTemplateKey` 常量持平台层（P2-2） |
| 4 | handler 注册入口变化（主包 Initializer 删除 → 平台库 Probe 无条件注册 + Template 消费方显式） | 配置/装配 | 消费方经 `AddWeChatFederationChannels()` 自动获得 Probe（开关默认关）；Template/业务 handler 显式注册 |
| 5 | `IEventReplyHandler`/`EventReplyHandlerBase` 契约 | **零破坏** | 保留不动；平台基类为纯新增 |

**新文件**（平台库）：`WechatEventReplyHandlerBase.cs` / `WechatTemplateEventReplyHandler.cs` / `WechatProbeEventReplyHandler.cs` / `WechatEventContext.cs`（含 Parse）/ `WechatReplyPayload.cs` / `WechatEventReplyOptions.cs`。
**删除文件**：`TestEventReplyHandler.cs`（主包）。
**修改文件**：`WeChatEventChannel.cs`（退回 null + `__decrypted_xml` 富化 + 删 XML 信封/迭代 + 异常哨兵）、`WeChatFederationServiceCollectionExtensions.cs`（Options + Probe 注册）、`FederationExtensionInitializer.cs`（删测试 handler 注册）、`FederationEventReplyOptions.cs`（删 TestEventReplyHandlerEnabled）、`EventReplyHandlerBase.cs`（删 TestEventReplyHandlerEnabled 属性）、`WeChatOptions.cs`（EventReply 节绑定）。

---

## 六、测试计划

| 项目 | 用例 | 覆盖 |
|---|---|---|
| `Extension.Federation.WeChat.Tests`（新增 `WechatEventReplyHandlerBaseTests`） | 二级分发映射（每个 MsgType/Event → 对应虚方法被调 + 强类型参数正确——含 `__decrypted_xml` 解析 EventKey/经纬度） | G1/G3 |
| | 未覆盖分支 → OnDefaultAsync → null（不回复） | 默认语义 |
| | 协议门控：unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 虚方法被调但返回 null（副作用可做、回复被吞） | G2 |
| | BuildXML：text/news 多图文信封正确（`<ArticleCount>`/`<Articles>`） | G6 |
| `Extension.Federation.WeChat.Tests`（新增 `WechatTemplateEventReplyHandlerTests`） | 模板驱动（占位符/disabled/ttl）；**无模板 → null**（Oracle P0-2）；Enabled=false → null | Template |
| `Extension.Federation.WeChat.Tests`（新增 `WechatProbeEventReplyHandlerTests`） | 开关关 → 全分支 null（不干预）；开关开 → 全分支 PROBE:branch=...；门控分支空回复 + 日志 | Probe |
| `Extension.Federation.WeChat.Tests`（改 `WechatEventChannelTests`） | HandleEventAsync 正常返回 null（原"空回复兜底"用例改断言 null）；`__decrypted_xml` 富化；异常路径返回空哨兵；原 XML 信封用例删（能力上移基类） | G4/G5/P2-3 |
| `Extension.Federation.Tests`（改 `EventReplyHandlerBaseTests`） | 删 TestHandler 相关用例（TestHandler_* 9 处）；基类（EventReplyHandlerBase）用例保留 | 主包删除 |
| `FederationWebExtensionTests`（W3 双桶） | 保留——语义不变 | G4 回归 |
| 全量回归 | slnx 构建 0 错误 + 受影响面测试全绿 + 全量分批（发布/tag 前 L2） | — |

**测试宿主适配**：`WeChatChannelTestHost.ConfigureHost` 走 `AddWeChatFederationChannels` 已含 Probe 注册 + Options 兜底（P2-7 落实——补 `AddOptions<WechatEventReplyOptions>` 默认值断言）；`Federation.Tests` 测试宿主删 TestEventReplyHandler 注册。

---

## 七、文档同步

| 文档 | 动作 |
|---|---|
| `docs/Federation/联邦互联-使用指南.md` §5.1 | 测试设施段改写：`TestEventReplyHandler` → 平台层 Template/Probe 三段式 + 注册方式 + 开关配置迁移（`TestEventReplyHandlerEnabled` → `TKWF:Federation:WeChat:EventReply:ProbeEventReplyHandlerEnabled`）+ Probe 与业务 handler 互斥（P2-5） |
| `docs/Federation.WeChat/微信公众平台连接器-使用指南.md` | 事件回复段落新增：平台基类 + 二级分发分支表 + 业务 handler 示例（P2-7 补全） |
| `_Framework/Federation.WeChat/README.md` | §三 组件清单补基类/Template/Probe + Options；§四 通道契约表更新（HandleEventAsync 返回 null + `__decrypted_xml`）；版本记录 V0.2.0 |
| `_Framework/Federation/README.md` | TestEventReplyHandler 段落更新（架构决策/版本记录） |
| `docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md` | 标注"已被本方案演进/替换"（不删除——历史保留） |
| `docs/Federation/ADR/ADR-Federation-事件回复handler基类与内置测试设施.md` | 标注"已废弃" + 引用新 ADR（AGENTS §4 生命周期——不删文件） |
| 新增 `docs/Federation/ADR/ADR-Federation-平台特定事件回复Handler基类与二级分发.md` | 三问必填 |

---

## 八、v4.10.80 顺带适配（核查结论）

| 适配点 | 核查 | 动作 |
|---|---|---|
| InboxCleanupService 启动自检（`InboxTableName` vs 实体 `[Table]` fail-fast） | 本仓库扩展零 EventBus/Inbox 使用（grep 核查） | 零影响 |
| IdTokenDecoder 异常契约（`OAuthProtocolException("invalid_jwt_format")`） | `Federation.Oidc/OidcChannelFlow.cs:78,180` catch 形态已含 `OAuthProtocolException`；裸 BCL `FormatException` 本不在 catch 集——转译后**修正漏接** | 零影响（顺带收益） |
| CPM lockstep 4.10.80 | tag 待用户同意 + NuGet 传播 | **不在本迭代**（CPM 保持 4.10.79；发布后按需 lockstep） |
| 本地 DLL 模式同步 | 部署根 refs 当前 4.10.79（用户侧执行 sync 后 4.10.80） | 零代码（本迭代不依赖 4.10.80 新 API） |

---

## 九、验收清单

| # | 检查项 |
|---|---------|
| 1 | 平台层基类 + Template + Probe 落地，二级分发分支全集表实现完整 |
| 2 | 协议门控（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）在回复组装层强制 null——任何 handler 无法回复 |
| 3 | `WeChatEventChannel.HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵——W3 双桶对微信生产可达（G4 回归测试实证） |
| 4 | Template handler 不默认注册（平台库零 handler 实例注册——仅 Probe 无条件注册且 IOptions 门控）+ Explore 配置 `ProbeEventReplyHandlerEnabled=false` 时全分支 null |
| 5 | 主包 `TestEventReplyHandler` 删除 + `TestEventReplyHandlerEnabled`/基类属性移除（零残留 grep） |
| 6 | `IEventReplyHandler`/`EventReplyHandlerBase` 契约零变化；平台基类不继承主包基类（并列） |
| 7 | slnx 构建 0 错误 + 受影响面测试全绿 |
| 8 | README/使用指南/ADR 同步（旧 ADR 废弃标记 + 新 ADR 三问必填） |
| 9 | 通知文字（不落文档）转告 DMP 项目组适配（消费方改造要点） |

<!-- EOF -->