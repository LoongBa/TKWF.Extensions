# EventReplyHandlerBase 与内置测试 Handler 开发方案

> **日期**: 2026-10-15 | **扩展**: Federation（联邦互联连接层） | **关联**: ADR-Federation-外部身份联邦与密封票据适配（E3 事件回复契约）、TrustCenter.Abstractions（ISsoChannel/ISealedTicketService/IAuthorizationCodeService）
> **状态**: ✅ **Oracle 评审 APPROVE WITH CONDITIONS（2026-10-15）——2 P0 + 2 P1 + 3 P2 全部吸收，修复后实施**；**2026-10-16 v0.8.1 修订**（用户裁定：测试端点/ticket 命名联动 + BaseUrl 空 → 相对路径 + 平台段/通道段选区——见 §八 变更记录）

---

## 一、目的与目标

本方案为 **Federation 事件回复链路**（外部身份联邦 E3——微信未认证号事件推送 → handler 签密封票据 → 图文回复 → 用户点击兑换）补齐**开发期测试能力**与**业务 handler 开发高内聚设施**：

1. **内置测试 handler**（`TestEventReplyHandler`）——Federation 默认注册，开发期零配置即可完成微信接入对接测试（事件推送 → 图文回复 → 点击 → 测试端点兑换 → 显示载荷，全链路闭环）。
2. **测试端点联动**——内置 `GET {prefix}/{platformId}/test/{channelId}/{ticket}` 测试端点（`TestTicketEndpointEnabled` 默认关，v0.8.1 起沿用正常端点风格：平台段 + 通道段 + ticket 段，选区经 `ResolveChannelAsync` 校验）**只配合测试 handler 使用**：消费方接入业务 handler 后关闭（避免生产暴露）。
3. **`EventReplyHandlerBase` 高内聚基类**——业务 handler 开发时，"经 `ISealedTicketService<FederatedIdentity>` 签密封票据 → 构造图文（news）链接"操作**内置在基类**（模板方法 + 受保护方法），子类只需实现业务规则（检查 openid/uid → 返回载荷或 null），减少样板代码、提高可维护性。

**读者 3 句话理解目标状态**：Federation 包内有一个默认注册的测试 handler（发图文带 ticket 链接）+ 一个配套测试端点（点开即兑换显示）；业务方开发自己的 handler 时继承基类只写业务规则；测试设施一旦被业务 handler 替换即整体关闭。

---

## 二、问题

### 2.1 现象

- **开发期微信接入无内置验证手段**：当前 `IEventReplyHandler` 是裸接口（`HandleAsync` 单方法），Federation 默认**不注册任何 handler**（`FederationExtensionInitializer` 注释明示"事件回复是业务决策，归消费方"）。消费方要验证微信事件链路，必须**先写一个业务 handler 才能测通**——无法先验证"微信→Federation→回复"基础连通性。
- **业务 handler 样板代码多**：签密封票据（`ISealedTicketService<FederatedIdentity>.IssueAsync`）+ 构造图文链接（`EventReplyNewsArticle` + URL 拼接）+ 组装 `EventReplyResult`——每个业务 handler 重复同一套逻辑，易错且难维护。
- **测试端点裸暴露风险**：`TestTicketEndpointEnabled`（默认关）是独立开关，无"与测试 handler 生命周期绑定"语义——若消费方忘关端点而接了业务 handler，测试端点仍在生产暴露。

### 2.2 触发场景

- 新项目接入微信（未认证号）事件链路——需要先验证"微信后台事件推送 → Federation 验签解密 → 回复"连通，再开发业务逻辑。
- 消费方开发业务 handler（如"发送图文菜单/欢迎消息"）——需要签 accesscode + 组图文的样板能力。
- 生产环境——必须确认测试 handler 与测试端点不会残留暴露。

### 2.3 现有方案不足

- `IEventReplyHandler` 裸接口 + 消费方自注册：验证链路需先写代码（鸡生蛋）。
- 无任何 handler 基类/扩展方法：accesscode→payload→图文的高内聚封装缺失（本次核查实证：`_Framework` 全仓搜 `EventReplyHandlerBase`/`AccessCode Helper`/`BuildNews` 零命中，仅 `WeChatEventChannel.BuildNewsReplyXml` 通道内部信封组装）。
- 测试端点独立开关：无"换 handler 即关"的联动语义（靠文档提醒，易遗漏）。

---

## 三、方案设计（方案 A：基类 + 内置测试 handler）

### 3.1 新增文件（Federation 主包 `_Framework/Federation/`）

#### ① `EventReplyHandlerBase.cs`（高内聚基类）

```csharp
/// <summary>
/// 事件回复 handler 基类（外部身份联邦 E3 高内聚设施）——模板方法封装：
/// "经 ISealedTicketService<FederatedIdentity> 签密封票据 → 构造图文（news）链接（URL 指测试端点）"。
/// 子类只需实现 <see cref="BuildReplyAsync"/>（业务规则：检查 context → 返回载荷或 null）。
/// </summary>
public abstract class EventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
{
    // ChannelType 由子类提供（wechat_event 等——编排按此匹配）
    public abstract string ChannelType { get; }

    protected EventReplyHandlerBase(IDomainUser user, IOptions<FederationEventReplyOptions>? options = null) : base(user) { }

    /// <summary>模板方法：子类业务载荷 → 签密封票据 → 图文链接回复（IEventReplyHandler.HandleAsync）。</summary>
    public async Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        var payload = await BuildReplyAsync(context, ct);   // 子类业务规则；null = 不处理
        if (payload is null) return null;

        // 高内聚核心：签密封票据（FederatedIdentity 载荷 JSON → AES-GCM 落库 + ExpectedClaimant CAS）
        var identity = JsonSerializer.Deserialize<FederatedIdentity>(payload.PayloadJson);
        if (identity is null) return null;
        var channelCfg = await User.Use<IChannelRegistry>().GetByAliasOrIdAsync(identity.ChannelId, ct);   // 反查平台段
        if (channelCfg is null) return null;                                                                // 查不到 → 不回复
        var issue = await User.Use<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, payload.Ttl, new SealedTicketOptions(ExpectedClaimant: payload.ExpectedClaimant), ct);
        var url = BuildTestTicketUrl(issue.Ticket, channelCfg.PlatformType, identity.ChannelId);            // 见 3.3
        return new EventReplyResult(
            Content: string.Empty, ContentType: "text/xml",
            NewsArticles: [new EventReplyNewsArticle(payload.Title, payload.Description, url, payload.PicUrl)]);
    }

    /// <summary>子类实现业务规则——返回载荷（非 null = 回复图文）或 null（不处理）。</summary>
    protected abstract Task<EventReplyPayload?> BuildReplyAsync(SsoChannelEventContext context, CancellationToken ct);

    /// <summary>构造测试端点 URL（{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}——与 FederationWebExtension 端点一致；BaseUrl 空 → 相对路径）。</summary>
    protected string BuildTestTicketUrl(string ticket, string platformId, string channelId) { ... }

    /// <summary>子类载荷 DTO（Title/Description/PayloadJson/Ttl/ExpectedClaimant/PicUrl）。</summary>
    protected sealed record EventReplyPayload(
        string Title, string Description, string PayloadJson,
        TimeSpan Ttl, string? ExpectedClaimant = null, string? PicUrl = null);
}
```

**要点**：
- 继承 `DomainServiceBase`（对齐 ADR90 正确路线——`IDomainUser` 经基类 `User`，不注册 DI）；`IEventReplyHandler : IDomainService` 接口满足（基类派生）。
- `User.Use<ISealedTicketService<FederatedIdentity>>()` 帧内懒加载（对齐 DI004——禁构造注入）。
- 签密封票据用 `ISealedTicketService<T>.IssueAsync(payload, ttl, options)`（面① ADR109 D4——载荷 JSON 落库加密，expectedClaimant 进原子 CAS）。
- 图文链接平台段经 `IChannelRegistry.GetByAliasOrIdAsync(identity.ChannelId)` 反查 `PlatformType`——v0.8.1 用户裁定；查不到 → 不回复（对齐"无法构造有效图文链接则不回复"语义）。
- `HandleAsync` 模板方法固定"签票→组图文"；子类只写 `BuildReplyAsync`（业务规则）。

#### ② `TestEventReplyHandler.cs`（内置测试 handler）

```csharp
/// <summary>
/// 内置测试 handler（开发期对接测试用）——受开关 TestEventReplyHandlerEnabled（默认 false）显式启用。
/// 收到事件（微信未认证号推送）→ 载荷 = FederatedIdentity（openid/uid 填充）→ 签密封票据 → 图文链接回复。
/// 消费方接入业务 handler 后保持测试开关关闭（测试设施整体不启用）。
/// </summary>
public sealed class TestEventReplyHandler : EventReplyHandlerBase
{
    public override string ChannelType => "wechat_event";   // 仅事件通道

    protected override Task<EventReplyPayload?> BuildReplyAsync(
        SsoChannelEventContext context, CancellationToken ct)
    {
        // 开关关闭（默认 false）→ 不回复不拦截（测试设施整体不启用，业务 handler 正常流转）
        if (!TestEventReplyHandlerEnabled)
            return Task.FromResult<EventReplyPayload?>(null);

        // 载荷 = FederatedIdentity 兼容 JSON（P0-2：测试端点 RedeemAsync<FederatedIdentity> 反序列化）
        var openId = context.Parameters.GetValueOrDefault("external_user_id");
        var uid = context.Parameters.GetValueOrDefault("uid");
        var channelId = context.Parameters.GetValueOrDefault("channel_id");
        var payload = new FederatedIdentity(
            UId: uid ?? openId ?? "", ChannelId: channelId ?? "", ExternalUserId: openId ?? "", ChannelType: "wechat_event");
        var payloadJson = JsonSerializer.Serialize(payload);
        return Task.FromResult<EventReplyPayload?>(
            new EventReplyPayload(
                Title: "微信接入测试", Description: $"openid={openId} uid={uid}——点击验证 ticket 兑换",
                PayloadJson: payloadJson, Ttl: TimeSpan.FromMinutes(5), ExpectedClaimant: null));  // P0-1：可转让（测试端点 Redeem claimant=null）
    }
}
```

**要点**：
- `ChannelType = "wechat_event"`——只匹配事件通道（微信未认证号），不影响 OAuth 通道。
- 载荷 = `FederatedIdentity` 序列化（P0-2 修复——测试端点 `RedeemAsync<FederatedIdentity>` 反序列化属性对齐：`UId`/`ChannelId`/`ExternalUserId`/`ChannelType`）。
- `ExpectedClaimant = null`（P0-1 修复——测试端点 `RedeemAsync(code, context: null)` 与 non-null `ExpectedClaimant` 不匹配必致 `CLAIMANT_MISMATCH`；可转让语义由密封票据自身高熵随机码保护）。
- `BaseUrl` 空 → 相对路径图文链接（v0.8.1 用户裁定——默认指向本服务同源，微信可点击；不再返回 null）。

#### ③ `FederationEventReplyOptions.cs`（领域配置）

```csharp
/// <summary>
/// 事件回复领域配置（TKWF:Federation:EventReply）——配置分层 AGENTS §8：领域行为开关归领域节。
/// </summary>
public class FederationEventReplyOptions
{
    /// <summary>内置测试 handler 注册开关（默认 false——P1-1 fail-safe：显式开启开发期测试；消费方接入业务 handler 后保持关闭）。</summary>
    public bool TestEventReplyHandlerEnabled { get; set; } = false;

    /// <summary>测试端点 URL 基址（图文链接拼接用——消费方部署域名；空 = 相对路径 /{RoutePrefix}/.../test/... 默认指向本服务同源）。</summary>
    public string? TestTicketEndpointBaseUrl { get; set; }

    /// <summary>测试端点 RoutePrefix 对齐（默认 /feberation——与 FederationEndpointOptions.RoutePrefix 一致）。</summary>
    public string RoutePrefix { get; set; } = "/feberation";
}
```

### 3.2 修改 `FederationExtensionInitializer.cs`（默认注册测试 handler）

在 `ConfigureServices` 中（事件编排门面注册处）追加：

```csharp
// 内置测试 handler（开发期对接测试——受 TestEventReplyHandlerEnabled 开关，默认 false 显式启用；
// 对齐 ADR92 集合版守卫工厂）。消费方接入业务 handler 后保持关闭（测试设施整体不启用）。
services.AddOptions<FederationEventReplyOptions>();   // 领域配置兜底
if (services.GetOptions<FederationEventReplyOptions>().TestEventReplyHandlerEnabled)
    services.TryAddEnumerableConstructible<IEventReplyHandler, TestEventReplyHandler>();
```

> ⚠️ **注册守卫工厂注意**：`TestEventReplyHandler` ctor 含 `IDomainUser`——`TryAddEnumerableConstructible` 集合版守卫工厂帧内供给（对齐既有 `ISsoChannel` 集合注册形态）；`EventReplyHandlerBase` 继承 `DomainServiceBase`（ctor(IDomainUser)），守卫工厂可构造。

### 3.3 测试端点 URL 拼接

`BuildTestTicketUrl` 实现：
```
{TestTicketEndpointBaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}
```
- `TestTicketEndpointBaseUrl`（如 `https://api.example.com`）+ `RoutePrefix`（默认 `/feberation`）+ `platformId`（经 registry 从 identity.ChannelId 反查 `PlatformType`）+ `channelId`（identity.ChannelId）→ 与 `FederationWebExtension` 的 `MapGet($"{prefix}/{{platformId}}/test/{{channelId}}/{{ticket}}")` 端点路径一致（v0.8.1 用户裁定：对齐正常端点风格——平台段 + 通道段 + ticket 段）。
- **BaseUrl 为空**（v0.8.1 用户裁定——默认测试 handler 应默认指向本服务）→ 相对路径 `/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}`（以 / 开头，默认指向本服务同源——微信可点击）；不再返回 null（原 P1-2/P2-1"空 → null"语义反转）。
- **反查失败**：载荷 `ChannelId` 不在 registry（无法反查 `PlatformType`——无法构造有效图文链接）→ 不签票据不回复（返回 null，不拦截业务 handler）。

### 3.4 与测试端点的联动语义（"换 handler 即关"）

| 组件 | 开关 | 默认 | 生命周期 |
|------|------|------|---------|
| 内置测试 handler | `TKWF:Federation:EventReply:TestEventReplyHandlerEnabled` | **false**（P1-1 fail-safe——显式开启） | 开发期测试开 true；接入业务 handler 保持 false |
| 测试端点 | `TKWF:Federation:Web:TestTicketEndpointEnabled` | **false**（显式开启） | 开发期测试开 true；接入业务 handler 保持 false |

**联动原则**：测试端点**只配合测试 handler**——开发期验证时同时开两个开关；接入业务 handler 时**两个都关**（测试 handler 关 = 测试链路不再回复；测试端点关 = 生产无暴露）。文档明确写入使用指南（见 §五），并在 `FederationEventReplyOptions`/`FederationEndpointOptions` 注释互相引用。

> 设计取舍：**不做运行时自动联动**（如端点开关自动跟随 handler 注册状态）——配置分层原则（AGENTS §8：领域开关归领域节、Web 暴露面开关归 Web 节，POCO 不镜像）要求两类配置解耦；自动联动需跨层读配置，破坏分层。改为**文档级强约束**（使用指南"测试设施生命周期"小节 + 两端开关注释互引）——Oracle 评审确认此取舍合理，可选增强：启动时若 `TestEventReplyHandlerEnabled=true` 且集合 >1 handler → 日志 Warning（不阻塞，不跨层读配置）。

---

## 四、备选方案与取舍

| 方案 | 描述 | 取舍 |
|------|------|------|
| **方案 A（本方案）** | `EventReplyHandlerBase` 基类 + `TestEventReplyHandler` 内置 + 领域 Options 开关 | ✅ 高内聚（模板方法封装签票→图文）；✅ 开发期默认可用；✅ 配置分层清晰 |
| 方案 B | 仅扩展方法（`EventReplyContextExtensions`）内聚签票→图文，无基类 | ❌ 每个 handler 仍自实现 `HandleAsync` 样板（ChannelType/签名/返回组装）；高内聚度不足 |
| 方案 C | 内置测试 handler 用 Web 层开关控制 | ❌ 违反配置分层（领域行为开关归领域节）；测试 handler 是领域服务非表现层 |

---

## 五、文档与测试计划

### 5.1 使用指南更新（`docs/Federation/联邦互联-使用指南.md`）

新增 **"微信事件链路测试设施"** 小节：
- 开发期测试流程：装配 Federation + 平台库 → 开 `TestEventReplyHandlerEnabled=true` + `TestTicketEndpointEnabled=true`（+ 可选配 `TestTicketEndpointBaseUrl` 部署域名；空 = 相对路径默认本服务）→ 微信后台配置事件推送 URL → 发消息触发 → 收到图文链接 → 点击 → 测试端点显示载荷 JSON（openid/uid/channel_id）。
- **业务 handler 替换**：开发 `XxxEventReplyHandler : EventReplyHandlerBase`（继承基类，只写 `BuildReplyAsync` 业务规则）→ `TryAddEnumerableConstructible<IEventReplyHandler, XxxEventReplyHandler>` 注册 → **同时关两个测试开关**（`TestEventReplyHandlerEnabled=false` + `TestTicketEndpointEnabled=false`）。
- 基类使用示例（继承 + 业务规则 10 行内）。

### 5.2 测试计划（`_Tests/Extension.Federation.Tests/` 新增）

| 测试 | 覆盖 |
|------|------|
| `EventReplyHandlerBaseTests`：模板方法签票→图文 | 子类返回载荷 → `HandleAsync` 返回 `EventReplyResult`（NewsArticles 非空 + URL 含 `/{platformId}/test/{channelId}/{ticket}` + 载荷 JSON 正确）+ `ISealedTicketService<FederatedIdentity>.IssueAsync` 被调（fake 断言）；platformId 经 registry 反查 |
| `EventReplyHandlerBaseTests`：null 透传 | 子类返回 null → `HandleAsync` 返回 null（不处理，first-non-null-wins 语义） |
| `TestEventReplyHandlerTests`：业务规则 | `ChannelType == "wechat_event"`；`BuildReplyAsync` 载荷含 openid/uid；ExpectedClaimant=openId |
| `FederationExtensionInitializerTests`：开关注册 | `TestEventReplyHandlerEnabled=true` → DI 含 `TestEventReplyHandler`；`false` → 不含（集合空） |

---

## 六、实施边界与依赖

- **不触碰并行任务文件**：`EventOrchestrator.cs`/`FederationEndpointOptions.cs`/`FederationWebExtension.cs`/`ISsoChannel.cs`（TrustCenter.Abstractions）/`WeChatEventChannel.cs`/`WeChatEventCrypto.cs`/`WeChatEventChannelTests.cs`（微信图文并行任务工作区）——本方案仅**新增文件** + 修改 `FederationExtensionInitializer.cs`（非并行任务文件）。
- **依赖契约**：`ISealedTicketService<T>`（主框架 `TKW.Framework.Domain.Ticket`）`IssueAsync(payload, ttl, options)` + `RedeemAsync(ticket, context)`（面① ADR109 D4——v4.10.77 TicketRedeemContext CAS）；`IChannelRegistry`（Federation——channelId → PlatformType 反查）；`SsoChannelEventContext.Parameters`（external_user_id/uid 富化——已存在）；`EventReplyResult.NewsArticles`（V0.1.1 图文扩展——并行任务已实现）；`FederatedIdentity`（主框架 `TKW.Framework.Domain`，已存在——`UId`/`ChannelId`/`ExternalUserId`/`ChannelType` 位置参数 record）。
- **⚠️ 并行任务依赖（P2-3 显式声明）**：本方案测试 handler 可达性依赖并行任务 `WeChatEventChannel.HandleEventAsync` 对**非 URL 验证事件返回 null**（委托 handler 迭代）。若并行任务维持"恒返回非 null"（EventOrchestrator 注释 L22-25 旧述），handler 迭代对 wechat_event 不可达、测试 handler 永不触发——需并行任务收尾时确认：业务事件（文本消息/关注等）返回 null、仅 URL 验证握手返回非 null（echostr 回显）。若不可达，需在 EventOrchestrator 增加显式 handler 委托分支（超出本方案范围，另立迭代）。
- **无外部依赖变更**：纯 Federation 主包内新增（基类 + 测试 handler + Options），零 NuGet 变更。

---

## 七、Oracle 评审结论与条件吸收（2026-10-15）

**裁定：APPROVE WITH CONDITIONS**——修复 6 项后实施。逐项吸收：

| 级别 | 评审发现 | 方案修订 |
|------|---------|---------|
| **P0-1** | `ExpectedClaimant=openId` 与测试端点 `RedeemAsync(code, claimant: null)` 不匹配 → 必致 `CLAIMANT_MISMATCH` | 改 `ExpectedClaimant = null`（可转让——accesscode 高熵随机码自保护） |
| **P0-2** | 载荷匿名对象 `{openId, uid, receivedAt}` 与测试端点 `RedeemAsync<FederatedIdentity>` 反序列化属性不匹配（openId≠ExternalUserId / uid≠UId）→ 兑换显示空值 | 载荷改 `FederatedIdentity` 序列化（`UId`/`ChannelId`/`ExternalUserId`/`ChannelType` 从 context.Parameters 填充；`Ttl=5min`） |
| **P1-1** | `TestEventReplyHandlerEnabled` 默认 true 违反 fail-safe 默认 + 与端点默认 false 不一致 + 生产误暴露 + handler 阴影 | 默认改 **false**（显式开启；与 `TestAccessCodeEndpointEnabled` 一致） |
| **P1-2/P2-1** | BaseUrl 空仍签 accesscode + 产出空 URL（浪费 + 死链） | `BuildReplyAsync` 内 `BaseUrl` 空 → 返回 null（不签不回复不拦截） |
| **P2-2** | `IEventReplyHandler` 注释 `ISealedTicketService<FederatedIdentity>` 与基类 `IAccessCodeService` 不一致 | 基类注释注明"经 `IAccessCodeService`（`ISealedTicketService<FederatedIdentity>` 的 IssueAsync 重载承载）签 accesscode" |
| **P2-3** | 测试 handler 可达性依赖并行任务 `WeChatEventChannel.HandleEventAsync` 对业务事件返回 null | §六 实施边界增加显式依赖声明 |

## 八、变更记录

| 日期 | 版本 | 内容 |
|------|------|------|
| 2026-10-15 | — | 方案初稿 → Oracle APPROVE WITH CONDITIONS（2 P0 + 2 P1 + 3 P2 全部吸收，修订后实施） |
| 2026-10-16 | v0.8.1 | **测试端点路径与 ticket 改名联动（用户裁定 4 点 + 删旧过渡）**：① 图文链接 BaseUrl 空 → 相对路径 `/feberation/…/test/…`（默认指向本服务同源，微信可点击）——原"空 = null"语义反转；② 端点路径对齐正常端点风格 `GET {prefix}/{platformId}/test/{channelId}/{ticket}`（event 改 test，含平台段 + 通道段 + ticket 段；原 `/test/accesscode/{code}` 废弃）；③ 测试端点返回 JSON 含 `channel_id`（取自签入载荷 `FederatedIdentity.ChannelId`，与选区分支无关）；④ AccessCode 命名全量改 ticket（承载面① `ISealedTicketService` 语义——路径段 + Options 键 `TestAccessCodeEndpointEnabled`→`TestTicketEndpointEnabled`/`TestAccessCodeEndpointBaseUrl`→`TestTicketEndpointBaseUrl`）；⑤ 附加：图文链接平台段经 `IChannelRegistry` 从 identity.ChannelId 反查 `PlatformType`（查不到 → 不回复）；URL 形态/路径/表同步更新 |
| 2026-10-16 | W2/模板化/W3 | **handler 优化方案 B（ReplyContext）+ 模板驱动两级模型 + 多通道绑定缝（用户定稿，转告框架组后推进）**：① **W2 方案 B**——基类入口按事件参数 `channel_id` 预查 `ChannelConfig`（一次查询）→ 组装 `ReplyContext(Context, Channel)` 传 `BuildReplyAsync`（子类零 registry 查询，直接读 `Channel.Extra`——决策输入齐备）；`BuildReplyAsync` 签名改收 `ReplyContext`；URL 拼装改用已查 cfg；② **模板驱动（两级模型第一级）**——默认 handler `TestEventReplyHandler` 读 `Channel.Extra["news_reply"]` 模板（JSON `{title, description, link, pic_url, ttl_seconds, enabled}` + 占位符 `{uid}/{openid}/{channel_id}/{channel_type}/{ticket}`）；`Link` 空 = 回退内置测试端点 URL、非空 = 签票后 `BuildTemplateUrl` 占位符注入（`{ticket}` 等）；模板缺失回退内置测试默认；`enabled=false` → 该通道不回复（null）；安全默认：开关关（默认）→ null（生产零影响，与现状一致）；③ **W3 绑定缝（后续扩展）**——新增可选标记接口 `IChannelSpecificEventReplyHandler`（`ChannelId` 属性，不改 `IEventReplyHandler` 契约）；`EventOrchestrator` 双桶解析（绑定桶优先 → ChannelType 桶回退）；默认 handler 不实现 = 所有 channelId 共用（并发多实例安全）；④ 测试 84→87（新增 W2 ReplyContext.Extra 断言/无 channel_id null/模板三用例/绑定三用例） |
