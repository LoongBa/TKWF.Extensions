# TKWF.Ext.Federation 联邦互联技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.11.0（2026-10-20 Iteration 2 渠道关注关系域——关注域 + 编排记录在后 + 免登入口；**待发布**） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain + `TKWF.Federation.Abstractions` 联邦契约包（I2-1 收口通道契约 + 关注域契约）+ `TKWF.Ext.TrustCenter.Abstractions` 信任契约包 + `TKWF.Ext.AuthCenter.Abstractions` 契约包（SsoProfileService 桥接消费）+ FreeSql + Microsoft.AspNetCore.App（`/feberation/*` 端点装配，V0.7.0 路由命名空间）+ Microsoft.Extensions.Caching.Memory

**中文名**: **联邦互联**（2026-10-05 用户裁定——"联邦认证"偏窄，落脚点"互联"宽于认证：Federation 层职责含应用注册/信任建立/身份映射/多 IdP 连接；与"认证中心"对仗：内部中心 + 外部互联）

⚠️ **重构说明**：信任内核（token2/accesscode/应用注册/`ISsoChannel` 契约定义）已迁 **`TKWF.Ext.TrustCenter`**——本包定位为**对外连接层壳**（通道选区/连接编排/`/feberation/*` 端点/外部 IdP 桥接）+ 多通道配置持久化。消费方引用/using 适配见 §八 破坏性变更段。

**核心约束**: 手写 ES256 JWT（零第三方 JWT 库）/ **独立密钥域**（ECDSA P-256 PEM + kid 轮换 + JWKS，与 Authentication RS256 完全独立）/ 授权码 accesscode（120s 单次原子 CAS + SHA256 存储 + PKCE 可选）/ 应用注册（origin 白名单防开放重定向 + scope + client credential AES-GCM + per-channel HMAC）/ 数据访问红线合规（全走 SG1 DataService）/ 经 AuthCenter.Abstractions 契约消费认证内核（组合式，L2 门控）/ **外部 IdP 桥接委托非双实现**（ExternalIdpAuthenticator 借道平台库通道——根因修复）

---

## 一、定位（2026-10-09 三层架构重定位）

认证体系**三层架构**（`TrustCenter剥离与三层架构-开发方案.md`）中的**对外连接层**——认证中心实例的对外**统一出口**：内网（AuthCenter 身份 + TrustCenter 信任）零外部协议依赖，Federation 承载所有对外连接（平台 IdP 接入、`/feberation/*` 端点、外部身份桥接）。

```
┌──────────── 内网（零外部协议依赖） ───────────────────────┐
│  AuthCenter（身份）── TrustCenter（信任·纯签发）            │
└──────────────┬────────────────────────────────────────────┘
               │ 经 Abstractions 契约（不引主包）
┌──────────────▼────────────────────────────────────────────┐
│  Federation（对外连接层壳）                                │
│   ├─ 装配面：Initializer+WebExtension（/feberation/* 端点）│
│   ├─ 多通道配置持久化（SsoChannelRegistryEntity）          │
│   ├─ 通道选区设施（IChannelRegistry 系列）                  │
│   ├─ 连接执行：平台库集合（实现 ISsoChannel）               │
│   ├─ 编排（SsoLogin + ISsoChannelFactory）                  │
│   ├─ ExternalIdpAuthenticator（实现 AuthCenter 桥接）      │
│   └─ profile API（server-to-server，scope 强制 + 审计）     │
└───────────────────────────────────────────────────────────┘
```

| 能力 | 说明 |
|------|------|
| 连接执行 | **平台库集合**（`TKWF.Federation.{平台}` 实现 Federation.Abstractions `ISsoChannel`——**I2-1 契约归属迁 FA**，平台库直引收口）经 DI 扩展方法装配进集合，**未装配库 = 空集合自然跳过**（F5） |
| 编排门面 | `ISsoLogin`（登录编排——认证→查号→建号→发码→换 token2）+ `ISsoChannelFactory`（按 channelId 构造通道实例）——经用户 `User.Use<>()` 帧内解析 |
| 多通道设施 | `IChannelRegistry`（按 ChannelId 精确选区——Composite：**DB 命中优先 → 静态回退**）+ `ChannelConfig`（公共列 + Extra 扩展字典）+ ChannelAlias 双键（对外别名）+ 静态/DB 双层来源 |
| DB 动态权威 | `SsoChannelRegistryEntity`（`TKWF_SsoChannelRegistry`——AppSecret/ExtraJson AES-GCM 密文列）+ `ISsoChannelRegistryService` 写入门面（Register/Update/Unregister——管理端点不建，服务层方法保留） |
| `/feberation/*` 端点 | `FederationWebExtension<TUserInfo>`（T3 装配面，IWebExtension）——**平台集成面**：`POST {prefix}/{platformId}/login`/`{prefix}/{platformId}/login/{channelId}`（登录编排——platformId=平台族 `ChannelConfig.PlatformType` 开放注册表，Ordinal 校验；多活跃通道省略通道段 → 400 `CHANNEL_REQUIRED`，单活跃通道降级）/ `POST {prefix}/{platformId}/event[/{channelId}]`（平台事件推送接收——channelType 按平台段权威推导 `{PlatformType}_event`）/ `GET {prefix}/{platformId}/oauth[/{channelId}]/callback`（IdP 回调，子应用委托——无通道段形态单活跃通道降级/多活跃通道 400 `CHANNEL_REQUIRED`）；**信任/委托面**（根级不挂平台段）：`GET {prefix}/jwks`（JWKS 公钥分发——信任内核职责经 `IToken2Service.GetJwksJson`）/ `POST {prefix}/authorize/start`·`POST {prefix}/trust/issue`·`POST {prefix}/identity/claim`（子应用委托 4 端点中根级 3 个——callback 已列上方，协议见 `子应用消费方接入-开发方案.md`）——匿名游客帧 + 配置分层 `TKWF:Federation:Web`（`RoutePrefix` 默认 `/feberation`） |
| 外部 IdP 桥接 | `ExternalIdpAuthenticator : IExternalIdpAuthenticator`（契约在 AuthCenter.Abstractions）——**委托平台库通道认证（协议单源，非双实现）**；AuthCenter 内网经 `IExternalIdpLoginService` 借道本桥接完成外部认证（根因修复：删除 AuthCenter 内微信 Provider 双源，P2 唯一事实源） |
| profile API | `ISsoProfileService`——server-to-server（scope 强制 + ILogger 审计 + 永不返回 openid/channel_id/phone） |
| 依赖方向 | 平台库 → `Federation.Abstractions`（I2-1 契约迁入后实现 `ISsoChannel`）+ `Federation`（装配）；Federation → `TrustCenter.Abstractions`（编排调签发）+ `AuthCenter.Abstractions`（桥接 + profile 消费）——单向无环 |

> **端点数对齐（实际 14 注册，2026-10-16 v0.8.1 核查）**：`FederationWebExtension.ConfigureEndpoints` 实际注册 **14 条**端点（`AllowAnonymous` 13 + 已认证 1）——平台集成面 6（login/event/oauth 各 1 + `channelId` 变体 3）+ 信任/委托面 4（jwks/authorize/start/trust/issue/identity/claim）+ **GET/POST event 双形态 2**（`GET {prefix}/{platformId}/event[/{channelId}]`）+ **test 端点 2**（`GET {prefix}/{platformId}/test/{channelId}/{ticket}`——v0.8.1 对齐正常端点风格：平台段 + 通道段 + ticket 段，选区经 `ResolveChannelAsync` 校验；原 `/test/accesscode/{code}` 废弃删）。`trust/issue` 无 `AllowAnonymous`（已认证——未认证 401，端点粒度精确判定）。
>
> **事件回复 handler 分层（2026-10-17 V0.9.0——方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS + ADR `docs/Federation/ADR/ADR-Federation-平台特定事件回复Handler基类与二级分发.md`）**：主包 `EventReplyHandlerBase` **保留不动**（平台无关**签票基类**——事件→签密封票据→图文链接，供"事件→登录链接"业务 handler 沿用）；**`TestEventReplyHandler` 已删除**（能力迁平台层 `WechatTemplateEventReplyHandler`/`WechatProbeEventReplyHandler`——见 Federation.WeChat README；Oracle P1-1：平台基类 `WechatEventReplyHandlerBase` **不继承**主包基类，两基类并列）；`TestEventReplyHandlerEnabled` 开关移除（`TestTicketEndpointBaseUrl`/`RoutePrefix` 保留）；handler 迭代唯一归口编排器 `EventOrchestrator`（双桶：绑定桶优先 → ChannelType 桶回退）；`IEventReplyHandler` 契约**零变化**（方案 §五 #1/#5、ADR D7）。

> **Iteration 2 渠道关注关系域（2026-10-20，V0.11.0 待发布——方案 `docs/Federation/I2-渠道关注关系域-开发方案.md` v0.5.1 Oracle APPROVE WITH CONDITIONS，P0-1/P1-1/P1-2/P1-3/P2-2 已闭环）**：**① 通道契约归属纠正（I2-1）**——`ISsoChannel.cs` 整文件自 `TrustCenter.Abstractions` 迁入 `Federation.Abstractions`（namespace 改 `TKWF.Ext.Federation`），FA 解除 TC 项目引用，依赖图单向化（TrustCenter（内核）⟂ Federation（连接层））；**② 传输/语义分层（I2-2）**——`SsoChannelEventContext`→`ChannelInboundContext`（传输层）+ `ChannelEvent`/`ChannelEventOutcome`（NotApplicable/Parsed/Rejected）/`ChannelEventDispatch`（语义层）+ `ChannelCapabilities`（[Flags] DIM 默认 None fail-closed——能力不足编排层直接跳过）；**③ 编排改造（I2-3/I2-6）**——`EventOrchestrator` 时序重构（解析→uid 只读查询→handler 业务回复优先→`FollowStateService.RecordAsync` **记录在后**（R3，防 PG 事务中毒））+ `FollowChangedEvent` 域事件（仅 `StateChanged==true` 时经 `User.GetService<ILocalEventBus>()` 发布，UoW 提交后派发）+ 陈旧注释修复；**④ 关注域实体（I2-4）**——`FollowEventEntity`（`TKWF_FollowEvent` 追加写 + DedupeKey UNIQUE 幂等）+ `FollowStateEntity`（`TKWF_FollowState` 聚合根——`(ChannelId, ExternalUserId)` UNIQUE + `LastEventAt` 排序守卫两段比较，旧事件不覆盖新状态）；**⑤ 关注域门面（I2-5）**——`IFollowStateService`（契约在 FA）+ `FollowStateService` 实现（RecordAsync 幂等/GetAsync/GetByExternalUserIdAsync/GetDailyStatsAsync/RebuildAsync/PurgeEventsAsync——经 SG1 DataService 组合零手写 Store）+ `ChannelEvent.ReceivedAt`；**⑥ 微信免登闭环（I2-8）**——`ILoginEntryService`（契约在 FA——`BuildTicketUrlAsync`/`BuildSilentAuthorizeUrlAsync`，实现建立在框架 `ISealedTicketService<FederatedIdentity>` 之上，基址复用 `FederationEventReplyOptions` 单一事实源）+ `FederationLoginEntryOptions`（`TKWF:Federation:LoginEntry`——Enabled=false 默认/TicketPath=/login/ticket/TicketTtlSeconds=300/ChannelBaseUrls）+ `EventReplyHandlerBase` URL 组装委托（逐字节等价）；平台库 `WeChatChannelSource.Capabilities` 声明（Event|Follow|FollowQuery|Menu+SilentAuthorize）+ `IFollowChannelQuery`（cgi-bin/user/info 反查——状态修复/对账，不在事件路径调用）；**测试 Federation.Tests 114 用例全绿**（79→114：+35 关注域/编排/域事件/免登；WeChat 110 / WeChatOperations 143+2 受影响面全绿）。

**组合矩阵**（消费方按需装配，4 档装配梯度——见使用指南 §一 组合矩阵）：
- 只引 `TKWF.Ext.AuthCenter` = AuthCenter 内部认证（单应用登录）
- **AuthCenter + TrustCenter = 信任中枢实例**（内部认证 + 跨应用 token2/accesscode，内网两层，无外连端点）
- **+ TrustCenter.Abstractions 契约消费**（业务层直连信任契约——`IToken2Service`/面① `ISealedTicketService<T>`/面② `IAuthorizationCodeService` 经 L2 间接层）
- **AuthCenter + TrustCenter + Federation + 平台库 = 认证中心完整形态**（内网两层 + 对外连接层 + 平台 IdP 接入）

**不包含**：具体平台适配扩展（微信/QQ/Google/Apple/微软 channel 实现，独立立项 `TKWF.Federation.{平台}`，引目标 Federation.Abstractions）；**信任内核**（token2/accesscode/应用注册 → `TKWF.Ext.TrustCenter`）；我方多实例互通（默认不做，iss 域隔离）。

## 二、安装与接线

### 1. 消费方引用 + 白名单启用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\TrustCenter\TKWF.Ext.TrustCenter.csproj" />   <!-- 组合式：信任内核（token2/accesscode） -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\AuthCenter\TKWF.Ext.AuthCenter.csproj" />    <!-- 组合式：认证中心实例 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\TrustCenter.Abstractions\TKWF.Ext.TrustCenter.Abstractions.csproj" />  <!-- 信任契约包（编排类型引用） -->
```

```csharp
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;    // 信任契约命名空间（IToken2Service/ISsoChannel/IAuthorizationCodeService——随内核迁出；面① ISealedTicketService<T> 在主框架 Domain.Ticket）

[TKWFEnabledExtension(typeof(TrustCenterExtensionInitializer<>))]   // 信任内核（token2/accesscode）
[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]    // 对外连接层（编排/端点/桥接）
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]   // 组合式：认证中心实例
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

### 2. 接入平台网关库（`TKWF.Federation.{平台}`）——外交部经 `ISsoChannel` 集合窄适配编排

Federation 连接层**拥有 `ISsoChannel` 集合编排权**（契约在 Federation.Abstractions，Oracle P1-1——**I2-1 契约归属迁 FA**），但**不实现任何通道**——通道由平台网关库（`TKWF.Federation.WeChat`/`.QQ`/`.Google`/...）实现并经其 DI 扩展方法注册进集合（开发方案 §5.4 Oracle P1-4）。消费方装配链路：

```csharp
// ① 消费方（认证中心实例）领域初始化器——白名单声明 TrustCenter + Federation（三钩子接线）
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

[TKWFEnabledExtension(typeof(TrustCenterExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]   // 组合式：认证中心实例
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// ② Program.cs（Web 装配层）——装配 TKWF.Federation.WeChat 平台网关库
//    库侧 AddWeChatFederationChannels() 内部 TryAddEnumerableConstructible<ISsoChannel, X>（ADR92 集合版守卫工厂）
builder.Services.AddWeChatFederationChannels();
//    通道配置经 TKWF:Federation:WeChat 配置节绑定（SG1 [Options] 自动），或编程覆盖：
builder.Services.Configure<WeChatOptions>(o => { /* 按 AppId 选区 Channels */ });

// ③ 装配 FederationWebExtension（/feberation/* 端点——T3 装配面，锚点 AfterRouting）
builder.ConfigWebAppDomain<MyUserInfo, MyDomainInitializer, DomainWebOptions>(...)
    .UseWebSession()
    .UseWebExtensions(e => e.Add<FederationWebExtension<MyUserInfo>>(x =>
    {
        x.ConfigureOptions = o => { /* 可编程覆盖端点 Options（TKWF:Federation:Web） */ };
    }))
    .Build(...);

// ④ 编排——端点 handler 经游客帧 guest.Use<门面>() 帧内枚举/构造 ISsoChannel：
var result = await user.Use<ISsoLogin>().LoginAsync(channelId, context, ct);
//    ⛔ 禁止 [FromServices] IEnumerable<ISsoChannel> 预绑定（帧外枚举抛守卫）
```

- **依赖方向**：`TKWF.Federation.{平台}` 库 → `TKWF.Ext.TrustCenter.Abstractions`（实现 `ISsoChannel`，**L2 门控——引目标随内核迁出更新，方案 §6.1 #1**）+ `TKWF.Ext.Federation` 扩展 → `TrustCenter.Abstractions` + `AuthCenter.Abstractions` + 主框架（Oracle P1-1，单向无环）。
- **未装配库 = 空集合**：Federation 初始器零 `ISsoChannel` 元素注册——未引用任何平台网关库时 `IEnumerable<ISsoChannel>` 帧内解析为空数组，"未注册通道自然跳过"（验收 F5）；装配库后元素自动进集合。
- **注册责任归属**：库无 Initializer（纯库判据）——由库侧 `AddWeChatFederationChannels()` 扩展方法注册（`Microsoft.Extensions.DependencyInjection` 命名空间）；Federation 扩展不强制引库保持连接层轻量。
- **⚠️ 装配约束**：`FederationWebExtension` **须与 `UseWebSession` 同装配**（ContextExtraction 阶段 2 写游客 DomainUser——匿名端点 handler 经游客帧 `guest.Use<门面>()` 调用守卫工厂门面；未装配 → 抛守卫 = 正确 fail 非静默降级）。

### 3. 配置（连接层两节 + 端点节）

```jsonc
{
  "TKWF": {
    "Federation": {
      // ⚠️ 信任内核配置已迁 TKWF:TrustCenter 节（Issuer/SigningKeyPath/CurrentKid/SigningKeys/AccessCode*/SecretEncryptionKeyPath/IsProduction）

      "ChannelRegistry": {                 // 连接层通道注册表加密密钥节（TrustCenter 剥离后独立承载）
        "SecretEncryptionKeyPath": "C:\\keys\\sso-channel-registry-aes.key",  // SsoChannelRegistryEntity AppSecret/ExtraJson AES-GCM（前 32 字节，生产必填）
        "IsProduction": true
      },
      "Web": {                             // 对外端点表现层（FederationWebExtension，配置分层 AGENTS §8——平台集成面含 {platformId} 平台段，信任/委托面根级）
        "RoutePrefix": "/feberation",        // 端点挂此前缀（V0.7.0 默认 /feberation，可配）
        "LoginEndpointEnabled": true,      // POST {prefix}/{platformId}/login[ /{channelId}]
        "JwksEndpointEnabled": true,       // GET {prefix}/jwks（根级）
        "EventEndpointEnabled": true,      // POST {prefix}/{platformId}/event[/{channelId}]
        "AuthorizeEndpointEnabled": true,  // POST {prefix}/authorize/start（子应用委托，根级）
        "OauthCallbackEndpointEnabled": true, // GET {prefix}/{platformId}/oauth[/{channelId}]/callback
        "TrustIssueEndpointEnabled": true,   // POST {prefix}/trust/issue（子应用委托，根级）
        "IdentityClaimEndpointEnabled": true // POST {prefix}/identity/claim（子应用委托，根级）
      }
      // 平台库各自配置节：TKWF:Federation:WeChat / :QQ / ...（Channels 列表）
    }
  }
}
```

> ⚠️ **配置节变更（破坏性）**：原 `TKWF:Federation` 签发/授权配置（Issuer/SigningKeyPath/CurrentKid/SigningKeys/AccessCodeExpirationSeconds/SecretEncryptionKeyPath/IsProduction）**迁移至 `TKWF:TrustCenter`**（·TrustCenter 的 token2/accesscode/密钥路径）；Federation 连接层仅保留通道注册表节 `TKWF:Federation:ChannelRegistry`（AES-GCM 密钥路径）+ 平台库通道节 + 端点节 `TKWF:Federation:Web`。生产 fail-fast 语义不变（按各自 `IsProduction` 门）。

**生成 EC 私钥 PEM**（生产，PKCS#8——消费方改为 TrustCenter 配置）：
```bash
openssl ecparam -name prime256v1 -genkey -noout -out sso-ec-private.pem   # PKCS#8 EC 私钥
openssl ec -in sso-ec-private.pem -pubout -out sso-ec-public.pem
```

### 4. 消费（应用端起——token2 契约经 TrustCenter）

```csharp
using TKWF.Ext.TrustCenter;    // 信任契约命名空间（随内核迁出）

// 门面帧内解析（AddConstructibleService 守卫工厂——禁止 [FromServices] 预绑定）
public class AppSessionService(DomainUser<MyUserInfo> user)
{
    // token2 验签（应用端离线验证：ES256 + JWKS 公钥；aud 须 == 自身 app_id）——信任内核职责
    public async Task<Token2ValidationResult> VerifyAsync(string token2)
        => await user.Use<IToken2Service>().ValidateToken2Async(token2);

    // profile 拉取（server-to-server：Bearer app client credential + X-App-Id）——连接层职责
    public async Task<SsoProfileDto?> GetProfileAsync(string appId, string uid, string[] scopes)
        => await user.Use<ISsoProfileService>().GetProfileAsync(appId, uid, scopes, null);
}
```

## 三、令牌契约（token2，ES256——**已迁 TrustCenter，见其 README §三**）

token2 签发/验签契约（`IToken2Service`/`Token2IssueRequest`/`Token2ValidationResult`）随信任内核迁入 **`TKWF.Ext.TrustCenter`**（命名空间 `TKWF.Ext.TrustCenter`）。契约形态不变（ES256 + kid 轮换 + JWKS）；Federation 编排链经 `User.Use<IToken2Service>()` 调签发。**消费方 token2 相关类型引用改 `using TKWF.Ext.TrustCenter;`**。契约详情移师 `_Framework/TrustCenter/README.md` §三。

```json
{ "alg": "ES256", "kid": "sso-key-2026-10", "typ": "JWT",
  "iss": "https://api.lexue.loongba.cn", "sub": "<uid>", "aud": "<target_app_id>",
  "iat": 1722240000, "exp": 1722240300, "jti": "<32B base64url>", "scope": "profile:basic" }
```

## 四、accesscode 契约（授权码 + 安全数据投递——**拆分命名 ADR109 D4 后已删，改两面契约**）

> **⚠️ 2026-10-16 拆分命名（ADR109 D4）落地 + v0.8.1 删旧过渡（用户裁定）**：旧 `IAccessCodeService`/`AccessCodeIssueRequest`/`AccessCodeEntity`/`AccessCodeService` 已删除——accesscode 概念拆两面，消费方按语义选用：
> - **面① 密封票据 + 安全数据投递**：`ISealedTicketService<T>`（主框架 `TKW.Framework.Domain.Ticket`）+ TrustCenter `SealedTicketService<T>` DB 后端（表 `TKWF_SealedTicket`）——`IssueAsync(payload, ttl, options)` / `RedeemAsync(ticket, context)` / `CleanupExpiredAsync`；Federation 事件链路与子应用委托面均消费此契约；配置键 `SealedTicketExpirationSeconds`/`SealedTicketRetentionDays`。
> - **面② OAuth 授权码**：`IAuthorizationCodeService`（`TrustCenter.Abstractions`）+ `AuthorizationCodeService`（表 `TKWF_AuthorizationCode`）——`IssueAsync(request)` / `ConsumeAsync(code, verifier, ip)` / `CleanupExpiredAsync`（SSO 联邦流）；配置键 `AuthorizationCodeExpirationSeconds`/`AuthorizationCodeRetentionDays`。
>
> 契约详情移师 `_Framework/TrustCenter/README.md` §四。**消费方引用改 `using TKWF.Ext.TrustCenter;`（面②）/ `using TKW.Framework.Domain.Ticket;`（面①）**。子应用消费方委托面（`SsoSubAppBridge`）已迁面①密封票据（`ISealedTicketService<FederatedIdentity>`）——子应用协议 `code` 参数 = ticket。

**表名变更（破坏性）**：`TKWF_SsoAccessCode` → **`TKWF_AccessCode`**（2026-10-09 剥离时改名）→ **2026-10-16 拆分后 `TKWF_SealedTicket`/`TKWF_AuthorizationCode` 双表**（旧 `TKWF_AccessCode` 不复存在）。存量 DBA 依消费方实际使用场景迁移（面①/面②取舍，若存量，方案 ADR109 D4）。

**错误码（面① 密封票据契约不泄露判因——`RedeemAsync` 失败返回 null）**：子应用 `ACCESS_CODE_NOT_FOUND`（兑现失败）/ `TICKET_STATE_MISMATCH`（PKCE——面②）等；测试端点 `TICKET_NOT_FOUND`（v0.8.1）。

**与 AuthCenter `IOAuthTicketService` 边界**（并列不迁移）：既有 = 单应用票据（login/bind + PKCE + TTL 5min，服务认证中心内部"票据换令牌"）；accesscode = 联邦互联授权码 + 安全数据投递（channel_id + target_app_id + scope + Payload）——语义不同，各自独立（ADR-SSO 3.3 并存路径）。

## 五、安全边界

- **数据访问红线合规**：全部实体 SG1 + `*EntityDataService` 委托（零 IFreeSql / 零 IEntityDAC 直注入）；Federation 经 Abstractions 契约消费信任内核（`IToken2Service`/面① `ISealedTicketService<T>`/面② `IAuthorizationCodeService`）与认证内核（`ISsoAccountQueryService`），敏感字段不出契约包。
- **密钥安全**：信任内核（token2 EC 私钥 PEM / AccessCode AES-GCM）密钥归 **TrustCenter**（`TrustCenterOptions`）管理——生产缺密钥 fail-fast；连接层通道注册表（`SsoChannelRegistryEntity` AppSecret/ExtraJson）归 **Federation** keyed `ISymmetricKeyProvider`（`SymmetricKeyProviderKeys.Federation`——主框架常量已存在直接复用）。kid 轮换支持紧急换钥。
- **防开放重定向**：`target_app_id → 注册精确 origin（scheme+host）` 白名单（TrustCenter `SsoClientService.IsOriginAllowedAsync`，Ordinal 精确匹配）；不接受自由 `redirect_uri` 参数。
- **外部 IdP 桥接 fail-hard（P7）**：`IExternalIdpAuthenticator`（契约在 AuthCenter.Abstractions）实现注册于 Federation——未装配 Federation 时 AuthCenter `User.Use<IExternalIdpAuthenticator>()` 抛守卫（不静默降级）；桥接**委托平台库通道认证（协议单源非双实现）**。
- **`/feberation/*` 端点匿名** + 游客帧（`guest.Use<门面>()`，须 UseWebSession 同装配）；事件端点只中转——事件验签由平台库通道内部完成（WeChatEventCrypto 一票否决）。
- **profile 审计**：ILogger 结构化日志（app_id/uid/scopes/ip/ts）——SsoProfileAuditEntity 归后续迭代（复用 SecurityLog 或自建表另议）。

## 六、架构决策记录

- 三层架构开发方案：`docs/TrustCenter/TrustCenter剥离与三层架构-开发方案.md`（Oracle 初评 PASS WITH CONDITIONS + 批判性复评附条件通过——5 项条件全部吸收）
- 既有 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（四层归层模型——本方案在其上三层化演进）；`docs/SSO/ADR/ADR-SSO-模块立项与契约归属.md`（令牌契约与密钥管理基础）
- 需求设计文档：`_TCloud/docs/协作/记录/20261005-01-认证中心设计方案.md`（v4，将归档）

## 七、版本记录

| 版本 | 内容 |
|------|------|
| V0.11.0（2026-10-20，**待发布**） | **Iteration 2 渠道关注关系域（方案 `docs/Federation/I2-渠道关注关系域-开发方案.md` v0.5.1 Oracle APPROVE WITH CONDITIONS——P0-1/P1-1/P1-2/P1-3/P2-2 闭环）**：① **契约归属纠正（I2-1）**——`ISsoChannel.cs` 迁入 `Federation.Abstractions`（namespace 改 `TKWF.Ext.Federation`），FA 解除 TC 项目引用；② **传输/语义分层（I2-2）**——`SsoChannelEventContext`→`ChannelInboundContext` + `ChannelEvent`/`ChannelEventOutcome`/`ChannelEventDispatch` + `ChannelCapabilities`（[Flags] DIM None fail-closed）；③ **编排改造（I2-3/I2-6）**——`EventOrchestrator` 记录在后（R3——handler 业务回复优先，`DedupeKey` 冲突不先中毒事务；附条件：并发重投同事务业务写会回滚——接受的已知代价）+ `FollowChangedEvent` 域事件（仅 `StateChanged==true` 经 `User.GetService<ILocalEventBus>()` 发布）+ 陈旧注释修复；④ **关注域实体（I2-4）**——`FollowEventEntity`（`TKWF_FollowEvent` 追加写 + DedupeKey UNIQUE）+ `FollowStateEntity`（`TKWF_FollowState` 聚合根——UNIQUE + `LastEventAt` 排序守卫两段比较）；⑤ **关注域门面（I2-5）**——`IFollowStateService` + `FollowStateService`（Record/Get/GetByExternalUserId/GetDailyStats/Rebuild/Purge——经 SG1 DataService 零手写 Store）+ `ChannelEvent.ReceivedAt`；⑥ **微信免登闭环（I2-8）**——`ILoginEntryService`（框架 `ISealedTicketService<FederatedIdentity>` 之上，基址复用 `FederationEventReplyOptions` 单一事实源）+ `FederationLoginEntryOptions`（`TKWF:Federation:LoginEntry`——Enabled 默认 false/TicketPath=/login/ticket/Ttl=300/ChannelBaseUrls）+ `EventReplyHandlerBase` URL 组装委托（逐字节等价）；**测试 Federation.Tests 114 用例全绿**（79→114：+35 关注域/编排/域事件/免登） |
| V0.9.0（2026-10-17） | **事件回复测试设施迁移（`TestEventReplyHandler` 替换——方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS）**：`TestEventReplyHandler` **删除** + `FederationEventReplyOptions.TestEventReplyHandlerEnabled` **移除**（探针开关迁平台库 `TKWF:Federation:WeChat:EventReply:ProbeEventReplyHandlerEnabled`）+ `EventReplyHandlerBase.TestEventReplyHandlerEnabled` protected 属性移除（仅 TestEventReplyHandler 消费——grep 核查）；`EventReplyHandlerBase` **保留不动**（平台无关签票基类）；`TestTicketEndpointBaseUrl`/`RoutePrefix` 保留（主包 `FederationEventReplyOptions`）；`NewsReplyTemplateKey` 常量迁平台层（Oracle P2-2）；`IEventReplyHandler` 契约零变化；破坏性（编译）——消费方若注册/继承测试 handler → 改平台层 Template/Probe（方案 §五 #3）；Federation.Tests 79 + WeChat.Tests 86 全绿 |
| V0.8.1（2026-10-16） | **测试端点路径与 ticket 改名联动（用户裁定 4 点 + 删旧过渡）**：测试端点 `GET {prefix}/test/accesscode/{code}` → **`GET {prefix}/{platformId}/test/{channelId}/{ticket}`**（对齐正常端点风格——平台段 + 通道段 + ticket 段，选区经 `ResolveChannelAsync` 校验，防跨平台错配；原路径废弃）；`TestAccessCodeEndpointEnabled`/`TestAccessCodeEndpointBaseUrl` → `TestTicketEndpointEnabled`/`TestTicketEndpointBaseUrl`（承载面① `ISealedTicketService` ticket 语义）；`HandleTestAccessCode` → `HandleTestTicket`（返回 JSON 含 `channel_id`——取自签入载荷 `FederatedIdentity.ChannelId`，与选区分支无关；错误码 `TICKET_NOT_FOUND`）；图文链接 `BuildTestAccessCodeUrl` → `BuildTestTicketUrl(ticket, platformId, channelId)`——platformId 经 `IChannelRegistry` 从 identity.ChannelId 反查 `PlatformType`（查不到 → 不回复），BaseUrl 空 → 相对路径 `/feberation/…/test/…`（默认指向本服务同源，微信可点击；原"空 = null"语义反转）；TrustCenter 旧 `IAccessCodeService`/`AccessCodeEntity`/`AccessCodeService` 过渡保留全删（用户裁定：项目组已适配，零兼容层）；端点数对齐 14 注册 |
| V0.7.0（2026-10-11） | **对外路由命名空间迭代（`/sso` → `/feberation` + 平台段，方案 `docs/Federation/对外路由命名空间-开发方案.md`）**：根前缀默认 `/feberation`（`FederationEndpointOptions.RoutePrefix` 可配保留）；平台集成面二级平台段 `{platformId}`（= `ChannelConfig.PlatformType` 开放注册表 `wechat/qq/dingtalk/wecom/alipay/oidc/google/microsoft`，Ordinal 校验防跨平台错配 → 统一 404 `CHANNEL_NOT_FOUND`）+ 三级端点组（login/oauth/event）+ 四级通道实例；信任/委托面根级不挂平台段（`jwks`/`authorize/start`/`trust/issue`/`identity/claim` 仅换前缀）；**B 守卫**（多活跃通道省略通道段 → 400 `CHANNEL_REQUIRED`，单活跃通道降级）；**event 缺陷修复**（channelType 按平台段权威推导 `{PlatformType}_event`——原 null 分支白名单排除 `*_event` 致通道不可达）；alias 全局解析 + 平台段额外校验层；`TKWF:Federation:Web` 端点开关全集收录（含子应用委托 4 开关 `Authorize/OauthCallback/TrustIssue/IdentityClaimEndpointEnabled`，承接 `子应用消费方接入-开发方案.md`——README 首次收录） |
| V0.5.0（2026-10-09，**待发布**） | **三层架构重构（TrustCenter 剥离，连接层壳重定位，Phase 1-4）**：信任内核（token2/accesscode/应用注册/`ISsoChannel` 契约定义）迁出至 `TKWF.Ext.TrustCenter`（MinVerTagPrefix `TrustCenter/v`）；平台库引目标改 `TrustCenter.Abstractions`（包名保留 `TKWF.Federation.{平台}`）；**新建 `FederationWebExtension<TUserInfo>`**（T3 装配面——`POST /sso/login[ /{channelId}]`/`GET /sso/jwks`/`POST /sso/event/{channelId}` + `FederationEndpointOptions` `TKWF:Federation:Web` 配置分层）；编排面（`ISsoLogin`/`ISsoChannelFactory`）+ 多通道设施（`IChannelRegistry` 系列/`SsoChannelRegistryEntity`/`SsoChannelRegistryService`）**保留归本包**；**新建 `ExternalIdpAuthenticator : IExternalIdpAuthenticator`**（AuthCenter.Abstractions 契约——委托平台库通道认证非双实现，根因修复）；`FederationChannelRegistryOptions` `TKWF:Federation:ChannelRegistry` 独立承载通道注册表密钥；配置节拆分（签发配置迁 `TKWF:TrustCenter`）；破坏性变更段 §八；测试重划（Federation.Tests 连接层 + TrustCenter.Tests 信任内核）+ 全量回归 0 失败（Phase 1-4 累计 1913 用例） |
| V0.4.0（2026-10-08/10） | **多通道联邦 Phase 2/3（DB 动态权威 + ChannelAlias 双键）**：`SsoChannelRegistryEntity`（`TKWF_SsoChannelRegistry`——AppSecret/ExtraJson AES-GCM 密文列）+ `DbChannelRegistry` + `CompositeChannelRegistry`（DB 命中优先 → 静态回退）+ `ISsoChannelRegistryService` 写入门面（Register/Update/Unregister）；`ChannelConfig.Alias` 双键 + `IChannelRegistry.GetByAliasOrIdAsync`（alias 精确匹配 → ChannelId）+ `SsoChannelRegistryEntity.ChannelAlias` 列；（阶段说明详见使用指南 §3.5b/§3.5c；tag Federation/v0.4.0 2026-10-10） |
| V0.3.0（2026-10-07/08） | **多通道联邦 Phase 1**（方案 `docs/Federation/多通道联邦-开发方案.md` Oracle PASS）：`IChannelRegistry`/`StaticChannelRegistry`/`CompositeChannelRegistry` + `IChannelSource`（平台库投影）+ `ISsoChannelFactory`（按 channelId 构造）+ `ISsoLogin`（编排门面 + 默认降级）——替代 6 平台库 14 处 `Channels.FirstOrDefault()`；凭证模型公共列（AppId/AppSecret/IsDefault/IsEnabled）+ Extra 扩展字典；41 项目 1888 用例全绿（tag Federation/v0.3.0 2026-10-08） |
| V0.2.0（2026-10-06） | **E4 密钥管理抽象**（框架 v4.10.61 配套）：删 `FederationSecretKeyStore`/`DevEcKeyCache` 静态密钥类——密钥迁移框架 `ISymmetricKeyProvider`（keyed "Federation"，`SymmetricKeyProviderKeys.Federation`）+ `DevKeyCache<Token2Service.EcKeySet>`（Utility.Caching）；删 `FederationSecretKeyStore` 死钩子 `ResetForTests`（零调用）；AES-GCM 格式统一单段（AeadEncryptionUtil）；23 测试全绿 + 全量回归通过 |
| V0.1.1（2026-10-05） | **Development 模式启动崩溃修复**（同 AuthCenter V0.5.4 defect——方案 A' 复制链）：`InitializeAsync` 的 `BeginSystemScopeAsync(sp)` 传 root 不建子 scope，ValidateScopes（Development）下 Scoped 守卫工厂从根解析必崩 → 改不传参（框架内部 CreateScope 建子 scope） |
| V0.1.0（2026-10-05） | Federation 联邦层内核（归层后命名，原 SSO）：应用注册（origin 白名单 + scope + AES-GCM credential + per-channel HMAC）/ accesscode（120s 原子 CAS + SHA256 + PKCE）/ token2（手写 ES256 独立密钥域 + kid 轮换 + JWKS）/ profile API（scope 强制 + 审计）/ ISsoChannel 契约；**以上信任内核已随 V0.5.0 迁 TrustCenter**；前置 `Authentication.Abstractions` 契约包拆出（Federation 消费面：ISsoAccountQueryService/ISsoChannelMapService + DTO，零实体零 SG1）；联盟锚点数据模型（AuthAccount.FederationAnchorOpenId 列 + PlatformAccountMap 扩展）；19 测试全绿 + 全量回归通过 |

## 八、破坏性变更与消费方适配（2026-10-09 三层架构——用户裁定通知各方适配）

| # | 变更 | 级别 | 消费方适配 |
|---|------|------|-----------|
| 1 | **命名空间迁移（token2/accesscode/ISsoChannel）** | 破坏性（编译） | `using TKWF.Ext.Federation` → **`using TKWF.Ext.TrustCenter;`**（`IToken2Service`/`IAccessCodeService`/`ISsoChannel`/`SsoChannelAuthContext`/`SsoChannelAuthResult` 等类型）+ 项目引用增 TrustCenter/TrustCenter.Abstractions |
| 2 | **配置节 `TKWF:Federation` 签发配置** | 破坏性（配置） | Issuer/SigningKeyPath/CurrentKid/SigningKeys/AccessCodeExpirationSeconds/SecretEncryptionKeyPath/IsProduction → **`TKWF:TrustCenter` 节**；Federation 连接层仅留 `TKWF:Federation:ChannelRegistry`（新增）/`TKWF:Federation:Web`（新增）/平台库通道节（不变） |
| 3 | **表名 `TKWF_SsoAccessCode` → `TKWF_AccessCode`** | 破坏性（DB） | 存量 DBA RENAME（若存量）；SyncStructure 开发环境自动 |
| 4 | **平台库引目标改 `TrustCenter.Abstractions`** | 破坏性（依赖） | 平台库 csproj 由引 `TKWF.Ext.Federation` 改引 `TKWF.Ext.TrustCenter.Abstractions`（实现 `ISsoChannel`，L2 门控）；包名/命名空间 `TKWF.Federation.{平台}` 保留不变 |
| 5 | **`TKWF.Ext.Federation` 信任内核 API 移除** | 破坏性（API） | `IToken2Service`/`ISsoAccessCodeService`→`IAccessCodeService`/`ISsoClientService`/`SsoAccessCodeService`/`Token2Service` 定义迁 TrustCenter（Federation 侧不经 `ISsoClientService`——信任注册归 TrustCenter） |
| 6 | **白名单声明组合更新** | 破坏性（装配） | 认证中心实例：`[TKWFEnabledExtension]` 增声明 `TrustCenterExtensionInitializer<>`（信任内核）+ `FederationExtensionInitializer<>`（连接层）——内部信任域独立启用场景只声明 TrustCenter |
| 7 | **NuGet 包线** | 发布 | 新线 `TKWF.Ext.TrustCenter` 独立 tag 独立发布（待框架组 SymmetricKeyProviderKeys 并入后统一发布）；`TKWF.Ext.Federation` 旧包 unlist 计划（清单见 `docs/TrustCenter/转告-SymmetricKeyProviderKeys申请并入TrustCenter键.md` 附录，用户核查 nuget.org 后 unlist）；8 平台库保留不 unlist |
| 8 | **`ISsoChannel.cs` 契约归属迁 `Federation.Abstractions`（I2-1，2026-10-20）** | 破坏性（编译） | `ISsoChannel`/`SsoChannelAuthContext`/`SsoChannelAuthResult`/`SsoChannelAuthorizeContext`/`SsoChannelAuthorizeResult`/`EventReplyResult`/`EventReplyNewsArticle` 自 `TrustCenter.Abstractions` 迁入 `TKWF.Federation.Abstractions`——消费方 `using TKWF.Ext.TrustCenter;` 改/增 `using TKWF.Ext.Federation;` + 契约包改引 `TKWF.Federation.Abstractions`；Google/Microsoft 平台库引用替换（TC→FA） |
| 9 | **传输/语义分层（I2-2）** | 破坏性（编译） | `SsoChannelEventContext` → `ChannelInboundContext`（传输层）；`ISsoChannel.HandleEventAsync` 返回 `ChannelEventOutcome`（NotApplicable/Parsed/Rejected）；`IEventReplyHandler.HandleAsync(ChannelEventDispatch)`；`IEventOrchestrator.OrchestrateAsync(channelId, channelType, ChannelInboundContext)`——事件通道实现/消费方 handler 重写解析段 |
| 10 | **新契约进 `Federation.Abstractions 0.2.0`（关注域 + 免登 + 能力）** | 非破坏（新增） | `IFollowStateService`（+ `FollowRecordResult`/`FollowStateDto`/`FollowDailyStatDto`）/`IFollowChannelQuery`（+ `FollowStateSnapshot`）/`FollowChangedEvent`/`ILoginEntryService`（+ `FederationLoginEntryOptions`）/`ChannelEvent` 系列/`ChannelCapabilities`/`IChannelSource.Capabilities`（DIM 默认 None fail-closed）——消费方按需使用；配置新增 `TKWF:Federation:LoginEntry` 节 |

> **用户裁定（2026-10-10）**：迁移**不必考虑兼容性**——通知各方适配即可；`SymmetricKeyProviderKeys.TrustCenter` 等框架组适配**发布后统一发布**。

<!-- EOF -->