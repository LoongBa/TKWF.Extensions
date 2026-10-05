# M2 Google + Microsoft 平台网关库 开发方案（TKWF.Federation.Google / TKWF.Federation.Microsoft）

> **立项来源**: 平台网关立项依据（近中远）M2（P1 中期）｜ **类型**: L1 平台库（国外 OIDC——基于 M1 基座）
> **版本**: v0.1.0 ｜ **日期**: 2026-10-06 ｜ **状态**: 📋 提议（待 Oracle 评审）
> **关联**: 国外总规划（§三第一批 Google/Microsoft + §五 OidcChannelBase + §六实施顺序）｜ M1 基座（前置依赖）｜ N2 锚点策略（pairwise channel_id 复合编码）｜ 国外规划 §八复核（issuer 容错/sub 不透明）
> **关键约束**: 平台网关 = **纯库**（无 Initializer 无 `[TKWFExtension]` 无持久化零 Store）；**继承 M1 OidcChannelBase**——平台差异仅配置 + sub 语义策略（代码复用 >80%）；不引 AspNetCore

---

## 一、背景与目标

### 现状

国外 OIDC 第一批 = **Google / Microsoft / Apple**（三大件，覆盖消费 + 企业 + 移动——国外规划 §三）。Apple 是 OIDC **变体**（form_post/user 一次性/无 userinfo——P14）须独立适配器（归 **L3 远期**）；**Google / Microsoft 是标准 OIDC**（Discovery + JWKS + /userinfo 齐全）——走 M1 通用 OIDC 基座，本方案实现两平台。

### 目标

1. **`TKWF.Federation.Google`**——public sub，OIDC 参考实现基线（国外规划 §六实施顺序 1：Google 先行）；
2. **`TKWF.Federation.Microsoft`**——pairwise sub（跨 App 不同，PPID），tenant 配置化（common/consumers/organizations/租户 ID）；
3. 均基于 M1 基座——各自仅 `OidcPlatformConfig` 配置差 + sub 语义策略（external_uid 归一 `(channel_id, sub)`）；
4. 验收：pairwise 语义断言 + 多 IdP 枚举（F2/F3/F6——国外总规划验收标准）。

### 不包含

| # | 内容 | 去向 |
|---|------|------|
| 1 | Apple（OIDC 变体——form_post/user 一次性/无 userinfo/sub 不透明） | L3 远期独立适配器（不继承基座——非标准 OIDC，P14） |
| 2 | LinkedIn / Slack（同为 OIDC 基座可接） | L5/L6 远期——M2 只做第一批两大件（Google/Microsoft），广度验证归 LinkedIn |
| 3 | 自托管 IdP（Keycloak/Okta/Auth0） | 经 M1 基座直配（`AddOidcFederationChannel`——国外规划 §三 P2-3），非本方案范围 |

---

## 二、协议事实（官方文档核实——国外规划 §四矩阵）

| 项 | Google | Microsoft Entra ID |
|----|--------|-------------------|
| 协议 | **标准 OIDC** | **标准 OIDC**（v2.0） |
| Authorize | `https://accounts.google.com/o/oauth2/v2/auth` | `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` |
| Token | `https://oauth2.googleapis.com/token` | `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` |
| UserInfo | `https://openidconnect.googleapis.com/v1/userinfo` | `https://graph.microsoft.com/oidc/userinfo` |
| JWKS | `https://www.googleapis.com/oauth2/v3/certs` | `https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys` |
| Discovery | `https://accounts.google.com/.well-known/openid-configuration` | `https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration` |
| sub 语义 | **public**（跨 App 相同） | **pairwise**（同一用户跨 App 不同——PPID） |
| tenant | 无（单一消费 IdP） | `{tenant}` 决定受众：`common`（任意 AAD+MSA）/`consumers`（仅 MSA）/`organizations`（仅 AAD 工作学校）/租户 ID（精确） |
| 用户信息 | 标准 userinfo | 标准 userinfo（graph 端点）；email 可缺失（未验证域） |
| scope | `openid`/`email`/`profile` | `openid`+`profile`+`email`（PKCE 强推荐） |
| PKCE | 支持（OAuth 2.1 对齐） | 支持且**强推荐**（public client 场景强制） |
| 验签 | JWKS RS256 | JWKS RS256（发现响应含 keys） |
| 唯一注意点 | 无特殊 | **pairwise sub 跨 App 不同——channel_id 复合编码（N2 §3.3）**；tenant 配置化；**common/consumers/organizations tenant 授权后 `iss` 含实际租户 GUID（非字面 tenant 值）——TokenIssuer 白名单需支持通配/正则（M2-P1-1）** |

**设计要点（从事实导出）**：
- **两平台均标准 OIDC**——M1 基座全覆盖，平台库仅 `Defaults()` 配置差；
- **Microsoft pairwise** → channel_id = `microsoft_oidc:{client_id}`（N2 §3.3 定案——同用户不同 App 隔离）；Google public → channel_id = `google_oidc:*`（通配 client_id）；
- **Microsoft tenant 配置化**——config 级 `Tenant` 字段（common/consumers/organizations/{id}），端点在 `Defaults()` 时按 Tenant 模板化；消费方以 ChannelId 区分多 tenant 实例；
- **无入站回调**（国外全部无事件推送——P13）→ 出站-only 精简形态（组件 1/2/5/6/7/8），信任根 = 出站 code 一次性 + client_secret + **id_token JWKS 验签**（M1 核心）。

---

## 三、组件设计（按 M1 基座 + 出站-only 形态）

```
_Framework/Federation.Google/
├── TKWF.Federation.Google.csproj              # ProjectReference ..\Federation.Oidc\TKWF.Federation.Oidc.csproj（继承基座——ISsoChannel 经基座传递引用，**不直接引 Federation——Oracle 评审 P2-2**）+ TKWF.Domain（条件）；不引 AspNetCore
├── GoogleOptions.cs                            # [Options("TKWF:Federation:Google")]——Channels 列表（GoogleChannelConfig）
├── GoogleChannelConfig.cs                      # ChannelId + ClientId + ClientSecret（凭证自持）+ Scopes[] + RedirectUris 白名单（redirect_uri 一致性校验）
├── GoogleOidcChannel.cs                        # OidcChannelBase 派生——ChannelType="google_oidc"；Defaults() 填 Google 端点；public sub（无需 pairwise 特殊处理）
├── GoogleUserInfo.cs                           # 响应裁剪 record（Sub/Email/Name/Picture/EmailVerified——对齐已落地语义）
├── GoogleFederationServiceCollectionExtensions.cs  # AddGoogleFederationChannels(configure)——AddOptions().Configure + AddHttpClient + TryAddEnumerableConstructible<ISsoChannel, GoogleOidcChannel>
└── (无 Initializer 无 [TKWFExtension])

_Framework/Federation.Microsoft/
├── TKWF.Federation.Microsoft.csproj            # 同上（继承 M1 基座）
├── MicrosoftOptions.cs                         # [Options("TKWF:Federation:Microsoft")]——Channels 列表（MicrosoftChannelConfig）
├── MicrosoftChannelConfig.cs                   # ChannelId + ClientId + ClientSecret + Tenant（common/consumers/organizations/{id}）+ Scopes[] + RedirectUris 白名单
├── MicrosoftOidcChannel.cs                     # OidcChannelBase 派生——ChannelType="microsoft_oidc"；Defaults() 按 Tenant 模板化端点；pairwise sub → channel_id 复合编码 `microsoft_oidc:{client_id}`（**ChannelId 计算属性自动拼接 `$"{ChannelType}:{_config.ClientId}"`——Oracle 评审 P1-2：构造责任在 channel 内部，不依赖消费方手动拼接**）
├── MicrosoftUserInfo.cs                        # 响应裁剪 record（Sub/Email/Name/PreferredUsername——email 可缺失）
├── MicrosoftFederationServiceCollectionExtensions.cs  # AddMicrosoftFederationChannels(configure)——同上
└── (无 Initializer 无 [TKWFExtension])
> **独立包取舍说明（Oracle 评审 P2-4）**：Google/Microsoft 各自独立包——消费方按需装配（不引 Google 就不装配 Google channel，集合零元素自然跳过）；合并包会强制消费方同时引两平台（即使只用一个）——独立包符合平台网关库按需装配原则（对齐 WeChat 先例一平台一包）。

```

### 3.1 GoogleOidcChannel 流程

```
装配层构造 authorize URL（M1 OidcAuthFlow.BuildAuthorizeUrl：client_id+redirect_uri+scope=openid email profile+state+PKCE）
→ 302 Google 授权页（纯跳转）→ 回调 ?code=&state=
→ 验 state（装配层）→ M1 ExchangeCodeAsync：code+client_secret+code_verifier → token 端点
→ JwksManager 取 Google JWKS（public RSA 公钥）→ OidcIdTokenValidator 验 id_token（RS256 + iss=accounts.google.com + aud=client_id）
→ (可选) userinfo 拉取 → SsoChannelAuthResult(Success, ExternalUserId=sub, AuthLevel=2)
→ (channel_id=google_oidc, sub) → uid（Federation 编排层，N2 锚点）
```

### 3.2 MicrosoftOidcChannel 流程（差异点 = pairwise + tenant）

```
装配层构造 authorize URL（M1 BuildAuthorizeUrl：{tenant} 模板化 + scope=openid profile email + state + PKCE S256——Microsoft 强推荐）
→ 回调 code → M1 ExchangeCodeAsync（tenant 对应 token 端点）→ id_token 验签（iss = https://login.microsoftonline.com/{tenant}/v2.0——**common/consumers/organizations 场景 iss 含实际租户 GUID，TokenIssuer 白名单须通配/正则匹配（Oracle 评审 P1-1）**）
→ SsoChannelAuthResult(Success, ExternalUserId=sub, AuthLevel=2)
→ (channel_id=microsoft_oidc:{client_id}, sub) → uid（pairwise——不同 client_id 不同 channel_id，天然隔离，N2 §3.3）
```

### 3.3 sub 语义与锚点

- **Google（public）**：sub 跨 App 相同——`google_oidc:*` channel_id（通配；**`*` 为字面字符串存映射表 `ChannelId` 列，精确匹配查询非 LIKE——Oracle 评审 P1-3**）；同一用户经不同 Google App 登录映射同 uid（映射表 `(channel_id, sub)` 相同）；**一个消费方只配一个 Google channel 实例**（多 Google App 场景需特殊处理——channel_id 加 client_id 区分但映射表按 sub 归一是 N2 §3.3 public 通配语义边界，本方案不实现多 App 场景）；
- **Microsoft（pairwise）**：sub 跨 App 不同——channel_id 复合编码 `microsoft_oidc:{client_id}`（**ChannelId 计算属性自动拼接——Oracle 评审 P1-2：消费方只配 ClientId，channel 内部构造**）；**不加映射表列、不动既有 `UX_PlatformAccountMap_Channel` 索引**（N2 §3.3 定案）；同用户经不同 Microsoft App 登录 → 不同 `(channel_id, sub)` → **不误并**（各自映射，绑定并合同一 uid 经用户验证——N2 §3.2 规则）；
- **锚点**：两平台均经 `ISsoChannelMapService.LinkAsync` 写 `PlatformAccountMap`；anchor 列存 TKWF 自生成平台无关锚点值（N2 §3.1 选项 B——`anc_` 前缀 + CSPRNG），经 `ISsoAccountLinkService.SetFederationAnchorAsync`。

### 3.4 凭证自持（对齐 WeChat 先例 QqChannelConfig 凭证形态）

- `GoogleChannelConfig`：`ClientId + ClientSecret`（Google 仅两凭证）；
- `MicrosoftChannelConfig`：`ClientId + ClientSecret + Tenant`（M2 特有 tenant 维度）；**企业场景支持 private_key_jwt（证书认证）——config 增加 `ClientCertificatePath`/`ClientCertificateThumbprint` 字段（M2 可先支持 client_secret，证书认证归后续迭代——Oracle 评审 P2-1）**；
- 生产：AES-GCM 密文或装配注入（K8s secret mount），**永不明文进配置库**（Oracle P2-4——凭证自持，不复用 `IPlatformCredentialService`）。

---

## 四、任务拆解

| 任务 | 描述 | 关联 | 工作量 |
|------|------|------|--------|
| T1 | **M1 基座前置验证**——确认 M1 已落地（`OidcChannelBase`/`OidcAuthFlow`/`JwksManager`/`OidcIdTokenValidator`/`AddOidcDerivedChannels<TChannel>` 可用且接口稳定）；若 M1 接口有缺口，**反馈 M1 修订（缺口回填归 M1 任务）而非本方案绕开基座**（Oracle 评审 P1-4） | M1 | 依赖 |
| T2 | Google csproj + Options/ChannelConfig（凭证自持） | M1 基座 | 低 |
| T3 | GoogleOidcChannel（Defaults() 配置 + public sub）+ GoogleFederationServiceCollectionExtensions | M1 基座 | 低 |
| T4 | Microsoft csproj + Options/ChannelConfig（**Tenant 配置化**）+ MicrosoftOidcChannel（**pairwise channel_id 复合编码**）+ 注册扩展 | M1 基座 | 中 |
| T5 | 测试：GoogleTestHost + MicrosoftTestHost（**基于 M1 测试基建**——mock token 端点 + JWKS + id_token 正负）；OAuth 正负（code 缺/state 伪造（装配层）/id_token 篡改拒）；**pairwise 语义断言（F2/F3）**；**多 IdP 同时装配枚举（F6——Google+Microsoft+配置型 IdP 三通道）** | M1 组件 8 | 中 |
| T6 | README（技术规范 ×2）+ 使用指南（国外平台行）更新 | — | 低 |

---

## 五、验收标准（对齐国外总规划 F1-F8）

| 需求ID | 验收条件 | 验收方式 |
|--------|---------|---------|
| F1 | 两库零 Initializer 零 `[TKWFExtension]` 零持久化零 Store（纯库判定） | 架构检查 |
| F2 | OAuth 通道：code→token→id_token 验签（M1 基座）正确；缺 code 拒；伪造 state 拒（装配层集成）；**id_token 篡改/伪造拒（信任根负路径）**；**redirect_uri 一致性校验经 M1 基座 `ExchangeCodeAsync`（M2 平台库不重复实现——Oracle 评审 P1-5）**，M2 测试验证 Google/Microsoft token 端点拒绝不一致 redirect_uri | 自动化测试（正负） |
| F3 | **pairwise 语义断言**：同一用户不同 client_id → 不同 channel_id（`microsoft_oidc:{client_id}`）+ 不同 `(channel_id, sub)` **不误并**——**测试 mock Microsoft token 端点按 client_id 返回不同 sub（模拟 pairwise 行为，Oracle 评审 P1-6）**；Google public → 通配 `google_oidc:*` 可并 | 自动化测试 + 语义断言 |
| F4 | `AddGoogleFederationChannels()` / `AddMicrosoftFederationChannels()` 注册进 `IEnumerable<ISsoChannel>` 集合，未装配通道自然跳过 | 自动化测试 + 探针门面 |
| F5 | 红线合规：零 Store / 零构造注入 / 零裸 ORM / 不引 AspNetCore | 代码审查 + skill 自检清单 |
| F6 | **多 IdP 同时装配**（Google + Microsoft + 配置型 IdP）后集合正确枚举所有已注册通道（国外规划 F6 Oracle P2-7） | 自动化测试 + 探针门面 |
| F7 | 凭证自持：ClientSecret 生产 AES-GCM 密文或装配注入，永不明文进配置库 | 代码审查 |
| F8 | 全量回归不破坏（当前基线全绿）；**email 缺失/未验证场景 sub 仍为 external_uid（email 不参与身份归一——Microsoft email 可缺失/Google email_verified=false 测试断言，Oracle 评审 P2-3）** | slnx 测试 |

---

## 六、风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| Microsoft tenant 配置化搅浑单通道语义（一 channel 多 tenant） | 中 | **一 ChannelId = 一 tenant 实例**——消费方按 ChannelId 区分多 Microsoft 通道（`microsoft_oidc:{client_id}` 前缀天然携带 client 维度）；不使用 tenant 数组（保持单实例简单）。**channel_id 不含 tenant——tenant 是访问控制维度（谁能登录：common=AAD+MSA / organizations=仅 AAD），不影响 Microsoft pairwise sub 派生（按 client_id 派生）——同一 client_id 跨 tenant sub 相同，channel_id 不含 tenant 正确（N2 §3.3 定案语义，Oracle 评审 P1-7）** |
| pairwise sub 误并（跨 App 错绑） | 灾难 | channel_id 复合编码（N2 定案）——`{platform}_oidc:{client_id}`，映射表联合唯一约束兜底；F3 断言 |
| M1 基座存在缺口（派生首消费者） | 中 | T1 先验证基座——缺口回填 M1 而非绕开基座写平台逻辑（保持 >80% 复用） |
| Google email 未验证 / Microsoft email 可缺失 | 低 | 不依赖 email 做唯一标识——`sub` 恒为 external_uid（P12）；email 仅展示字段 |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-06 | v0.1.0 | 初始版本——M2 Google+Microsoft 平台网关库开发方案（TKWF.Federation.Google / TKWF.Federation.Microsoft，均基于 M1 基座）：协议事实（两平台标准 OIDC 全矩阵/Google public/Microsoft pairwise+tenant 配置化）+ 组件设计（GoogleOidcChannel 流程 3.1/MicrosoftOidcChannel 差异 3.2/sub 语义与锚点 3.3（pairwise channel_id 复合编码 N2 定案 + Google 通配）/凭证自持 3.4）+ 任务拆解 T1-T6（T1 先验证 M1 基座）+ 验收 F1-F8（pairwise 语义断言 F3 + 多 IdP 枚举 F6）+ 风险（tenant 单实例/pairwise 误并/M1 缺口回填） |
| 2026-10-06 | v0.1.0 | **Oracle 评审（bg_2744eb77 oracle8）PASS WITH CONDITIONS——P1×7 落实**：① §二/§3.2 **Microsoft common/consumers/organizations tenant 授权后 `iss` 含实际租户 GUID**（非字面 tenant 值）——TokenIssuer 白名单须支持通配/正则匹配（与 M1-P1-4 issuer 容错联动）（P1-1）；② §三/§3.3 **channel_id 复合编码构造点定案**——`MicrosoftOidcChannel.ChannelId` 计算属性自动拼接 `$"{ChannelType}:{_config.ClientId}"`（消费方只配 ClientId，不依赖手动拼接）；Google public 经 `BuildChannelId` 返回 `$"{ChannelType}:*"`（P1-2）；③ §3.3 **Google 通配 `*` 语义边界**——字面字符串存映射表精确匹配（非 LIKE）+ 一个消费方只配一个 Google channel 实例（多 App 场景不实现）（P1-3）；④ §四 T1 **"M1 基座前置验证"而非"完成 M1"**——缺口回填归 M1 任务（P1-4）；⑤ §五 F2 **redirect_uri 一致性校验经 M1 基座 `ExchangeCodeAsync`**（M2 不重复实现，测试继承基座行为）（P1-5）；⑥ §五 F3 **pairwise 测试 mock 策略**——mock Microsoft token 端点按 client_id 返回不同 sub 断言不误并（P1-6）；⑦ §六 **tenant 与 channel_id 语义声明**——tenant 是访问控制维度不影响 pairwise sub 派生，channel_id 不含 tenant 正确（N2 §3.3 定案语义）（P1-7）。同步落实 P2 快速项：§3.4 private_key_jwt 证书认证字段预留 / §三 csproj 移除多余 Federation 直接引用 / F8 补 email 语义断言 / 独立包取舍说明 |

<!-- EOF -->