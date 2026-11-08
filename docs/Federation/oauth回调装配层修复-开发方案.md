# OAuth 回调装配层修复-开发方案

> **版本**：v0.1.1（v0.1 → Oracle 评审 PASS WITH CONDITIONS 条件逐条闭环修订）
> **日期**：2026-11-09
> **状态**：Oracle 评审 **PASS WITH CONDITIONS**（2026-11-09，1 P0 / 2 P1 / 若干 P2）→ 条件已逐条闭环进正文（见「变更记录」v0.1.1 行）
> **来源需求**：主框架 `_TKWF/docs/03_扩展模块/阶段性评审/扩展模块-阶段性评审记录-2026-11-08.md` 批次 9 平台库组评审——**QQ-1（P0）/ AL-1（P0）** 装配链路断链 + 评审后记（L610）"与 Oidc 系同类问题合并一次 C 档立项"
> **改动面**：`Federation`（主包装配层，本方案主体）· `Federation.QQ` / `Federation.Alipay` · `Federation.Oidc`（通道兼容双键/软读对齐）· 测试四项目（Federation / QQ / Alipay / Oidc）

---

## 1. 背景与问题

### 1.1 评审问题（QQ-1 / AL-1 / OIDC 段落全文核对）

| # | 级别 | 位置（评审记录原文） | 问题 | 建议 |
|---|:---:|------|------|------|
| QQ-1 | **P0** | 评审记录 L595：QqOauthChannel L68-70 × FederationWebExtension L327-329 × SsoSubAppBridge L126 | 装配链路断链：README 声称"装配层注入 redirect_uri"无实现——`HandleOAuthCallback` 原样透传 query，QQ 302 回调仅 code+state 无 redirect_uri → `QQ_REDIRECT_URI_REQUIRED` 必败；微信通道不要求 redirect_uri（对照通） | ① 装配层注入当前请求 URL 作 redirect_uri；② 或文档明示自建适配端点 |
| AL-1 | **P0** | 评审记录 L596：AlipayOauthChannel L69-71/78-80 × 同 QQ-1 装配层证据 | 装配链路断链（双重）：auth_code 键未归一为 code（全仓 grep 零实现）+ redirect_uri 无注入 → `ALIPAY_CODE_REQUIRED` 必败 | ① 装配层注入；② Alipay 通道兼容 `code ?? auth_code` 双键；③ README 如变化（三管齐下） |
| 评审后记 | — | 评审记录 L610 | QQ-1/AL-1 与 Oidc 系同类（OidcChannelBase L86 同读 redirect_uri + 同装配层）——**合并一次 C 档立项**：Federation 装配层 HandleOAuthCallback 注入 redirect_uri + auth_code→code 归一 + 通道兼容双键 | 一次立项收口三平台 |

> **OIDC 关联问题（本次不实施，原理同源）**：OIDC-7（P1，评审记录 L569）"共用指南无三库接入章节"——已实读 `docs/Federation/联邦互联-使用指南.md` L366-398 "2.1 OIDC 系平台网关库接入"专章**已存在**（A 档文档修复批次 bbd3d7d 已补齐，含 `AddOidcFederationChannel`/`AddGoogleFederationChannels`/`AddMicrosoftFederationChannels` 接入骨架）——本文案 §7 文档同步不再重复该专章，仅对齐回调语义表述。

### 1.2 现状链路走通（已实读源码确认）

**OAuth 回调主链路（子应用消费方委托形态——authorize/start 发起）**：

```
POST {prefix}/authorize/start                         FederationWebExtension.HandleAuthorizeStart（L306-314）
  └─ ISsoSubAppBridge.StartAsync                       SsoSubAppBridge.cs L65-112（state 票据 + 通道构授权 URL）
       └─ channel.BuildAuthorizeUrlAsync               L96-101（RedirectUri = request.CallbackBaseUrl——仅基址）
平台授权页（QQ graph.qq.com / 支付宝 openauth / OIDC authorize）→ 302 回跳
GET {prefix}/{platformId}/oauth[/{channelId}]/callback FederationWebExtension.HandleOAuthCallback（L320-335，GET——IdP 302 语义）
  ├─ L323    ResolveChannelAsync 选区（平台段 Ordinal 校验 + 双键解析）
  ├─ L327-329 query 原样包参数字典             ← 断链点 1：无 redirect_uri 注入（对比 HandleEvent L245 已有 base_url 注入先例）
  └─ L331    ISsoSubAppBridge.CompleteOAuthAsync(cfg.ChannelId, parameters)
       └─ SsoSubAppBridge.CompleteOAuthAsync            SsoSubAppBridge.cs L115-159
            ├─ L120   state 票据验签（解 app_id/redirect/sub_state）
            ├─ L126   ISsoLogin.LoginAsync(l, new SsoChannelAuthContext(parameters))  ← 断链点 2：原样包装透传
            └─ L143   签密封票据 → 302 跳回子应用（code + sub_state）
└─ ISsoLogin.LoginAsync → channel.AuthenticateAsync     SsoLogin.cs L38-57（LoginAsync——帧内构造通道 → 委托认证）
     └─ 平台通道按 Parameters 读 code / redirect_uri：
          QQ     QqOauthChannel L58-61 code 缺拒 / L67-70 redirect_uri 缺拒 → QQ_REDIRECT_URI_REQUIRED
          Alipay AlipayOauthChannel L64-68 code 缺拒 / L74-77 redirect_uri 缺拒 → ALIPAY_CODE_REQUIRED
          Oidc   OidcChannelBase L79-81 code 缺拒 / L86-87 redirect_uri·code_verifier 软读
```

**另一入口（借道桥接形态）**：外部 IdP 登录——`ExternalIdpAuthenticator.AuthenticateAsync`（Federation 主包，`IExternalIdpAuthenticator` 契约，AuthCenter `IExternalIdpLoginService` 借道消费）→ `_channel.AuthenticateAsync(new SsoChannelAuthContext(parameters))`（L70-88）——**parameters 原样进上下文**，同样无注入归一（消费方 Web 层 `/login/external/{channelType}` 端点传参决定成败，本方案不做该端点改造，仅登记边界）。

### 1.3 断链点清单（本方案修复范围）

| # | 断链点 | 位置 | 断链语义 | 后果 |
|---|--------|------|---------|------|
| B1 | **redirect_uri 无注入**（主断链） | `FederationWebExtension.HandleOAuthCallback` L327-329 | 回调仅带 code+state（QQ 实证）；QQ/Alipay 通道硬读 `Parameters["redirect_uri"]` | `QQ_REDIRECT_URI_REQUIRED` / `ALIPAY_REDIRECT_URI_REQUIRED` 必败 |
| B2 | **auth_code 未归一**（主断链） | `AlipayOauthChannel` L64-68 + 注释 L63-64 声明的装配行为零实现 | 支付宝回调携带 `auth_code` 键，通道只读 `code` 键（注释声称"装配层把 auth_code 注入 code 键"——全仓 grep 零实现 => 注释失实） | `ALIPAY_CODE_REQUIRED` 必败 |
| B3 | **OIDC 同源软断链** | `OidcChannelBase` L86 | redirect_uri 缺失时传 `""` 出站换 token——标准 OIDC IdP（Google/Microsoft）与授权时 redirect_uri 不一致 → 换取被拒（RFC 6749 §4.1.3） | 换取失败（消息堆栈错误，非通道硬拒）——与 B1 同根因 |
| B4 | 委托发起侧 redirect_uri 粒度不足（关联缺陷，**本次同步收口**） | `SsoSubAppBridge.StartAsync` L96-101 + `ISsoSubAppBridge.cs` L48-49 注释 | 发起时 `BuildAuthorizeUrlAsync(RedirectUri: request.CallbackBaseUrl)`——**仅 scheme://host 基址**，IdP 授权后回跳根路径而非回调端点（契约注释预期 `{CallbackBaseUrl}/{RoutePrefix}/oauth/{channelId}/callback`；QQ/AL/OIDC 回跳后根本到不了 oauth/callback） | 子应用委托形态回调链路整体不可达 |
| B5 | 借道桥接同源缺口（**仅登记，本次不改造**） | `ExternalIdpAuthenticator` L70-88 | 桥接路径 parameters 原样进上下文——若消费方以 auth_code 键/无 redirect_uri 调借道，同样必败；但该端点参数由消费方 Web 端点组装（`AuthCenter /login/external/{channelType}`），不在本方案装配层范围 | 待 AuthCenter 侧联调时按需同步（登记 §9） |

> **微信对照（不受影响）**：`WeChatOauthChannel` 认证只读 `code`（L57-60），不要求 redirect_uri——回调链路当前可用（评审"对照通"）。本方案注入对微信为**无害增量**（多一个键，通道不读）。

---

## 2. 目标

1. **B1 修复**：装配层 `HandleOAuthCallback` 注入 redirect_uri（当前请求 URL，服务端可信填充）——QQ/Alipay/OIDC 回调认证必败根除；
2. **B2 修复**：auth_code 归一——装配层归一 `auth_code → code`（主要路径）+ Alipay 通道兼容 `code ?? auth_code` 双键（防御）；
3. **B3 修复**：OIDC 系同根因——装配层统一注入后 OidcChannelBase 出站换取携带真实 redirect_uri（软读语义不变，行为修正）；
4. **B4 收口**：委托发起侧 begin链路闭环（redirect_uri 落点 = 回调端点全 URL），子应用委托形态（authorize/start → oauth/callback）真正可走通；
5. 与原语文档对齐：QQ/AL 通道注释中"装配层注入"的既有声明**成为事实**（README §四 N3 P1-4 / N4 P1-2 契约兑现）；OIDC 同源对齐；
6. **安全边界保持**：注入值服务端权威（Query 传入值被覆盖——防伪造回调域，对齐 `HandleEvent` base_url L243-245 / `HandleAuthorizeStart` L309 先例）；state 票据链路零变化；不加新端点、不改路由、零破坏性（既有消费方零迁移）。

**非目标**：
- 通道自持回调（选项 B，§3.2 否决）；
- B5 借道桥接端点改造（AuthCenter `IExternalIdpLoginService` 消费面——本方案只保证桥接内部注入语义，端点参数组装不动）；
- OIDC-7 指南专章（已存在，见 §1.1）；OIDC-2/3/8（另一档）；AL-2 timestamp / AL-4 验签剔除集 / QQ-5/6/7 / AL-5/6/7（B 档另立）；
- 平台授权 URL 的 scope/PKCE 扩展语义（不在回调断链范围）。

---

## 3. 选项对比与选定

### 3.1 选项 A：装配层统一回调路由（按 state/channelId 恢复上下文）

- **回调落点唯一**：`GET {prefix}/{platformId}/oauth[/{channelId}]/callback` 保持为 IdP 302 唯一入口（GET 语义 + 平台段校验 + 通道双键选区已就绪，FederationWebExtension L107-111/L515-551）；
- **恢复上下文**：通道上下文经路由段（channelId/alias）+ `state` 票据（SsoSubAppBridge L120-123 验签解 app_id/redirect）两重恢复——state 承载子应用上下文、路由段承载通道上下文，**各自语义已经分工**，装配层只补"协议参数注入"；
- **注入装配**：handler 内服务端可信注入 redirect_uri（当前请求 URL）+ auth_code 归一（§4）；
- 改动集中在主包 Web 装配层 + 门户（桥接可复用），平台库仅防御性双键。

### 3.2 选项 B：通道自持回调（各通道自己收回调）

- 每个平台库自带回调端点（`AddXxxFederationChannels()` 内 Map 回调路由）——授权 URL 回跳目标直接指向库端点，redirect_uri 天然确定；
- **否决理由**（对齐评审建议方向 + 架构纪律）：
  1. **纯库判据破坏**：平台库"不引 AspNetCore / 零装配"（N1 模板判据 + QQ README L125——入站端点归 Federation/消费方装配层），通道自持回调 = 平台库引入端点装配，反转既有架构边界；
  2. **多通道形态爆炸**：N 平台 × M 通道 × 回调路由 = 装配面碎片化，通道注册/启停/路由校验逻辑重复实现（主包 `ResolveChannelAsync` 平台段校验 + B 守卫将不可复用）；
  3. **协议不一致问题依旧**：通道收回调只是自知道 redirect_uri，auth_code 归一（平台协议差异）仍需通道自持——恰恰违背"协议差异收敛于通道实现"与"装配层注入"的既有分工（QQ README L84/N3 P1-4 已定"装配层以 redirect_uri 构造 authorize URL + 回调注入 context"）；
  4. **消费方合规面放大**：回调端点须匿名游客帧 + 白名单启用 + 端点 Options——N 库各自声明 Web 装配 = 消费方装配复杂度 O(N) 而非 O(1)。

### 3.3 选定：**选项 A——装配层统一回调路由**（一句话：回调落点唯一、装配层服务端可信注入 redirect_uri + auth_code 归一、通道防御性兼容双键——对齐既有"authorize 构造归装配层、通道不感知 state"分工，零破坏增量修复）

与评审建议（QQ-1 建议①"装配层注入当前请求 URL 作 redirect_uri"、AL-1 建议①②③ 三管齐下、后记 L610 合并立项）完全一致。

---

## 4. 设计（v0.1——三平台合并一次立项）

### 4.1 redirect_uri 注入（B1/B3——Federation 主包装配层）

**`FederationWebExtension.HandleOAuthCallback`（L320-335）改造**——param 组装段（L327-329）改为：

```csharp
var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
foreach (var (key, value) in ctx.Request.Query)
    parameters[key] = value.ToString();
// 服务端可信注入（对齐 HandleEvent L243-245 base_url 先例 + HandleAuthorizeStart L309 先例）：
// ① redirect_uri = 当前请求完整 URL（Scheme://Host + PathBase + Path，不含 query）——IdP 授权时
//    收到的回跳目标即本端点（响应式注入，天然与授权时一致，RFC 6749 §4.1.3）；
//    最后写覆盖 query——攻击者 query 注入 ?redirect_uri= 不得覆盖服务端权威值。
parameters["redirect_uri"] = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}{ctx.Request.Path}";
// ② auth_code 归一：支付宝回调授权码键名 → 通道统一 "code" 键（通道契约语义，见 4.2）
if (parameters.TryGetValue("auth_code", out var authCode)
    && !string.IsNullOrWhiteSpace(authCode)
    && !parameters.ContainsKey("code"))
    parameters["code"] = authCode;
```

- **注入时序**：在 L331 `CompleteOAuthAsync` 调用之前（`parameters` 组装段内）——门面接收到的字典已含两键，SsoSubAppBridge/SsoLogin/通道**零改动透传**（B1/B2 断链点在单点内消解）；
- **响应式而非持久式**：不落 state、不查注册表——当前请求 URL 即"授权时 IdP 回跳目标"（回调端点 URL 唯一确定性），无状态恢复成本、无 TTL/清洗问题；
- **安全**：覆盖式写入防伪造（对齐 base_url 先例——攻击者无法把 redirect_uri 导向任意域）；redirect_uri 不参与跳回（跳回目标 = state 票据内的 redirect，SsoSubAppBridge L143——防开放重定向链路不变）。

### 4.2 auth_code 归一（B2——装配层归一 + 通道双键防御）

**双层防线（AL-1 建议 ②"三管齐下"落地）**：

1. **主要路径——装配层归一**（§4.1 代码 ②）：支付宝回调 `auth_code` → `parameters["code"]`——QQ/OIDC 天然 code 键零影响；**通道硬拒语义不变**（通道仍只读 `code`）；
2. **防御路径——Alipay 通道兼容双键**（`AlipayOauthChannel.AuthenticateAsync` L64-68 改）：

```csharp
var hasCode = context.Parameters != null
    && (context.Parameters.TryGetValue("code", out var code) || context.Parameters.TryGetValue("auth_code", out code))
    && !string.IsNullOrWhiteSpace(code);
if (!hasCode)
    return new SsoChannelAuthResult(false, null, "ALIPAY_CODE_REQUIRED", 0);
```

   - 修正 L63-64 注释"装配层把 auth_code 注入该键"失实声明——改为"装配层归一 + 本通道防御性双键兼容"；
   - 双键命中语义（防御路径下 `auth_code` 与 `code` 同时出现时 `code` 优先——对齐 TryGetValue 短路顺序）。

### 4.3 与 `ISsoChannel.AuthenticateAsync` 契约对齐

- 契约（`Federation.Abstractions/ISsoChannel.cs` L9 `SsoChannelAuthContext(IReadOnlyDictionary<string,string?> Parameters)`——可扩展键值模型）**零变化**；
- 通道读取契约语义规范化（本方案实施后正式成立）：
  - `Parameters["code"]` —— **统一授权码键**（QQ 回调 code / OIDC 回调 code / Alipay 回调 auth_code 归一后）；
  - `Parameters["redirect_uri"]` —— **授权时回跳目标**（装配层响应式注入；OIDC 系另读 `code_verifier`——PKCE 语义保持，L87 不动）；
  - 通道不得假定"回调 query 自带两键"——装配层注入是契约成立前提；QQ/AL 硬拒码（`*_CODE_REQUIRED`/`*_REDIRECT_URI_REQUIRED`）保留为**防御语义**（绕过装配层自建端点接入时仍然生效——QQ-1 建议② 的"文档明示自建适配端点"路径兼容）。
- **OIDC 修正（B3）**：`OidcChannelBase` L86 软读语义保持（`TryGetValue`），装配层注入后出站携带真实 redirect_uri——`ExchangeCodeAsync` 换取（L93-94）与授权时一致，标准 IdP 比对通过；无注入时仍传 `""`（行为不变，防御语义同 QQ/AL）。

### 4.4 兼容双键（channelId + alias）

- **回调路由选区**：`ResolveChannelAsync`（FederationWebExtension L515-551）已支持 alias 全局解析 → ChannelId（L538-539 `GetByAliasOrIdAsync`）+ 平台段 Ordinal 校验（L547-548）——**本方案零改动**；
- **注入与选区正交**：redirect_uri 注入发生在选区成功之后（L324 之后），注入值为当前请求 URL——**与通道段形态无关**（带/不带 channelId 段、alias/内部键形态的回调 URL 各自响应式注入，均与授权时一致）；
- §4.4 标题语义收口：双键兼容 = ① 回调路由 alias/内部键双键 + ② Alipay code/auth_code 双键，本方案实现后者、验证前者（测试锁定）。

### 4.5 B4 收口：委托发起侧 redirect_uri 落点（Federation 主包装配层）

**`HandleAuthorizeStart`（L306-314）改造**——服务端可信回调 URL 与回调端点对齐（当前仅基址经 `SsoSubAppBridge.StartAsync` L96-101 传给通道构授权 URL）：

> **挂载前缀口径（P0-1 闭环）**：`FederationEndpointOptions.RoutePrefix` 默认 `/feberation` 是**"链接组装前缀（非挂载）"**；端点实际挂载路径 = **框架 Module 前缀模型**：basePath（`TKWF:Api:RestBasePath`，测试宿主实证 `/api`）+ `[assembly: ExtensionModule("Federation","feberation")]` 段 = `/api/feberation/...`（实证：`FederationEndpointHost` L97 `ExcludedPathPrefixes.Add("/api/feberation")` + `FederationWebExtensionTests.cs` L29-31 `DefaultPrefix = "/api/feberation"`）。故落点构造**不得**用 Options.RoutePrefix（否则授权 URL 的 redirect_uri 指向 404 路径，B4 目标落空且 IdP 授权时校验 redirect_uri 即拒）。

```csharp
// ① 挂载前缀推导——handler 仅在被挂载的 authorize/start 端点上被调用 → 当前请求路径恒为 {mount}/authorize/start：
//    Ordinal 去除尾部 /authorize/start 段即真实挂载前缀（含 PathBase/basePath），与 §4.1 回调侧响应式注入
//    （当前请求 URL）天然逐字节一致。
var mountPrefix = (ctx.Request.PathBase + ctx.Request.Path).Value![..^"/authorize/start".Length]; // Ordinal 尾段去除

// ② platformId 解析——channelId 已知经 registry 解析通道 PlatformType；null（默认通道降级）时也须解析
//    默认通道 platformId（无通道段形态 /{platformId}/oauth/callback 仍需 platformId 段）；语义对齐
//    回调侧 HandleOAuthCallback L323 ResolveChannelAsync（GetByAliasOrIdAsync / 默认通道选区）。
var channel = req.ChannelId != null
    ? await channelRegistry.GetByAliasOrIdAsync(req.ChannelId, ct)   // 双键：alias/内部键
    : await channelRegistry.GetDefaultChannelAsync(ct);              // 默认通道（单活跃降级）——方法名示意，实现对齐 L323 选区
var platformId = channel?.PlatformType;                              // 平台类型段（如 qq / wechat / alipay / oidc）
// channel 为 null（解析失败）→ 拒绝授权（fail-closed，对齐回调侧平台段 404 语义）

// ③ 回调端点全 URL 落点——完整落点（非仅前缀，P2 伪代码正文对齐）
var callbackPath = $"{mountPrefix}/{platformId}/oauth" + (req.ChannelId != null ? $"/{req.ChannelId}/callback" : "/callback");
var request = req with
{
    // 服务端可信回跳目标 = 回调端点全 URL（契约注释 ISsoSubAppBridge.cs L48-49 预期形态）：
    // {mount}/{platformId}/oauth[/{channelId}]/callback——发起侧 ChannelId 已知则带通道段（精确选区）；
    // null（默认通道）则无通道段形态（回调侧单活跃降级，L331 同语义）。
    CallbackBaseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{callbackPath}",
};
```

- **落点构造**：`{Scheme}://{Host}{mountPrefix}/{platformId}/oauth[/{channelId}]/callback`——mountPrefix **从当前请求路径推导**（含 PathBase/basePath，非 Options.RoutePrefix）；发起时 ChannelId 已知则带通道段（精确选区）；null（默认通道）则无通道段形态（回调侧单活跃降级，L331 同语义）；
- **RoutePrefix 配置边界（P0-1 解析优先级）**：请求路径推导为**唯一权威来源**——handler 仅在被挂载端点上被调用 → 请求路径必然含挂载段 → 推导恒可用、**无需 RoutePrefix 兜底**；`FederationEndpointOptions.RoutePrefix`（默认 `/feberation`）**不再参与落点构造**，语义收口为"链接组装前缀（非挂载）"（README/指南口径）；消费方既有多回调域名部署按 §9 风险 1 走自建适配端点或统一回调域名；
- **platformId 解析（P1-2 闭环）**：handler 内经 channel registry（`GetByAliasOrIdAsync` / 默认通道解析——对齐 `HandleOAuthCallback` L323 `ResolveChannelAsync` 语义）取 `PlatformType` 后参与落点构造；**req.ChannelId 为 null（默认通道降级）时同样解析默认通道 platformId**（无通道段形态 `/{platformId}/oauth/callback` 仍需 platformId 段）；解析失败拒绝授权（fail-closed）；
- **服务端权威**：请求体 `CallbackBaseUrl` 值仍被覆盖（防子应用伪造回调域——L309 既有语义保持，仅覆盖值从基址变为回调端点全 URL）；
- **回调时一致性**：IdP 回跳到 `{mount}/{platformId}/oauth[/{channelId}]/callback` → `HandleOAuthCallback` 注入当前请求 URL → 与授权时 IdP 收到的 redirect_uri **逐字节一致**（方案闭环；TypeScript 侧消费方零迁移——请求体字段不再被使用，删除字段非破坏）；
- **与开放重定向无关**：该 URL 仅供 IdP 回跳与换取校验，不含子应用 redirect（子应用回跳仍经 state 票据，L143）。

---

## 5. 装配层集成测试计划（模拟回调全链路 ≥3 用例：QQ / Alipay / Oidc 各一）

**测试宿主策略**：复用 `Extension.Federation.Tests` 现有 Web 端点宿主（`FederationEndpointHost`——TestServer + `FakeSsoSubAppBridge` L331-370）——本轮升级 Fake 记录完整参数字典（`LastCallbackParams` 已含，补 `redirect_uri` 断言）→ **新增回调参数注入契约用例**；平台通道侧在各自测试项目（QQ L48-126 / Alipay L48-77 / Oidc 对应正负用例）**新增双键/注入语义用例**。断言注入由 Web 宿主（真实 handler 路径）发起——**不手写 StubDomainUser，走生产路径**（tkwf-test 纪律）。

> **挂载前缀口径（P1-1 闭环）**：测试请求 URL 与断言值一律用 **`/api/feberation`** 前缀——实际挂载路径 = basePath（`TKWF:Api:RestBasePath`，测试宿主实证 `/api`）+ `[assembly: ExtensionModule("Federation","feberation")]` 段（**框架 Module 前缀模型**；实证 `FederationWebExtensionTests.cs` L29-31 `DefaultPrefix = "/api/feberation"` 常量 + `FederationEndpointHost` L97 `ExcludedPathPrefixes.Add("/api/feberation")`）；`/feberation`（`Options.RoutePrefix` 默认）仅为"链接组装前缀（非挂载）"，不用于端点 URL 构造/断言。

| # | 测试 | 项目 | 用例语义 | 断言 |
|---|------|------|---------|------|
| 1 | **QQ 回调全链路（主用例）** | Federation.Tests + QQ.Tests | `GET /api/feberation/qq/oauth/{channelId}/callback?code=qq-code-1&state={票据}`（伪造 query `?redirect_uri=https://evil/` 一并携带）→ Fake 记录 → 断言字典含 `redirect_uri = http://localhost/api/feberation/qq/oauth/{channelId}/callback`（**服务端权威覆盖伪造值**）；QQ 通道侧 `AuthenticateAsync(参数含注入 redirect_uri)` → 出站链携带 → 成功 `SsoChannelAuthResult(openid)` | `LastCallbackParams["redirect_uri"]` 等于请求 URL；QQ 通道响应 openid（QQ.Tests 补注入参数正用例） |
| 2 | **Alipay 回调全链路（主用例）** | Federation.Tests + Alipay.Tests | `GET /api/feberation/alipay/oauth/callback?auth_code=alipay-ac-1&state={票据}`（无通道段单通道降级形态）→ 断言字典 `code == "alipay-ac-1"`（归一）+ redirect_uri 注入；Alipay 通道侧双键防御用例：直接构造 `Parameters["auth_code"]`（无 code）→ 成功（不依赖装配层） | `code` 键归一断言 + 通道 `code ?? auth_code` 命中；无归一直调通道亦成功（防御路径） |
| 3 | **OIDC 回调同源（主用例）** | Federation.Tests（Oidc 通道用例在 Oidc.Tests） | `GET /api/feberation/oidc/oauth/{channelId}/callback?code=oidc-code-1&state={票据}` → 断言字典含注入 redirect_uri（OidcChannelBase 软读语义 + 出站换取携带断言在 Oidc.Tests——stub 捕获换取请求 redirect_uri 参数） | 注入键断言 + Oidc 出站换取携带同一 URL |
| 4 | **B4 委托链路闭环（收口用例）** | Federation.Tests | `POST /api/feberation/authorize/start`（ChannelId=wechat-mp）→ Fake 断言 `CallbackBaseUrl = http://localhost/api/feberation/wechat/oauth/wechat-mp/callback`（原断言"等于 http://localhost"更新——既有 L470 断言适配）；回调端同 URL 注入 → start/callback 两段 redirect_uri 一致 | start 段与 callback 段 URL 逐字节相等 |
| 5 | 微信回归（对照通） | Federation.Tests（既有 OAuthCallback 用例 L497-559 适配） | 既有 4 用例参数断言不动（code/state 透传）+ 新增注入键无害断言（字典多一键、通道不读） | 既有用例全绿 + 注入键存在 |
| 6 | 错误路径（B 守卫/平台段不变） | Federation.Tests | 多通道无通道段 → 400 CHANNEL_REQUIRED（L541-559 既有）/ 平台段错配 → 404（既有）——注入在后于选区，行为不变 | 既有断言全绿 |

> 测试数预估：Federation.Tests 现有基线外 +4（注入 3 用例 + B4 闭环 1）+ 适配既有 2 处断言；QQ.Tests 18 基线 +1；Alipay.Tests 14 基线 +1；Oidc.Tests（Federation.Oidc 测试项目）按现有结构 +1（换取携带断言）。

---

## 6. 受影响面与测试矩阵

### 6.1 受影响面（依赖拓扑反向图）

| 面 | 变更 | 影响 |
|----|------|------|
| **Federation 主包** | `FederationWebExtension.HandleOAuthCallback`（L327-329 注入段）+ `HandleAuthorizeStart`（L309 落点构造）——**本方案全部代码变更所在地** | `/api/feberation/*`（实际挂载——basePath `/api` + Module 段 `feberation`；`Options.RoutePrefix` `/feberation` 仅为链接组装前缀非挂载）回调/委托端点行为；对外契约零变化（`ISsoSubAppBridge`/`SsoLogin`/`ISsoChannel` 签名零改动） |
| **Federation.QQ** | 通道零代码变更（注入后硬读语义天然通过）；仅 README 同步"装配层注入已实现"事实 | 回调链路由必败转可用——QQ.Tests 补注入参数正用例 |
| **Federation.Alipay** | `AlipayOauthChannel.AuthenticateAsync` L64-68 双键兼容 + 注释修正（L63-64） | 回调链路由必败转可用；防御双键（零装配直调亦可用） |
| **Federation.Oidc**（+ Google/Microsoft 派生） | 通道零代码变更（L86-88 软读 + 注入修正行为）；派生库（Google/Microsoft）经基类继承自动受益 | 换取携带真实 redirect_uri——标准 IdP 比对通过；**派生库零改动**（Oidc.Tests 补换取断言） |
| **AuthCenter 借道桥接（B5 边界）** | `ExternalIdpAuthenticator`（L70-88）**零代码变更**——桥接内部仅透传；消费方端点参数组装不动 | 桥接语义不变；若消费方以 QQ/AL 通道借道需自备注入参数（登记 §9 已知边界） |
| 消费方 | 零迁移（无契约/路由/配置变化；请求体 `CallbackBaseUrl` 字段不再被使用但删除字段为破坏性——保留字段兼容） | 回调链路修复后既有部署行为变化（由失败转成功）——正向 |

### 6.2 测试矩阵（受影响面定向测试纪律）

| 层 | 范围 | 执行 |
|----|------|------|
| L0 编译门 | slnx 构建 0 错误 | 必做（每次收尾） |
| L1 受影响面 | Federation.Tests（含升级用例）+ QQ.Tests + Alipay.Tests + Oidc.Tests（含派生 Google/Microsoft 若触及基类） | 必做（本方案收尾） |
| L1+ 关联 | AuthCenter.Tests（借道桥接冒烟——`ExternalIdpAuthenticator` 路径回归确认零影响） | 建议（B5 边界确认） |
| L2 全量回归 | 45 项目（~2569 用例） | 发布/tag 前执行（既有纪律） |

---

## 7. 文档同步

| 文档 | 变更 |
|------|------|
| `docs/Federation/联邦互联-使用指南.md` | ① L178 支付宝"回调查验"段——"装配层把 auth_code 映射为 code 键 + redirect_uri 注入"**从声明改为事实描述**（附注入机制一句话）；② 回调链路图/§3.5 路由定形段——补"回调时装配层响应式注入 redirect_uri + auth_code 归一"说明；③ OIDC 系接入专章（L366-398）补回调注入语义一句话（OIDC-7 专章已存在，仅对齐） |
| `docs/Federation.QQ/QQ互联连接器-使用指南.md` | 回调链路说明同步（注入即契约前提；自建适配端点路径保留为防御语义说明） |
| `docs/Federation/N4-支付宝平台网关-开发方案.md` 补裁定记录 / Alipay 相关 | auth_code 双键 + 装配层归一裁定注记（方案级文档留痕） |
| `_Framework/Federation/README.md` | 端点表回调行补注入语义；版本记录 V0.19.0 行（发布后） |
| `_Framework/Federation.QQ/README.md` / `Federation.Alipay/README.md` / `Federation.Oidc/README.md` | L84（QQ N3 P1-4）/ L63-64 注释对应 README 段（AL N4 P1-2）改"已实现"；Oidc README 回调段对齐（前述版本断链 OIDC-1/GG-1/MS-1 为 A 档已闭环，不重复） |

---

## 8. 版本

| 包 | 版本 bumps | 说明 |
|----|-----------|------|
| **Federation（主包）** | **V0.19.0**（minor——装配层行为修复 + 委托链路落点变化） | 本方案代码全部所在地；README/指南同步 |
| **Federation.QQ** | 同版 bump（V0.5.0） | 零代码变更（版本随测试/文档同步） |
| **Federation.Alipay** | 同版 bump（V0.4.0） | 双键防御代码变更 |
| **Federation.Oidc** | 同版 bump（V0.4.0） | 零代码变更（派生 Google/Microsoft 零改动——不单独 bump，继承基类行为） |
| Federation.Abstractions / TrustCenter / AuthCenter | **不 bump** | 契约零变化（`ISsoChannel`/`ISsoSubAppBridge`/`IExternalIdpAuthenticator` 签名不变） |

> **发布顺序（P2 闭环固化，保持）**：**Federation V0.19.0 先行**（主包装配层行为修复 + 委托链路落点变化先落地）→ **QQ V0.5.0 / Alipay V0.4.0 / Oidc V0.4.0**（平台库版本随测试/文档/防御代码同步）；派生 **Google/Microsoft 不 bump**（继承基类行为，零改动）。

> 版本纪律（AGENTS §5）：tag 须征求用户同意 + 方案评审/审核报告闭环；tag 后本地全量部署（`build-ext-deployment.ps1`），在线发布随发布动作攒批。

---

## 9. 风险与回滚

| # | 风险 | 等级 | 缓解 |
|---|------|:---:|------|
| 1 | **注入与授权时值不一致**（IdP 回跳 URL 与当前请求 URL 不同——反代改写/多域名回调配置） | 中 | 响应式注入以当前请求 URL 为准，服务端权威覆盖；平台侧若配置多回调域名——按 URL 差异配置自建适配端点（QQ-1 建议② 路径）或统一回调域名；测试用例以 TestServer URL 断言锁定一致性 |
| 2 | 回调路径含敏感 query（code 泄漏进注入 URL） | 低 | 注入 URL 不含 query（Scheme://Host + PathBase + Path 显式构造）；测试断言确认无 `?` |
| 3 | `Request.PathBase` 反代挂载差异致 URL 不一致 | 低 | 使用框架标准 PathBase 语义（`ctx.Request.PathBase`——与消费方 UsePathBase 一致性）；消费方反代场景文档注记（§7 指南） |
| 4 | B4 落点变化致既有部署回调 URL 漂移（若消费方以旧基址形态自行配置 IdP 回调域名） | 中 | 落点变化仅影响**子应用委托形态发起侧**（authorize/start 新构造的授权 URL）；既有已配好回跳的部署（IdP 控制台回调地址 = 完整回调端点 URL 形态）在 B4 修正前后**一致**（修正前 IdP 收基址、修正后收完整端点 URL——IdP 控制台需按回调端点 URL 配置，方案文档 + 指南明示） |
| 5 | OIDC 换取携带真实 redirect_uri 后个别 IdP 配置不符（原软读 "" 恰好通过的边缘） | 低 | 出站携带真实值 = RFC 标准行为；IdP 控制台回调地址配置一致即通过（指南注记）；变更为正向（原必败/边缘行为修正） |
| 6 | 测试宿主 Fake 断言升级破坏既有用例 | 低 | 既有断言适配清单化（§5 用例 4/5——`CallbackBaseUrl` L470 断言更新）；适配独立提交可回滚 |

**回滚**：本方案零契约/零路由/零配置变化——回滚 = 还原 `HandleOAuthCallback`/`HandleAuthorizeStart` 两 handler 注入段 + Alipay 双键（单文件级 revert，Federation v0.19.0 tag 前本地验证充分）。

---

## 变更记录

| 版本 | 日期 | 变更 |
|------|------|------|
| v0.1 | 2026-11-09 | 首版——C 档立项方案（评审 QQ-1/AL-1 合并 + Oidc 同源）；断链点 B1-B5 清单 + 选项 A 选定 + 装配层注入设计 + ≥3 全链路集成测试计划 + 受影响面矩阵 |
| v0.1.1 | 2026-11-09 | **Oracle（pro3）评审 PASS WITH CONDITIONS 条件闭环修订**（1 P0 / 2 P1 / 若干 P2，逐条闭环进既有章节）：**P0-1** §4.5 落点构造改为从当前请求路径推导挂载前缀（删除 Options.RoutePrefix 解析来源，附挂载前缀口径实证 + RoutePrefix 边界/解析优先级）；**P1-1** §5 测试 URL/断言值前缀统一 `/api/feberation`（附框架 Module 前缀模型注记）；**P1-2** §4.5 补 platformId 解析步骤（channel registry 取 PlatformType，含默认通道降级；伪代码补全去省略号）；**P2** 行号微偏 4 处修订（SsoLogin L38-57 / ExternalIdpAuthenticator L70-88 / Oidc ExchangeCodeAsync L93-94 / WeChat L57-60）+ 伪代码/正文对齐（CallbackBaseUrl 含完整落点）+ 发布顺序 note 固化（§8）；§6.1 受影响面路径随 P0-1 口径同步；核心架构（装配层统一回调路由）与既有章节结构零改变 |