# OIDC 原语归并 Utility.OAuthClient 引擎 开发方案（M1 基座委托 + 引擎边界扩展）

> **立项来源**: 平台网关中期收官后架构评估（2026-10-06——M1 基座 README 已标注"待迁移"）｜ **类型**: 跨仓库重构（扩展 → 主框架 Utility 归并）
> **版本**: v0.1.1 ｜ **日期**: 2026-10-06 ｜ **状态**: 📋 Oracle PASS WITH CONDITIONS（P1×8/P2×3 已落实）
> **关联**: M1 开发方案（`M1-通用OIDC通道基座-开发方案.md`——Oracle P1-3"OIDC 原语若 `Utility.OAuthClient` 引擎可用则委托、否则内联标注待迁移"）｜ 主框架 `v4.10.60-OAuthClient引擎-开发方案.md`（引擎落地 + 三处 YAGNI：JWKS/discovery/缓存/client_credentials）｜ 转达 `转达-Utility.OAuthClient引擎归属-请框架组裁定.md`（归属已裁定选 A 并入 Utility，边界"四职责不扩"）｜ ADR52（收纳准则：算法归 Utility / 装配归扩展）｜ ADR `ADR-Federation-引擎边界扩展与OIDC原语归并.md`（本方案关联 ADR）
> **关键约束**: 引擎零框架依赖保持（纯 BCL——不引 DI/不绑 IHttpClientFactory）；**引擎三处 YAGNI 记载（JWKS/discovery——README §五/方案 Q3；kid 路由经 JWKS 排除隐式）需转达框架组重裁**；跨仓库时序（框架侧落地前扩展侧维持内联零阻塞）

---

## 一、背景与目标

### 现状

M1 通用 OIDC 通道基座（`TKWF.Federation.Oidc` V0.1.0，2026-10-06 落地）三核心文件**内联实现**了 OIDC Client 协议原语：
- `OidcAuthFlow.cs`（273 行）——Discovery 可选解析 / authorize URL 构造 / code→token（client_secret post / private_key_jwt + redirect_uri 一致性 + PKCE S256）/ userinfo 拉取；
- `JwksManager.cs`（161 行）——JWKS 拉取（kid 精确匹配 + RSA ≥2048 过滤 + 响应大小钳制 + L1 缓存 + SemaphoreSlim 并发锁 + 轮换重取）；
- `OidcIdTokenValidator.cs`（209 行）——id_token 验签（alg 强制 RS256 / kid 白名单 / iss 通配正则 / aud/azp / exp·iat leeway / nbf / sub / FixedTimeEquals）。

主框架 `TKW.Framework.Utility.OAuthClient` 引擎（v4.10.60，2026-10-05 落地，23 测试全绿）已提供：authorize 构造 / code 交换 / userinfo / id_token 验签（RS256/ES256/HS256 + alg 白名单 + 混淆防护，**单一 PEM 无 kid 路由**）——归属已由转达裁定闭环（选 A 并入 `TKWF.Utility` 包）。

### 目标

1. **委托层**：`OidcAuthFlow` 的 authorize 构造 / code 交换（client_secret post 形态）/ userinfo / 基础验签 → 改调 `TKW.Framework.Utility.OAuthClient` 引擎（**删内联重复**）；
2. **引擎侧变更（转达框架组）**：
   - **边界扩展**：`JwksManager`（JWKS 拉取 + kid 多密钥路由 + L1 缓存 + 轮换重取）与 **OIDC discovery 可选解析** 上移 Utility——需框架组重批（推翻 YAGNI）；
   - **职责增强**：`OidcIdTokenValidator` 增强（iss 通配/正则 + aud 数组/azp + exp·iat leeway + nbf）——属既有"验签调用"职责内部增强（非边界扩展，YAGNI 不适用——M1 基座已是消费方）；
3. **留扩展侧**：`OidcChannelBase`（`DomainServiceBase, ISsoChannel`——DI/领域依赖，ADR52 排除项）/ `OidcConfiguredChannel` / `OidcPlatformConfig` / 平台 Defaults/MergeConfig 接线保持 `TKWF.Federation.Oidc`。

**成功判据**：
1. `Federation.Oidc` 内联 OIDC 原语归零（authorize/code/userinfo 委托引擎；JWKS/验签增强上移 Utility）——仅保留通道装配层 + private_key_jwt（转达评估，见 §3.2）；
2. 引擎侧变更落地且**零框架依赖保持**（`dotnet-analyzer` 依赖核对实证）；
3. M1 基座 18 测试全绿（改走引擎 API）+ 全量回归 0 失败；
4. 依赖方向：`Federation.Oidc → TKWF.Utility（引擎）+ TKWF.Ext.Federation（契约）` 单向无循环。

---

## 二、问题

### 2.1 现象

- **重复实现**：`OidcAuthFlow` 的 authorize/code 交换/userinfo 与引擎四职责重叠（引擎落地前 M1 内联合理——引擎当时未含；落地后重复）。
- **能力差距**：引擎明确不内置（三处 YAGNI 记载）的 JWKS 拉取 / kid 多密钥路由 / discovery——恰是 M1 `JwksManager`/`OidcIdTokenValidator` 的既有增量能力；M2 Google/Microsoft（iss 通配正则 + pairwise 复合编码）依赖这些增强。

### 2.2 根因

M1 方案 Oracle P1-3 已裁定委托优先（引擎可用则委托）——实施时引擎（v4.10.60）与 M1（v4.10.61 后）几乎同期落地，内联是"引擎未含增量"的现实选择；中期收官后缺口显性化：**内联 OIDC 原语无跨库复用价值**（任何新 OIDC 平台接入都需复制），与"算法归 Utility"（ADR52）背道而驰。

### 2.3 能力差距矩阵（委托/上移判定依据——区分边界扩展与职责增强）

| 能力 | Utility.OAuthClient 引擎 | Federation.Oidc 内联 | 性质 | 处置 |
|------|:---:|:---:|------|------|
| authorize 构造 / code 交换（client_secret post）/ userinfo | ✅ | ✅ | — | **委托引擎**（删内联） |
| JWT 验签（alg 白名单 + 混淆防护） | ✅ RS256/ES256/HS256 单 PEM | ✅ 强制 RS256 | — | 委托引擎基础 + 增强 |
| **JWKS 拉取 + kid 多密钥路由 + L1 缓存 + 轮换重取** | ❌ YAGNI（README §五） | ✅ JwksManager | **真边界扩展**（不在引擎四职责内——需框架组重批推翻 YAGNI） | **上移 Utility** |
| **OIDC discovery 可选解析** | ❌ YAGNI（方案 Q3） | ✅ 配置驱动 | **真边界扩展**（不在四职责内——需框架组重批） | **上移 Utility（可选）** |
| **iss 通配/正则 + aud 数组/azp + exp·iat leeway + nbf** | ❌ 单 Issuer 精确 + 无 leeway | ✅（`OidcIdTokenValidator.cs:177-208`/`:109-134`） | **职责增强**（属"验签调用"四职责内部——YAGNI 不适用，M1 已是消费方） | **上移 Utility（增强）** |
| private_key_jwt（RFC 7523 企业证书） | ❌（`ClientAuthenticationScheme` 无 PrivateKeyJwt） | ✅ 内联 `BuildPrivateKeyJwtAssertion` | — | **转达评估；过渡期 + 终态均内联**（除非引擎扩展 scheme） |
| `OidcChannelBase`（DomainServiceBase, ISsoChannel） | — | ✅ | ADR52 排除项（DI/领域依赖） | **留扩展侧** |

> ⚠️ **边界扩展 vs 职责增强区分（Oracle 评审 P1-4）**：转达 §七"四职责不扩"裁定下——JWKS 拉取 + OIDC discovery **不在**四职责内 = **真边界扩展**（需框架组重批推翻 YAGNI）；kid 路由 / iss 通配 / azp / leeway / nbf **属"验签调用"职责内部** = **职责增强**（引擎 v4.10.60 方案 §6.3 是"已考虑并排除"而非"未含"——论据须以 M1 既有实现为主，M2 仅辅助印证）。

---

## 三、设计

### 3.1 归并后文件布局

```
主框架 _Framework/Utility/OAuthClient/（引擎侧变更——转达框架组实施）
├── JwksManager.cs                    # 新文件（自扩展仓库上移——kid 精确匹配 + L1 缓存 + SemaphoreSlim + 轮换重取 + 大小钳制 + RSA≥2048 过滤）
├── IdTokenValidator.cs               # 新文件（自扩展仓库上移——iss 通配/正则 + aud 数组/azp + exp·iat leeway + nbf + sub + Base64Url 边界）
├── OAuthClient.cs                    # 改造——新增 JWKS 路径分支 / ValidateIdTokenAsync 重载 / discovery 可选（API 草图见 3.3）
├── IdTokenValidationParameters.cs    # 改造——新增可选字段（TokenIssuers/AuthorizedParty/ClockSkew/JwksUri——向后兼容方案 A）
└── （既有 11 文件不动）

扩展仓库 _Framework/Federation.Oidc/（委托后瘦身）
├── OidcChannelBase.cs                # 保留——通道装配层（DomainServiceBase, ISsoChannel + BuildChannelId 复合编码 + Defaults 派生 + MergeConfig）
├── OidcConfiguredChannel.cs          # 保留——自托管 IdP 直配通道
├── OidcPlatformConfig.cs             # 保留——平台配置（端点/TokenIssuer 白名单/PKCE 开关/private_key_jwt 预留）
├── OidcChannelFlow.cs                # 改造替代 OidcAuthFlow——薄层：组装 OAuthClientOptions（引擎参数化）+ 调引擎四职责 + 验签增强（引擎 IdTokenValidator）+ discovery（引擎可选）
└── OidcFederationServiceCollectionExtensions.cs  # 保留——AddOidcFederationChannel/AddOidcDerivedChannels（typed client 注入改引引擎 HttpClient）
```

### 3.2 委托边界与过渡期矩阵（Oracle 评审 P1-1/P1-8 落实）

| OidcAuthFlow 现状方法 | 终态去向 | 过渡期（T3 完成、T2 未完成） |
|------|------|------|
| `BuildAuthorizeUrl` | 引擎 `OAuthClient.BuildAuthorizeUrl` | ✅ **可立即委托**（引擎 v4.10.60 已可用） |
| `GenerateCodeVerifier`/`ComputeCodeChallenge` | 引擎 `Pkce.CreateVerifier`/`CreateS256Challenge` | ✅ **可立即委托** |
| `ExchangeCodeAsync`（client_secret post 形态） | 引擎 `OAuthClient.ExchangeCodeAsync`（PostBody scheme） | ✅ **可立即委托** |
| `FetchUserInfoAsync` | 引擎 `OAuthClient.FetchUserInfoAsync` | ✅ **可立即委托** |
| `DiscoverAsync` | 引擎可选 discovery（转达扩展） | ⏸ **保留内联**（引擎无 discovery——待 T2 后委托） |
| id_token 验签（JwksManager + OidcIdTokenValidator） | 引擎验签基础（单 PEM）+ 引擎 `JwksManager`/`IdTokenValidator` 增强 | ⏸ **保留内联完整链路**（引擎单 PEM 不支持 JWKS kid 路由——验签链路过渡期不委托） |
| `ExchangeCodeAsync`（private_key_jwt 形态） | 转达评估（引擎 `ClientAuthenticationScheme.PrivateKeyJwt` 扩展） | ⛔ **过渡期 + 终态均内联**（引擎无 PrivateKeyJwt——除非引擎扩展 scheme） |

> **F1 标注（Oracle 评审 P1-1）**：F1"内联 OIDC 原语归零"是**终态目标**；过渡期 F1-过渡 = authorize/code/userinfo 委托归零 + 验签链路/private_key_jwt 保留内联。

### 3.3 引擎侧变更设计（转达框架组——Oracle 评审 P1-2/P1-3/P1-4 落实）

#### 3.3.1 两类变更区分（P1-4）

| 类别 | 内容 | 裁定路径 |
|------|------|------|
| **A 边界扩展**（不在引擎四职责内） | JWKS 拉取（JwksManager 上移）+ OIDC discovery 可选 | **转达框架组重批**（推翻 README §五/方案 Q3 两处 YAGNI）——论据：M1 基座已实现 + 已验证 18 用例 + 已是纯 BCL（HttpClient+SemaphoreSlim+RSA）+ ADR52 三不满足（非前瞻猜测，M1 既有消费方） |
| **B 职责增强**（属"验签调用"职责内部） | kid 多密钥路由 + iss 通配/正则 + aud 数组/azp + exp·iat leeway + nbf（IdTokenValidator 上移） | **转达框架组增强既有职责**（YAGNI 不适用——引擎 v4.10.60 已含"验签调用"职责，增强是其内部实现演进） |

#### 3.3.2 API 设计草图（向后兼容——P1-2/P1-3；请框架组评估采纳 A/B）

**`IdTokenValidationParameters` 扩展（方案 A 推荐——向后兼容）**：
```csharp
public sealed record IdTokenValidationParameters(
    string Issuer,                                  // 既有字段不动（兼容 v4.10.60 消费方）
    IReadOnlyCollection<string> Audiences,
    string? VerificationKeyPem,                     // 既有字段不动（单 PEM 路径）
    string? ExpectedNonce = null,
    IReadOnlyCollection<string>? AllowedAlgorithms = null,
    // ── 新增可选字段（null 时行为 = 既有 v4.10.60 语义）──
    IReadOnlyCollection<string>? TokenIssuers = null,  // iss 通配/正则列表（非空时替代 Issuer 单值——向后兼容）
    string? JwksUri = null,                            // 非空 → JWKS kid 路由验签（VerificationKeyPem 二选一）
    string? AuthorizedParty = null,                    // azp 校验（aud 数组多方场景）
    TimeSpan? ClockSkew = null);                       // exp/iat/nbf leeway（缺省 30s）
```

**`OAuthClient` 验签 API 扩展**：
```csharp
// 既有（不动）：同步单 PEM 验签
public JsonElement ValidateIdToken(string idToken, IdTokenValidationParameters validation);
// 新增（JWKS 路径——异步拉取 + kid 选区）：
public Task<JsonElement> ValidateIdTokenAsync(string idToken, IdTokenValidationParameters validation, CancellationToken ct = default);
// JwksManager 作为引擎内部协作类（sealed internal or public——框架组裁；推荐 public sealed 供高级场景 + IVT 测试）
```

**`OAuthClientOptions` discovery 扩展（可选）**：
```csharp
public sealed record OAuthClientOptions(..., Uri? DiscoveryUri = null);  // 非空 → 构造时可选解析端点覆盖
```

**方案 B（备选——不动既有 record）**：新增 `JwksIdTokenValidationParameters` record + `ValidateIdTokenAsync` 重载——零破坏但 API 面分裂。

#### 3.3.3 private_key_jwt（转达评估——YAGNI 倾向扩展侧保留）

引擎 `ClientAuthenticationScheme` 补充 `PrivateKeyJwt`（RFC 7523——`ClientAssertionSigningKeyPath` 注入）——低频企业证书场景（M2-P2-1 预留）；YAGNI 倾向**扩展侧保留内联**（`OidcChannelFlow` 内嵌 `BuildPrivateKeyJwtAssertion`——现状可用，非主流平台场景）。框架组评估 client_credentials 时一并考虑（见 3.3.4）。

#### 3.3.4 client_credentials 核实（Oracle 评审 P2-1——归属转达框架组）

**ADR102（主框架私有 V5 提议 ADR，状态 🔴 提议）§4.1 声称引擎支持 client_credentials + 用户/租户声明——但引擎 v4.10.60 代码 `OAuthClient.cs:280` grant_type 硬编码 `authorization_code`，`ClientAuthenticationScheme` 无该形态——文档/代码不一致**。本方案**不直接核实/修改**（ADR102 归主框架）——转达条目请框架组核实 ADR102 表述 + 评估 client_credentials 纳入（服务端凭证流——OAuthServer 立项（见关联）的必备 grant，与 3.3.3 一并裁）。

### 3.4 依赖方向（不变）

```
Federation.Oidc（通道层）→ TKWF.Utility（引擎——PackageReference/CPM）+ TKWF.Ext.Federation（ISsoChannel 契约）
Federation.Google/Microsoft（派生库）→ Federation.Oidc（不变）
```

---

## 四、任务拆解

| 任务 | 描述 | 关联 | 工作量 |
|------|------|------|--------|
| T1 | **转达框架组**（引擎侧变更裁定）：两类区分（A 边界扩展——JWKS/discovery 重批 YAGNI；B 职责增强——kid/iss/azp/leeway）+ **API 设计草图（§3.3.2 方案 A 推荐 + 向后兼容策略）** + private_key_jwt 评估 + **client_credentials 核实（ADR102 文档/代码不一致——归属框架组）** | 主框架 `docs/03_扩展模块/转达/`（已起草，随本方案定稿提交） | 低 |
| T2 | 框架侧实施（v4.10.x 迭代）——引擎新增 JwksManager/IdTokenValidator 增强/JWKS 路径/discovery/private_key_jwt（框架组，按 T1 裁定）→ CPM 升级（扩展仓库 `Directory.Packages.props`） | 主框架 + 扩展 CPM | 中（框架侧） |
| T3 | 扩展侧委托改造（**过渡期先做可委托部分**——authorize/code/userinfo 调引擎；验签链路/private_key_jwt/discovery 过渡期保留内联）：OidcAuthFlow → OidcChannelFlow 薄层（引擎参数化组装 + 调四职责 + 验签增强 + discovery）+ 删内联重复 | 扩展仓库 `_Framework/Federation.Oidc/` | 中 |
| T4 | 测试适配：18 测试改走引擎 API（Stub 端点不变——验签正负/pairwise/PKCE/Discovery/multi-IdP 枚举）+ **引擎侧新增文件测试量化（P2-2——对齐 M1 既有 18 用例负路径：kid 不匹配重取 / iss 通配正则 / azp 多受众 / nbf 未来 / 过期 / 篡改 + JWKS 响应超限 / RSA<2048 拒）** | 扩展 + 框架测试项目 | 中 |
| T5 | 全量回归 + 文档：README 更新（委托注记 → 已委托）+ 转达闭环记录 + ADR 落档 + 方案变更记录 | — | 低 |

---

## 五、验收标准

| 需求ID | 验收条件 | 验收方式 |
|--------|---------|---------|
| F1 | `Federation.Oidc` 内联 OIDC 原语归零（**终态**——authorize/code/userinfo 委托引擎；JWKS/验签增强上移 Utility；仅剩通道装配层 + private_key_jwt 内联（转达评估保留））｜ **过渡期 F1-过渡**：authorize/code/userinfo 委托归零，验签链路/private_key_jwt 保留内联 | 架构检查（grep 内联协议逻辑） |
| F2 | 引擎侧变更落地（JwksManager/IdTokenValidator 在 `TKW.Framework.Utility.OAuthClient`）且零框架依赖保持（纯 BCL）｜ **向后兼容**（v4.10.60 既有消费方——`Issuer`/`VerificationKeyPem` 单值路径行为不变，方案 A） | `dotnet-analyzer` 依赖核对 + 引擎既有 23 测试保持全绿 |
| F3 | M1 基座 18 测试全绿（改走引擎 API）+ 全量回归 0 失败（扩展侧）；引擎侧新增文件测试全绿（框架侧——P2-2 量化负路径） | slnx 测试 |
| F4 | 依赖方向单向：`Federation.Oidc → TKWF.Utility + TKWF.Ext.Federation` 无循环 | 依赖核对 |
| F5 | **（修订——Oracle 评审 P1-6）** 本方案验收 = M1 基座 18 测试全绿（验签正负/issuer 容错/PKCE/Discovery/multi-IdP 枚举改走引擎 API）；派生库（Google/Microsoft）委托后行为不变**待 M2 落地后执行**（标注 F5 后续项，非本方案验收门槛） | M1 18 测试 |

---

## 六、风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| 引擎 A 类边界扩展（JWKS/discovery）被框架组否（维持 YAGNI） | 高 | 转达含充分依据（M1 既有实现主据——已实现 + 已验证 18 用例 + ADR52 三不满足 + kid/iss/azp 属验签职责增强非边界扩展）；否 → 扩展侧保留 JwksManager 内联（现状维持，仅委托 authorize/code/userinfo——F1 降级部分达成，记录 ADR 决策） |
| 跨仓库时序（T2 发布前扩展侧等待） | 中 | **零阻塞**——扩展侧先做可委托部分（authorize/code/userinfo 调引擎——v4.10.60 已可用），JWKS/验签增强待 T2 后委托（过渡期内联保留，F1-过渡） |
| **private_key_jwt 阻塞 code 交换委托（Oracle 评审 P1-8）** | 低 | 明示：private_key_jwt 平台 `ExchangeCodeAsync` **过渡期 + 终态均内联**（引擎 `ClientAuthenticationScheme` 无 `PrivateKeyJwt`——除非引擎扩展 scheme，转达评估） |
| **YAGNI 推翻论证混淆（Oracle 评审 P1-4）** | 中 | 方案已重构为两类区分（§2.3/§3.3.1）——A 边界扩展（重批）/ B 职责增强（不适用 YAGNI）；论据以 M1 既有实现为主、M2 仅辅助印证（P2-3） |
| **引擎 API 破坏性变更（Oracle 评审 P1-2）** | 中 | 向后兼容方案 A（§3.3.2——新增可选字段 null 回退既有语义）优先；方案 B 备选；转达含 API 草图 |
| **ADR102 client_credentials 文档/代码不一致（Oracle 评审 P2-1）** | 中 | 归属转达框架组核实（不扩展侧直接改）；若 OAuthServer 立项（见关联）则 client_credentials 为 OP 必备 grant，与 3.3.3 一并裁 |

---

## 七、评审结论与条件闭环表（Oracle bg_93912e8e，2026-10-06）

**Oracle PASS WITH CONDITIONS——P1×8 + P2×3 全部落实**：

| # | 条件 | 落实 |
|:-:|------|------|
| P1-1 | 过渡期委托切分未明示——验签链路过渡期不委托 | §3.2 过渡期矩阵（⏸ 验签链路/⛔ private_key_jwt）+ F1 终态/F1-过渡标注 |
| P1-2 | 引擎 API 破坏性变更未处理 | §3.3.2 方案 A（推荐——新增可选字段 null 回退）/ 方案 B 备选 + 转达含 API 草图 |
| P1-3 | JWKS 与 ValidateIdToken 接线缺 API 设计 | §3.3.2 `ValidateIdTokenAsync` + `JwksUri` 字段分支 + JwksManager 形态（框架组裁） |
| P1-4 | YAGNI 推翻论证混淆边界扩展与职责增强 | §2.3/§3.3.1 两类区分（A 边界扩展重批 / B 职责增强）+ 论据改 M1 既有实现主据 |
| P1-5 | 边界扩展缺 ADR | 新增 `docs/Federation/ADR/ADR-Federation-引擎边界扩展与OIDC原语归并.md`（三问必填） |
| P1-6 | F5 验收不可测（M2 未建） | §五 F5 修订——M1 18 测试为验收门槛，派生库行为不变标注"待 M2 落地后执行" |
| P1-7 | OAuthServer 移交口径偏移（触发条件被动 vs 前瞻） | 关联节改"转达框架组前瞻评估（不等待触发条件）" |
| P1-8 | private_key_jwt 阻塞 code 交换委托未明示 | §3.2/§六——private_key_jwt 平台 ExchangeCodeAsync 过渡期 + 终态均内联 |
| P2-1 | client_credentials 不一致核实归属 | §3.3.4——归属转达框架组核实（ADR102 归主框架） |
| P2-2 | 引擎新增文件测试覆盖未量化 | §四 T4——对齐 M1 18 用例负路径清单 |
| P2-3 | M2 前瞻论据弱化 | §2.3/§3.3.1——M1 既有实现主据（已实现 + 已验证 + 纯 BCL），M2 仅辅助 |

---

## 关联：标准 OAuthServer（OP）转达框架组前瞻评估（Oracle 评审 P1-7 落实）

> 独立于本方案（非本方案实施范围）——2026-10-06 用户裁定 + 转达框架组（`转达-OIDC原语归并引擎与OAuthServer前瞻-请框架组评估.md`）：

- **现状**：OAuthClient（RP）面已闭环（引擎 + 6 平台库）；Server 面我方已有**自研协议版**（Federation accesscode + token2 ES256 + JWKS 分发 + profile API——自有生态闭环）；**标准协议版**（标准 `authorize`/`token`/`userinfo`/`jwks` 端点）主框架零资产。
- **裁定（2026-10-06 用户）**：**转告框架组前瞻评估、完成，扩展侧再迁移适配**——"框架需要一定的前瞻考虑，不是根据'已有代码实现'和'现在的需求'。既然是中小企业开发的'通用需求'，何况已经提供了 OAuthClient 又实现了 OAuthServer 的部分"。
- **移交口径（Oracle 评审 P1-7）**：**不等待触发条件**——框架组**主动前瞻评估**（资产复用可行性 + 与自研协议关系），评估完成后扩展侧 AuthCenter/Federation 迁移适配框架成果。
- **资产复用清单（供框架组评估输入）**：`Token2Service` ES256 签发（token 端点）/ `AuthCenter` RS256 令牌体系（可选）/ 本方案 JwksManager（JWKS 分发侧）/ 引擎对称原语（`Pkce`/`StateGenerator`）/ `client_credentials`（**引擎缺口 + ADR102 不一致——§3.3.4 一并裁**）。

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-06 | v0.1.0 | 初始版本——OIDC 原语归并 Utility.OAuthClient 引擎开发方案：委托层（authorize/code/userinfo 调引擎）+ 引擎边界扩展（JwksManager/IdTokenValidator 上移 + discovery 可选 + private_key_jwt 评估——转达框架组推翻三处 YAGNI）+ 留扩展侧（OidcChannelBase 等通道装配层）+ 关联问题 2 标准 OAuthServer 转达登记 |
| 2026-10-06 | v0.1.1 | **Oracle 评审（bg_93912e8e）PASS WITH CONDITIONS——P1×8/P2×3 落实**：① 过渡期委托矩阵（§3.2）+ F1 终态/F1-过渡标注（P1-1）；② 引擎 API 向后兼容方案 A/B + 转达 API 草图（P1-2/P1-3）；③ YAGNI 推翻论证重构——边界扩展（JWKS/discovery）vs 职责增强（kid/iss/azp/leeway）两类区分 + M1 既有实现主据（P1-4/P2-3）；④ 补 ADR（P1-5）；⑤ F5 改可测——M1 18 测试为门槛、派生库待 M2 落地（P1-6）；⑥ OAuthServer 移交改前瞻评估不等待触发（P1-7）；⑦ private_key_jwt 阻塞委托明示（P1-8）；⑧ client_credentials 核实归属转达框架组（P2-1）+ 引擎新增文件测试量化（P2-2）；同步落实 P2×3（client_credentials 归属 / 测试覆盖量化 / M2 论据弱化） |

<!-- EOF -->
