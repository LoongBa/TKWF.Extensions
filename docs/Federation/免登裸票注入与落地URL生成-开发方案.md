# 免登裸票注入与落地 URL 生成-开发方案

> **版本**：v0.3
> **日期**：2026-11-05
> **状态**：待 Oracle 复审（v0.2 评审 FAIL(BLOCKER)——B1-B6 已全部吸收修订）
> **来源需求**：DMP-Lite `docs/会员卡H5系统/需求单-免登链接裸票注入与落地URL生成-转达扩展组.md`（v1.0）+ `免登落地URL生成方案对比.md`（v1.1）+ 用户裁定（2026-11-05：**不是二选一**——形态①②独立交付，双占位符并存合法，同票双值）
> **改动面**：`Federation.Abstractions`（契约）· `Federation`（实现）· `Federation.WeChat`（占位符/模板链路）· 测试三项目

---

## 1. 背景与问题

### 1.1 现状机制（已实读源码确认）

**两条并列的事件回复 handler 链路**（Oracle 评审遗漏-1 补全——本期必须厘清）：

| 链路 | 基类 | 位置 | `{ticket}` 语义 | 消费方 |
|---|---|---|---|---|
| **A：主包签票基类** | `EventReplyHandlerBase`（Federation 主包） | `_Framework/Federation/EventReplyHandlerBase.cs` | **裸票**（`BuildTemplateUrl` 将 `{ticket}` 替换为 `issue.Ticket`，L164-175） | 子类实现 `BuildReplyAsync`，含 **D03 1.5 运营配置面** `IOperationsConfigQuery.UrlTemplate` 三级回退（L141-162）——**裸票注入 + 消费方自拼 URL 已实现** |
| **B：平台库模板 handler** | `WechatTemplateEventReplyHandler`（Federation.WeChat 平台库） | `_Framework/Federation.WeChat/WechatTemplateEventReplyHandler.cs` | **完整 URL**（`ResolveTicketAsync` → `BuildTicketUrlAsync` → `ReplacePlaceholders` 5 参 `.Replace("{ticket}", ticketUrl)`） | DMP 实际链路：`WeChatOperationsReplyHandler : WechatTemplateEventReplyHandler`（无 DB 规则 → `base.OnDefaultAsync` 模板兜底） |

**问题**：DMP（链路 B）模板 `{ticket}` 被替换为扩展拼装的完整免登 URL `https://m.loongba.cn/feberation/login/ticket?ticket=xxx` → 消费方 UserH5 该路由已删 → **404**。落地 URL 形态（`/m/{mid}/auth`）属消费方业务编排，扩展不应独占拼接。

**为何 DMP 不走链路 A 的既有裸票能力**：D03 1.5 的 `UrlTemplate`（`https://m.loongba.cn/{mid}/auth?t={ticket}` → 裸票）仅对**继承 `EventReplyHandlerBase`** 的 handler 生效；DMP  handler 继承**平台库模板 handler**（需要微信二级分发 + 模板链 4 级 + 规则驱动），链路 B **未接入 `IOperationsConfigQuery`**——故 DMP 实际无裸票通道。本期正是为链路 B 补裸票能力。

### 1.2 真正的问题

1. **消费方 404**：链路 B `{ticket}` = 完整 URL（含 `/feberation` + `TicketPath`），落地路由已删；
2. **职责越界**：落地 URL 形态（多商户路径、mid 语义）属消费方业务编排，被扩展独占拼接；
3. **表达力不足**：`TicketPath` 全局单值不适配多通道多租户；扩展不应理解业务 mid。

### 1.3 裁定方向（用户 2026-11-05）

- **需求 B（本期）**：扩展注入裸票据，落地 URL 由消费方图文模板承载；
- **交付形态 = 两形态并立，不是二选一**：
  - 形态①：新增占位符 `{ticket_value}`（裸票据串）；`{ticket}` 保持完整 URL 语义（零破坏）；
  - 形态②：`ILoginEntryService.IssueTicketAsync(...) → string?`（仅签票返回裸票）；
- **双占位符并存合法**：一次签票、同一票值，`{ticket}` → URL、`{ticket_value}` → 裸票（不置空任何一方）；
- **需求 A（预留，本期不启用）**：`TicketPath` 支持 `{channelId}` + 去 RoutePrefix。

### 1.4 评审历史

| 版本 | 评审 | 结论 | 修订 |
|---|---|---|---|
| v0.1/v0.2 | 扩展组自评 + Oracle3 | **FAIL(BLOCKER)**：BLOCKER-1（`FederationEventReplyOptions` 在主包，平台库不可引用）+ BLOCKER-2（`LoginEntryService` internal sealed，平台库不可调用）+ 遗漏-1（D03 1.5 既有裸票能力未分析）+ 遗漏风险 dev 日志/子类 ctor/测试缺口 | **v0.3 全部吸收**——契约收口双值，平台库零主包依赖（见 §3） |

---

## 2. 目标

1. **链路 B（平台库模板 handler）获得裸票注入能力**——`{ticket_value}` 占位符 + `IssueTicketAsync` 原语；
2. 图文模板 `link` 由业务侧直写完整落地 URL，票据以裸串注入；
3. 零破坏：`{ticket}` 完整 URL 语义不变、`BuildTicketUrlAsync` 签名不变、平台库依赖不变；
4. 一次一票（双占位符同票）、fail-closed 空串、TTL 模板优先、票据不落日志、uid 透传。

**非目标**：需求 A；`/login/ticket/redeem` 兑票端点（另单）；链路 A 改造（其裸票能力已具备，本期不动）。

---

## 3. 设计（v0.3 重构——吸收 Oracle B1-B6）

### 3.0 架构原则（修订核心——吸收 BLOCKER-1/2）

**平台库（Federation.WeChat）零主包依赖**——csproj 实证只引 `Federation.Abstractions`（+ TKWF.Domain），**不引 Federation 主包**（L2 门控）。因此：

- ❌ 平台库不得引用 `LoginEntryService`（internal sealed，`LoginEntryService.cs:31-32`——类型与方法均不可见）；
- ❌ 平台库不得引用 `FederationEventReplyOptions`（主包类型，`FederationEventReplyOptions.cs:18`）；
- ❌ 平台库不得持有 ctor `IOptions<FederationEventReplyOptions>`（CS0246 + L2 违规）；
- ✅ **平台库一切签票/组装能力经 `ILoginEntryService` 契约（Abstractions）获取**——双值一次拿全，Options 读取/组装逻辑收口在主包实现内。

### 3.1 契约变更（Federation.Abstractions，new minor）

#### 3.1.1 新增 `IssueTicketAsync`（形态②——返回裸票 `string?`）

```csharp
/// <summary>
/// 仅签发票据（不组装 URL）——返回<b>裸票据串</b>，落地 URL 由消费方业务侧自拼（需求 B 形态②；
/// 用户裁定：与 BuildTicketUrlAsync 及 {ticket_value} 占位符并立，非二选一）。
/// <para>与 <see cref="BuildTicketUrlAsync"/> 同源签票（同一内部核心 + 同一 FederationLoginEntryOptions）：
/// Enabled 门控 / TTL（ttlSeconds ?? TicketTtlSeconds）/ uid 透传入载荷——行为逐项对齐；
/// <b>唯一差异</b>：不组装 URL、不打印 dev 完整 URL 日志（裸票零出口——票据不落日志红线强化）。</para>
/// <para>fail-closed：未启用 / 签票失败 → null（调用方替换占位符为空串，其余文本照常渲染）。</para>
/// <para>⚠️ 参数尾部追加（ttlSeconds → requestBaseUrl → ct → uId，对齐 BuildTicketUrlAsync 签名形态——
/// requestBaseUrl 供 <see cref="TicketIssueResult.Url"/> 基址三级链第三级（请求 Host 推导）；裸票消费取
/// <c>.Ticket</c> 不受影响）。</para>
/// </summary>
Task<string?> IssueTicketAsync(
    string channelId, string channelType, string externalUserId,
    int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null);
```

#### 3.1.2 双占位符并存场景的完成面——保留 `BuildTicketUrlAsync` 作为并存组装的契约入口

**关键设计（吸收 BLOCKER-1/2 + 用户"不是二选一"）**：

并存场景（模板同时含 `{ticket}` + `{ticket_value}`）需要**同票双值**。受平台库零主包依赖约束，双值**必须经契约一次获得**。备选方案对比：

| 方案 | 描述 | 判定 |
|---|---|---|
| a) 平台库调 `LoginEntryService.BuildTicketUrl` | 类型 internal + 主包依赖 | ❌ BLOCKER-1/2 |
| b) 平台库调 `IssueTicketAsync`（裸票）+ 自拼 URL | 自拼需 RoutePrefix/TicketPath/基址——平台库无 Options | ❌ 复制组装逻辑 |
| c) **平台库调 `BuildTicketUrlAsync`（URL）+ 反解裸票** | 反解 URL 脆弱 | ❌ 否决 |
| d) **新增组合契约方法返回双值** | 契约新增 `IssueTicketAsync` 返回 record（Ticket+Url） | ✅ **v0.3 定稿** |
| e) 并存场景 `{ticket}` 置空 | 违反用户"不是二选一" | ❌ 否决 |

**定稿（方案 d——`IssueTicketAsync` 返回双值 record）**：

```csharp
/// <summary>签票结果（同票双形态——需求 B 双占位符并存共用同一票值）。</summary>
public sealed record TicketIssueResult(string? Ticket, string? Url);
//  Ticket = 裸票据串（{ticket_value} 占位符替换值）
//  Url    = 完整免登 URL（{ticket} 占位符替换值——{baseUrl}{RoutePrefix}{TicketPath}?ticket={Ticket}，与 BuildTicketUrlAsync 逐字节等价）

Task<TicketIssueResult?> IssueTicketAsync(
    string channelId, string channelType, string externalUserId,
    int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null);
```

- **一次签票 → 双值**（内部核心一次 `IssueAsync` → 裸票 + URL 组装）；fail-closed 全 null；
- **`requestBaseUrl` 参数（Oracle 复审 C1 必闭）**：`TicketIssueResult.Url` 的基址三级链第三级（`ChannelBaseUrls` → `TestTicketEndpointBaseUrl` → `requestBaseUrl`）——**链路 B 平台库模板 handler 经 `ctx.BaseUrl`（请求 Host 推导）传入**，确保 URL 完整化（微信图文 link 必须完整 URL）；不传时前两级均空 → 相对路径兜底（与 `BuildTicketUrlAsync` 语义一致）；
- 形态②（裸票消费方）取 `.Ticket`；形态①（占位符）`{ticket_value}` 取 `.Ticket`、`{ticket}` 取 `.Url`——**同票同源不签两次**；
- **需求单原签名 `→ string?` 的适配说明**：需求单形态②写`→ string?`（示意"仅签票不拼 URL"），v0.3 定稿返回 record 是**实现并存双值的唯一契约干净路径**（方案 d）——语义仍"仅签票"，Url 是附带同票产物，不为组装开第二方法；v0.4 补 `requestBaseUrl` 尾部参数（对齐 `BuildTicketUrlAsync` 签名先例）；签名变更登记需求单回执（见 §3.7）。
- `BuildTicketUrlAsync`（存量）= 保留签名不变，内部委托同一核心取 `.Url`（零行为变化回归锁）；
- `TicketIssueResult` 放 Abstractions（`ILoginEntryService.cs` 同文件，对齐 `FederationLoginEntryOptions` 先例）。

#### 3.1.3 `BuildTicketUrlAsync` / `FederationLoginEntryOptions` 注释更新

- `BuildTicketUrlAsync`：注明"扩展托管 URL 遗留形态（含 RoutePrefix + TicketPath）；新链路推荐 `IssueTicketAsync` 裸票 + 消费方自拼 URL"；
- `TicketPath`：注明"`BuildTicketUrlAsync`/`TicketIssueResult.Url` 组装形态自定义项——裸票消费（IssueTicketAsync.Ticket）不消费"。

### 3.2 实现（Federation 主包 LoginEntryService）

```csharp
// ① 内部签票核心（现 BuildTicketUrlAsync L64-98 门控+签票段原样抽出）：
private async Task<string?> IssueTicketCoreAsync(string channelId, string channelType, string externalUserId,
    int? ttlSeconds, CancellationToken ct, string? uId)
{
    if (!_loginEntry.Value.Enabled) return null;              // Enabled 门控（fail-closed ①）
    var payload = FederatedTicketPayloadBuilder.Build(provider:"wechat", protocolChannelType: channelType,
        channelId: channelId, externalUserId: externalUserId, platformUid: uId, assurance:"event",
        ttl: TimeSpan.FromSeconds(ttlSeconds ?? _loginEntry.Value.TicketTtlSeconds), resolver: ResolveChannelForm);
    try { return (await TicketService().IssueAsync(payload,
        TimeSpan.FromSeconds(ttlSeconds ?? _loginEntry.Value.TicketTtlSeconds), new SealedTicketOptions(), ct)).Ticket; }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { _logger.LogWarning(ex, "免登票据签票失败（票据后端不可用）——不生成票据：ChannelId={ChannelId} ExternalUserId={ExternalUserIdPrefix}",
        channelId, externalUserId.Length <= 6 ? externalUserId : externalUserId[..6]); return null; }
}

// ② URL 组装核心（现 BuildTicketUrlAsync L72-77 基址解析 + L100-101 组装段原样抽出——基址三级链 + CombineUrl）：
private string BuildTicketUrlCore(string channelId, string ticket, string? requestBaseUrl)
{
    // ChannelBaseUrls[channelId] → TestTicketEndpointBaseUrl → requestBaseUrl → ""（相对路径兜底）
    // → CombineUrl(baseUrl, _eventReply.Value.RoutePrefix, $"{_loginEntry.Value.TicketPath}?ticket={ticket}")
}

// ③ 新契约实现（一次签票双值）：
public async Task<TicketIssueResult?> IssueTicketAsync(string channelId, string channelType, string externalUserId,
    int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
{
    var ticket = await IssueTicketCoreAsync(channelId, channelType, externalUserId, ttlSeconds, ct, uId);
    if (ticket is null) return null;                                          // 一次签票（含 Enabled 门控 + fail-closed）
    var url = BuildTicketUrlCore(channelId, ticket, requestBaseUrl);          // URL 同票组装（基址三级链完整——Oracle 复审 C1）
    return new TicketIssueResult(ticket, url);
}

// ④ 存量方法委托（签名不变——回归锁）：
public Task<string?> BuildTicketUrlAsync(string channelId, string channelType, string externalUserId,
    int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
{
    // Enabled 门控 → IssueTicketCoreAsync → BuildTicketUrlCore(channelId, ticket, requestBaseUrl)
    // → dev 日志（IsProduction != true）→ 返回 Url        // 现出口保留（L105-106）
}
// BuildTestTicketUrlAsync：zero-change（不经 Enabled 门控，测试端点专用签票独立路径）
```

红线核对：
- **票据不落日志**：`IssueTicketCoreAsync` 零日志含票值；`IssueTicketAsync` **不打印任何日志**（含 dev——裸票零出口；Url 虽含票但属同票产物，**不输出**防日志漂移）；dev URL 日志仅存量 `BuildTicketUrlAsync`/`BuildTestTicketUrlAsync` 出口（回归锁）；
- **fail-closed**：Enabled=false / 签票失败 → null；
- **一次一票**：`IssueTicketAsync` 每次调用签一次——双值同票（单一 `IssueTicketCoreAsync`）。

### 3.3 平台库占位符（Federation.WeChat WechatEventReplyHandlerBase）

**新增 6 参重载**（5 参保留——存量调用零破坏）：

```csharp
/// <summary>
/// 占位符替换（{uid}/{openid}/{channel_id}/{channel_type}/{ticket}/{ticket_value}——需求 B 双占位符并立）：
/// {ticket} → <paramref name="ticketUrl"/>（完整 URL——存量语义不变）；
/// {ticket_value} → <paramref name="ticketValue"/>（裸票据串，null → 空串）。
/// 双占位符并存 = 合法场景：ticketUrl 与 ticketValue 同票（一次签票双形态），不置空任何一方（用户裁定）。
/// </summary>
protected static string ReplacePlaceholders(string text, string? uid, string? openId, string? channelId,
    string? ticketUrl, string? ticketValue)
    => ReplacePlaceholders(text, uid, openId, channelId, ticketUrl)
        .Replace("{ticket_value}", ticketValue ?? string.Empty);
```

**新增裸票/双值解析 helper**（替换 `ResolveTicketAsync` 在模板链路的使用——签名保持对齐）：

```csharp
/// <summary>
/// 免登票据双值解析（需求 B——一次签票取裸票 + 完整 URL（同票））。
/// <para>经 <see cref="ILoginEntryService.IssueTicketAsync"/> 签密封票据（ChannelType="wechat_event"、
/// ctx.OpenId 编排富化）→ <see cref="TicketIssueResult"/>（Ticket=裸票 / Url=完整 URL）；
/// null = 未启用/签票失败（fail-closed——调用方占位符替换空串，其余文本照常渲染）。</para>
/// </summary>
protected async Task<TicketIssueResult?> ResolveTicketAsync(WechatEventContext ctx, CancellationToken ct = default, int? ttlSeconds = null)
    => ctx is null || string.IsNullOrWhiteSpace(ctx.ChannelId) ? null
        : await User.Use<ILoginEntryService>()
            .IssueTicketAsync(ctx.ChannelId, ChannelType, ctx.OpenId ?? string.Empty,
                ttlSeconds, requestBaseUrl: ctx.BaseUrl, ct: ct, uId: ctx.UId);   // ⚠️ ctx.BaseUrl 透传——URL 基址三级链第三级（Oracle 复审 C1 必闭）
```

> ⚠️ **既有 `ResolveTicketAsync` 签名变化**：返回 `string?`（URL）→ `TicketIssueResult?`（双值）。**平台库内消费方**：`WechatTemplateEventReplyHandler`（模板链路，本期改造）+ 潜在子类——**破坏性**（编译期），方案登记破坏性变更（见 §7）；链外消费方（DMP 自写 handler）未用此 protected 方法（仅模板链路基类内部使用）。

### 3.4 模板链路（WechatTemplateEventReplyHandler）

**占位符语义（用户裁定：不是二选一）**：

| 模板占位符 | 替换值 | 语义 |
|---|---|---|
| `{ticket}`（存量） | 完整免登 URL | 扩展托管 URL 遗留形态，零破坏 |
| `{ticket_value}`（新） | **裸票据串** | 落地 URL 由业务模板自拼 |
| **双占位符并存** | **同一张票**：`{ticket}` → Url、`{ticket_value}` → Ticket | 合法场景——不置空任何一方 |

`OnDefaultAsync` 签票段改造（吸收 BLOCKER：**平台库只调契约，一次拿双值**）：

```csharp
// 现状：
//   if (IsBuiltInTestTemplate(template))
//       ticketUrl = await ResolveTestTicketUrlAsync(ctx, ct, template.TtlSeconds);
//   else
//       ticketUrl = await ResolveTicketAsync(ctx, ct, template.TtlSeconds);       // string?（URL）

// 改造后：
var builtIn = IsBuiltInTestTemplate(template);
TicketIssueResult? ticketResult = null;
if (builtIn)
{
    ticketUrl = await ResolveTestTicketUrlAsync(ctx, ct, template.TtlSeconds);     // 测试端点（现状不动——独立路径）
}
else
{
    ticketResult = await ResolveTicketAsync(ctx, ct, template.TtlSeconds);         // 一次签票 → 双值（Ticket + Url 同票）
    ticketUrl    = ticketResult?.Url;                                              // {ticket}      → 完整 URL
    ticketValue  = ticketResult?.Ticket;                                           // {ticket_value} → 裸票
}
// 占位符替换 6 参重载（双值注入）——BuildArticles 内 Render 改传 (ticketUrl, ticketValue)
```

- **一次签票**：`OnDefaultAsync` 每次调用只进一个签票分支且只调一次 `ResolveTicketAsync`——同票同源；
- 内置测试模板（link=`{ticket}`）→ 测试端点 URL——**现状不动**；
- 纯 `{ticket}` 存量业务模板 → 走新分支（`IssueTicketAsync` 双值取 `.Url`）——**产出 URL 与现状逐字节一致**（内部核心同 `BuildTicketUrlCore`，回归锁）；
- **平台库 ctor 零新增参数**——`WechatTemplateEventReplyHandler` ctor 保持 `(IDomainUser, IOptions<WechatEventReplyOptions>?, ILogger?)`（BLOCKER-1 消除：不引主包 Options）；`WeChatOperationsReplyHandler` ctor 链**零变化**（BLOCKER-5 消除）。

### 3.5 占位符名称冲突确认（P0-2）

grep 基线（2026-11-05）：现占位符集 `{uid}/{openid}/{channel_id}/{channel_type}/{ticket}/{msg_type}/{event}/{type}/{text}`——无 `{ticket_value}` 存量。登记使用指南总表。

### 3.6 票据安全护栏（P1-1）

- 裸票进 link = 票据随图文传播——安全模型不变（密封票据 AES-GCM + 兑票端点限 null-claimant + TTL 默认 120s）；
- 模板 `TtlSeconds` 受 `MaxTicketTtlSeconds`（默认 120）启动期校验约束——不因裸票形态放宽；
- 指南明示：link 含裸票 = 票据曝光面 = 点击者任一人，TTL 保持短生命周期。

### 3.7 需求单回执（签名变更登记 + 补充说明）

| 需求单项 | v0.3/v0.4 处理 | 说明 |
|---|---|---|
| 形态② `IssueTicketAsync → string?` | **返回 `TicketIssueResult(Ticket, Url)`** | 并存双值唯一契约干净路径（方案 d）；语义仍"仅签票"，Url 为同票附带；消费方取 `.Ticket` 裸票自拼；**v0.4 补 `requestBaseUrl` 尾部参数（Oracle 复审 C1）——Url 基址三级链第三级** |
| 形态① `{ticket_value}` 占位符 | 按原文交付 | 6 参重载 `{ticket}`（URL）+ `{ticket_value}`（裸票） |
| 双占位符并存 | 合法场景同票双值 | 一次签票不置空（用户裁定） |
| D03 1.5 既有能力 | **衔接说明** | 链路 A（EventReplyHandlerBase + IOperationsConfigQuery.UrlTemplate）已有裸票注入；本期为**链路 B（模板 handler）**补齐——DMP 实际链路，两条链路 `{ticket}` 语义差异（裸票 vs URL）**保持现状**（互不干扰），后续迭代可评估统一（见 §6 预留） |

### 3.8 文档同步

| 位置 | 变更 |
|---|---|
| `Federation.Abstractions/README.md` | 契约清单补 `IssueTicketAsync` + `TicketIssueResult`（与 BuildTicketUrlAsync 分工）；FederationLoginEntryOptions 注释 |
| `Federation/README.md` | 功能注记（IssueTicketAsync 双值 + 裸票零日志） |
| `Federation.WeChat/README.md` | 占位符总表补 `{ticket_value}`（`{ticket}` 注明"完整 URL 存量语义"） |
| `docs/Federation/联邦互联-使用指南.md` | 免登链路章节：裸票注入用法 + 双占位符语义表 + IssueTicketAsync 门面用法 + 两链路 `{ticket}` 语义差异说明 |
| `docs/Federation.WeChat/微信公众平台连接器-使用指南.md` | 占位符总表 + `{ticket_value}` 示例（业务直写落地 URL） |
| 需求单回执 | DMP-Lite 需求单追加变更记录（签名形态定稿 + 双链路说明） |

### 3.9 版本与发布

- `Federation.Abstractions`：new minor（`IssueTicketAsync` + `TicketIssueResult`）；
- `Federation`：new minor（实现）；
- `Federation.WeChat`：new minor（占位符 + 模板链路；`ResolveTicketAsync` 返回类型破坏性变更登记）；
- 三包联动 tag（契约包先行）——tag 须征求用户同意（AGENTS §5）；
- 受影响面 L1：Federation.Tests（164）+ Federation.WeChat.Tests（134）+ WeChatOperations.Tests（143/203）——契约包变更建议 L2。

---

## 4. 改动面清单（含 Oracle B5 增补）

| 文件 | 变更 |
|---|---|
| `_Framework/Federation.Abstractions/ILoginEntryService.cs` | 新增 `TicketIssueResult` record + `IssueTicketAsync` 签名（含 `requestBaseUrl` 尾部参数——C1 必闭）+ `BuildTicketUrlAsync`/`TicketPath` 注释更新 |
| `_Framework/Federation/LoginEntryService.cs` | 抽 `IssueTicketCoreAsync` + `BuildTicketUrlCore` → `IssueTicketAsync`（双值，零日志，URL 组装传 `requestBaseUrl`——C1）+ `BuildTicketUrlAsync`（存量委托 `.Url`，dev 日志回归）+ `BuildTestTicketUrlAsync`（零变化） |
| `_Framework/Federation.WeChat/WechatEventReplyHandlerBase.cs` | 占位符 6 参重载（`{ticket_value}`）+ `ResolveTicketAsync` 返回 `TicketIssueResult?`（破坏性登记）+ **透传 `ctx.BaseUrl` 为 requestBaseUrl（C1）**+ `ResolveTestTicketUrlAsync` 零变化 |
| `_Framework/Federation.WeChat/WechatTemplateEventReplyHandler.cs` | `OnDefaultAsync` 业务模板分支改双值取用（`.Url`/`.Ticket`）+ `BuildArticles` 传双值（6 参重载） |
| `_Framework/WeChatOperations/WeChatOperationsReplyHandler.cs` | **ctor 零变化**（BLOCKER-5 消除——不新增 Options）；内部 `base.OnDefaultAsync` 调用点适配返回形态（`:219,400`）|
| `_Tests/Extension.Federation.Tests/LoginEntryServiceTests.cs` | IssueTicketAsync 用例（门控/TTL/uid/null/零日志/同票双值） |
| `_Tests/Extension.Federation.WeChat.Tests/WechatTemplateEventReplyHandlerTests.cs` | `{ticket_value}` + 并存 + 同票断言 + 存量 URL 逐字节回归 |
| 测试宿主 `WeChatTestHost.cs` / `WeChatOperationsTestHost.cs` | Fake `ILoginEntryService` 补 `IssueTicketAsync`（record）+ 计数版 |
| 文档 6 处（§3.8） | — |

---

## 5. 测试设计（吸收 Oracle B6 强化）

### 5.1 Federation.Tests（LoginEntryService）

| # | 用例 | 断言 |
|---|---|---|
| 1 | `IssueTicketAsync` 正常路径 | 返回 `TicketIssueResult`：`Ticket` 为裸票（非 URL 无 `/feberation` 无 `?ticket=`）；可 `RedeemAsync` 兑付（闭环） |
| 2 | Enabled=false | null（fail-closed） |
| 3 | 签票失败（票据后端异常） | null + Warning |
| 4 | TTL 优先级 | 载荷 TTL 断言 |
| 5 | uid 透传 | 载荷 PlatformUid == 入参 |
| 6 | **裸票零日志出口（B6 强化）** | 非生产模式下 IssueTicketAsync **任何日志级别不含裸票全串**（TestLogger 收集断言：href 无 ticket 值 / Warning 无裸票） |
| 7 | **同票双形态** | `result.Ticket` ⊆ `result.Url`（URL query ticket == 裸票——同核心一次签发） |
| 8 | **URL 逐字节等价（C1 必闭回归）** | `IssueTicketAsync(requestBaseUrl: X).Url` ≡ `BuildTicketUrlAsync(requestBaseUrl: X)`（同参数同基址——委托同一核心）；**requestBaseUrl=null 与传值两形态均断言**（基址三级链完整） |
| 9 | `BuildTicketUrlAsync` 回归 | 现有用例全绿（dev 日志出口保留） |

### 5.2 Federation.WeChat.Tests（模板链路）

| # | 用例 | 断言 |
|---|---|---|
| 1 | 模板 link 含 `{ticket_value}` | 替换为裸票（Fake 计数版 + 返回值入 link） |
| 2 | 一次回复只签一次票 | 双 `{ticket_value}` → IssueTicketAsync 恰 1 次 + 两处同值 |
| 3 | **双占位符并存** | `{ticket}` → URL、`{ticket_value}` → 裸票、同票（URL query ticket == 裸票）——不置空任何一方（用户裁定回归锁） |
| 4 | 仅 `{ticket}`（存量业务模板） | 产出 URL 与现状逐字节一致（内部核心回归锁）——**基址三级链完整（ctx.BaseUrl 透传，C1 必闭）** |
| 5 | 未启用/签票失败（Fake null） | 两占位符均空串、link 残 URL 形态断言（P1-3） |
| 6 | 内置测试模板（link=`{ticket}`） | 仍走 ResolveTestTicketUrlAsync（测试端点 URL） |
| 7 | 转义 | 裸票字符集（base64url 白名单）断言 |
| **8（B5）** | **WeChatOperations 子类回归** | `WeChatOperationsReplyHandler` 无规则 → template 兜底 → `{ticket_value}` 裸票（WeChatOperations.Tests 新增——生产 handler 全链路） |
| **9（B3）** | **链路 A 回归** | `EventReplyHandlerBase` 既有裸票路径（BuildTemplateUrl）不受影响——Federation.Tests 既有用例全绿 |

### 5.3 宿主 Fake 升级

`WeChatTestHost.NullLoginEntryService` / `WeChatOperationsTestHost`：补 `IssueTicketAsync` 实现（接口编译必需）+ 计数版（`CallCount` + 可配置返回 `TicketIssueResult`）。

### 5.4 回归

- 既有全绿：Federation 164 + WeChat 134 + WeChatOperations 143（V0.8.1 线 203）；
- slnx 构建 0 错误（L0）；契约包变更 → 建议 L2。

---

## 6. 预留能力登记

| 能力 | 说明 | 启用条件 |
|---|---|---|
| **A-1** `TicketPath` 支持 `{channelId}` 占位符 | URL 组装时替换通道标识（别名/id 原样注入） | 未来"简单多通道"消费方无模板体系 |
| **A-2** 免登链路退出 RoutePrefix | `{baseUrl}{TicketPath}?ticket=`（事件/测试端点不受影响） | 同上 |
| **A-3** `ChannelTicketPaths` per-channel 覆盖 | 缺省回退全局 TicketPath | 同扩展不同落地骨架 |
| **U-1** 两链路 `{ticket}` 语义统一（A 裸票 vs B URL） | 统一为裸票或增加兼容层 | 后续迭代评估（本期保持现状，两链路互不干扰） |

> 本期零实现；登记于本方案 + 指南"已知预留"段。

---

## 7. 风险与边界（吸收 Oracle 全项）

| 风险 | 处置 |
|---|---|
| **BLOCKER-1** 平台库引主包 | **消除**：平台库零主包依赖——契约收口（§3.0/3.1.2） |
| **BLOCKER-2** internal 类型不可见 | **消除**：不经 `LoginEntryService` 类型——双值经契约 record 获取（§3.2） |
| **C1（Oracle 复审）** URL 基址第三级丢失 | **消除**：`IssueTicketAsync` 契约增 `requestBaseUrl` 尾部参数 + `ResolveTicketAsync` 透传 `ctx.BaseUrl`——`{ticket}` 存量路径 URL 与现状逐字节等价（测试 5.1#8/5.2#4 锁定） |
| **遗漏-1** D03 1.5 既有能力 | **衔接**：链路 A 裸票已实现（§1.1 表格 + §6 U-1 统一评估）；本期为链路 B 补齐（§1.1 为何 DMP 不走 A） |
| 双占位符并存误解 | 语义表固化 + 同票回归锁（测试 5.2#3） |
| 裸票漏打日志 | `IssueTicketAsync` 零日志出口（含 dev）；测试 5.1#6 任何级别断言 |
| **ResolveTicketAsync 返回类型破坏性** | 平台库 protected 方法签名变化（string?→TicketIssueResult?）——内部使用方仅模板链路（本期改造）；链外子类编译期可见（登记破坏性，指南声明）；`ResolveTestTicketUrlAsync` 零变化 |
| 存量 `{ticket}` dev 日志出口 | 纯 `{ticket}` 模板链路改走 `IssueTicketAsync`（零日志）——**dev URL 日志出口丢失**（现 `BuildTicketUrlAsync` L105-106 出口仅存量调用方保留）→ **处置**：开发期验证走测试端点链路（`ResolveTestTicketUrlAsync` 保留 dev 日志）或 `EnableBuiltInTestTemplate`——指南明示（吸收遗漏风险-1） |
| 契约包变更下游编译 | Fake 同步（5.3）——编译期暴露 |
| 兑票端点（另单）未交付 | 本需求只解决链接形态；核销端点 DMP 真实兑票依赖（遗留 #1），联调走 Fake 边界 |

---

## 变更记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-11-05 | v0.1 | 首版：需求 B 双形态合并交付 + 评审结论 + 需求 A 预留登记 |
| 2026-11-05 | v0.2 | 按用户裁定（不是二选一）修订：形态①②独立交付 + 双占位符并存合法 |
| 2026-11-05 | v0.3 | **按 Oracle3 评审 FAIL(BLOCKER) 全面修订——B1-B6 全吸收**：① BLOCKER-1/2 根治（平台库零主包依赖——`IssueTicketAsync` 返回 `TicketIssueResult(Ticket, Url)` 双值 record，契约收口；不引 `FederationEventReplyOptions`/`LoginEntryService`）；② 遗漏-1 衔接（链路 A/B 双 handler 链路差异分析——D03 1.5 裸票仅链路 A 有，本期为链路 B 补齐）；③ dev URL 日志出口变化处置（存量出口仅存于 BuildTicketUrlAsync，开发期走测试端点）；④ WeChatOperations 子类回归测试（B5）+ 裸票零日志任何级别断言（B6）+ 破坏性变更登记（ResolveTicketAsync 返回形态） |
| 2026-11-05 | v0.4 | **按 Oracle2 复审 PASS WITH CONDITIONS 修订——C1-C3 闭环**：① **C1（BLOCKER）消除**——`IssueTicketAsync` 契约增 `string? requestBaseUrl = null` 尾部参数（对齐 BuildTicketUrlAsync 签名形态），`ResolveTicketAsync` 透传 `ctx.BaseUrl`——URL 基址三级链第三级（请求 Host 推导）不丢失，`{ticket}` 存量路径 URL 与现状逐字节等价；② **C2** §3.1.2 注释矛盾修正（"无 requestBaseUrl 不组 URL" → "requestBaseUrl 供 Url 基址第三级"）；③ **C3** §3.2 行号引用修正（L83-98 → L64-98 门控+签票段 / L72-77 基址解析）；测试 5.1#8/5.2#4 补基址完整等价断言 |