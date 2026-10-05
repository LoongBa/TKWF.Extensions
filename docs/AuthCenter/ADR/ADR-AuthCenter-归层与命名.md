# ADR-AuthCenter-归层与命名（AuthCenter / Federation / Connectors / Utility 四层模型）

## 状态

**活跃**——Oracle 评审 **PASS WITH CONDITIONS**（2026-10-05，bg_3518b273）；P1 四项修订已落实（§五.1 Utility 转达 / §五.3 存储契约 / §五.7 契约包定论 / §六.1 迁移路径分阶段），P2 二项标注开发方案期细化

## 一、目的与目标

确立认证体系扩展的**最终归层模型**：认证体系拆分为四层——**`TKWF.Ext.AuthCenter`**（联盟核心/认证中心：对内、持久化+装配）、**`TKWF.Ext.Federation`**（外交部/对外身份接口：配置装配、**自含实体**、编排平台网关）、**`TKWF.Federation.{平台}`**（大使馆/平台网关库：`WeChat`/`QQ`/`Google`/`Alipay`——平台名直接挂 Federation 域下，**无持久化纯库**）、**`TKWF.Utility.OAuthClient`**（引擎/协议：纯 BCL，被上层调用）。读者应在 3 句话内明白：认证体系 = AuthCenter（对内核心）+ Federation（对外接口）+ Federation.{平台}（平台网关库，如 `TKWF.Federation.WeChat`）+ Utility.OAuthClient（引擎）；**"不是所有 AuthCenter 都需要 Federation"**（单应用认证中心可单独装，需要对外时才加装外交部）；Federation **自含实体**（SsoClient/SsoAccessCode——数据访问直接经 SG1 DataService，**不建 Store 伪层**，skill 铁律）。

## 二、问题

### 问题现象

- **SSO 命名误导**：`TKWF.Ext.SSO` 名称暗示"单点登录体验"，但实际承担的是"对外身份接口层（联邦结构）"——SSO 是**体验属性**（用户一次登录到处通行），Federation 是**结构属性**（对外身份接口 + IdP 连接）——命名与职责错位（术语对齐：Federation vs SSO 区分的延续）。
- **AuthCenter 与 Federation 职责耦合**：既有的 SSO 联邦层与 Authentication 认证内核在**数据层已强耦合**——联邦锚点数据模型（`AuthAccountEntity.FederationAnchorOpenId` 唯一索引 + `PlatformAccountMapEntity.ChannelId/ExternalUserId` 联合唯一索引）**物理落在 Authentication 主包实体上**，主包服务已多接口实现 SSO 契约（`AuthAccountQueryService : IAuthAccountQueryService + ISsoAccountQueryService + ISsoAccountLinkService`）——两扩展"名分两包、数据不分家"，契约边界是人为的。
- **平台适配误建为扩展**：ADR-SSO 规划 `TKWF.Ext.SSO.{平台}` 卫星扩展——但平台网关（微信/QQ/Google 客户端）**无持久化、纯协议**，按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库/Utility"判据，应为**库**而非扩展（每次平台适配一个包 = 包泛滥）。
- **异构客户端对接成本高**：现状 token2 fragment 投递 + 应用自研 `/auth/exchange`——Node.js/静态页等异构客户端需自研 JWT 验签 + 建会话，无标准库支撑（`openid-client`/`oidc-client-ts` 无法直接对接非标准端点）。

### 触发场景

- 认证中心立项评估：单应用（内部账号）vs 多应用（统一登录态）vs 接外部 IdP——需要按需装配的清晰分层。
- 平台适配规划：微信/QQ/Google/支付宝接入——是扩展还是库？需要判据。
- 异构客户端（.NET / Node.js / 静态页）对接认证中心——需要标准 OAuth 接口 vs 自研协议裁定。
- 名称语义：`SSO` 名称在联邦结构语境下误导消费方。

### 现有方案不足

- **`TKWF.Ext.SSO` 独立模块（ADR-SSO 选项 A）**：将联邦层拆为独立扩展 + 组合式消费认证内核——但①数据层已耦合（联邦锚点实体在主包）②SSO 命名误导 ③平台适配卫星扩展（`.{平台}`）按库应更轻——**"拆 SSO 独立扩展"的收益（组合矩阵第三形态零全量传递）已被 Connectors 下沉为库的方案取代**（平台网关成库后，纯外部联邦场景引 AuthCenter/Federation + Connectors 库，零内部 Provider 全量）。
- **SSO 名称**：体验词（Single Sign-On）与结构职责（对外身份接口）错位，需更准确的"Federation"。

## 三、使用场景

### 适用场景

| 消费方装配 | 获得能力 | 说明 |
|---|---|---|
| 只引 `TKWF.Ext.AuthCenter` | 单应用认证中心（内部账号/短信/微信 Provider + 令牌） | 不需要对外联邦时，无需外交部 |
| AuthCenter + Federation | 认证中心 + 对外身份接口（token2/accesscode/JWKS，无持久化，编排 Connectors） | 多应用统一登录态；Federation 存储契约归 AuthCenter 实现 |
| Federation + Connectors 库 | 纯外部联邦登录（BYO IdP——V5 国外客户主力形态） | 平台网关成库后零内部 Provider 全量 |
| AuthCenter + Federation + Connectors | 完整认证中心实例（内部认证 + 对外联邦 + 平台接入） | 认证中心完整形态 |

### 不适用边界

- **UserCenter 档案面**：独立扩展（`TKWF.Ext.UserCenter`）——档案面是另一领域，不属认证归层。
- **通用支付网关**（纯转发型）：`TKWF.Federation.{平台}` 的网关是"平台适配（双向）"；若出现纯转发型网关另行评估 `TKWF.Gateways`。
- **AuthCenter 与 Federation 密钥体系**：RS256（AuthCenter 内部令牌）与 ES256（Federation token2）**独立密钥域**——合并后仍须保持隔离（ADR 既有裁定，不合并密钥体系）。

## 四、选项

### 选项 A：四层归层模型（AuthCenter / Federation / Connectors / Utility.OAuthClient）——**选定**

- 描述：认证体系拆四层——AuthCenter（对内核心，持久化+装配）/ Federation（对外接口，配置装配无持久化）/ Connectors（平台网关库，无持久化纯库）/ Utility.OAuthClient（引擎，纯 BCL）。
- 优点：职责清晰（联盟核心/外交部/大使馆/引擎各司其职）；"不是所有 AuthCenter 都需要 Federation"按需装配；平台适配成库（无包泛滥）；异构客户端可经标准 OAuth 对接（v0.2.0 迭代）。
- 缺点：需合并已实施代码（Authentication + SSO → AuthCenter/Federation）+ 废弃/修订两份旧 ADR + 测试迁移（107 用例）。

### 选项 B：维持现状（Authentication 单包 + SSO 独立模块 + 平台卫星扩展）

- 描述：延续 ADR-Authentication 选项 A（单包）+ ADR-SSO 选项 A（SSO 独立）+ `.{平台}` 卫星扩展。
- 优点：成本低（无合并/迁移）；ADR 稳定。
- 缺点：SSO 命名误导；平台适配包泛滥（每平台一个扩展）；AuthCenter/Federation 数据层耦合但包分离——"名分两包、数据不分家"的治理张力持续。

### 选项 C：Federation 并入 AuthCenter（单扩展）+ Connectors 库

- 描述：认证体系 = `TKWF.Ext.AuthCenter`（含认证内核 + 对外接口）+ Connectors 库。
- 优点：少一个包。
- 缺点：**违背"不是所有 AuthCenter 都需要 Federation"**——单应用认证中心被迫背对外接口层；与用户裁定"分开合理"相悖。

## 五、决策

选定：**选项 A——四层归层模型**。

1. **命名与职责**：
   - `TKWF.Ext.AuthCenter`：联盟核心/认证中心（对内）——账号/令牌/登录保护/票据（持久化+装配）。
   - `TKWF.Ext.Federation`：外交部/对外身份接口——token2/accesscode/JWKS 协议编排 + `ISsoChannel` 窄适配（Connector → 身份获取方向）+ `FederationOptions`（单/多身份配置）；**自含实体**（`SsoClientEntity`/`SsoAccessCodeEntity`——client 注册表/授权码状态是协议编排核心状态），数据访问直接经 SG1 DataService（门面 `User.Use<DataService>()`，不建 Store——skill 铁律）。
   - `TKWF.Federation.{平台}`：大使馆/平台网关库——`TKWF.Federation.WeChat`/`.QQ`/`.Google`/`.Alipay`（平台名直接挂 Federation 域下，少 Connectors 中间层——用户裁定；**独立库域语义**：与 `TKWF.Ext.*`（扩展）/`TKWF.Utility`（引擎）平级，命名空间表归属（联邦域）、程序集保独立，可独立包发布不引 Federation 扩展——Oracle P2-1 命名一致性关切以"独立库域 + 命名空间表归属"回应）——双向（出站签名调用 + 入站接收验签）+ 内存态缓存，**无持久化纯库**。
   - `TKWF.Utility.OAuthClient`：引擎——authorize 构造/code 交换/令牌解析/验签调用，BCL `HttpClient`，**零框架依赖**（不绑 `IHttpClientFactory`），归主框架 Utility（`TKW.Framework.Utility` 命名空间族）。**⚠️ 归属标注（Oracle P1-4）**：本 ADR 是扩展仓库文档，**主框架 Utility 归属须转达框架组裁定**——本 ADR 仅记录方向（先例 `TKW.Framework.Utility.Cryptography` 的 `WeChatPaySignUtil`/`AlipaySignUtil` 已存在），不作为本仓库决策项。
2. **"不是所有 AuthCenter 都需要 Federation"**（用户裁定）：单应用认证中心只装 AuthCenter；需要对外统一身份接口时才加装 Federation——分开合理。
3. **Federation 持久化与数据访问（2026-10-05 user+tkwf-extension skill 复核修订——不建 Store）**：Federation **保留自有实体 `SsoClientEntity`/`SsoAccessCodeEntity`**（client 注册表/授权码状态是 Federation 协议编排的核心状态，不迁回 AuthCenter）；数据访问**直接经 SG1 DataService**（门面 `User.Use<SsoClientEntityDataService>()` 懒加载组合，红线合规）——**不创建 `ISsoClientStore`/`ISsoAccessCodeStore` 伪层**（tkwf-extension skill §5 铁律：CRUD 归 SG1 DataService，组合归门面；Settings `ISettingStore`/SSO 阶段 1 Store 曾为反例已回退）。签名密钥（EC PEM 文件）Federation 自持（配置文件非 DB 持久化）。
4. **单/多身份配置**（用户裁定）：Federation 不关心"一个身份 vs 多个身份"——取决于传入配置（装载的 Connector 实例数），联邦层逻辑不变。
5. **SSO 命名废弃 → Federation**：SSO（体验词）废弃，Federation（结构词）采用。
6. **平台适配 = 库非扩展**：`TKWF.Federation.{平台}`（单包多命名空间，少分包），微信/QQ/Google/支付宝均为 `TKWF.Federation.WeChat` 等命名空间内类；SSO.WeChat 开发方案改写为 `WeChatConnector`（`TKWF.Federation.WeChat`）库实施（降级：从含 Initializer 的扩展 → 纯库，无 `[TKWFExtension]`）。
7. **契约包去留（Oracle P1-2 定论 + skill 复核）**：`Authentication.Abstractions` **保留**（SSO 消费面契约：`ISsoAccountQueryService`/`ISsoChannelMapService`/`ISsoAccountLinkService` + DTO）——Federation 经契约消费 AuthCenter 的账号/映射（跨扩展消费面）；**不再新增存储契约**（Federation 自含实体，数据访问直接经 SG1 DataService，无需契约包承载 Store）；命名随归层调整（`AuthCenter.Abstractions` 或保留——评审后定）；V0.5.x 消费方 public API 位置变更**非破坏性设计**（命名空间保持 `TKWF.Ext.Authentication` + `TypeForwardedTo`，或评估过渡期）。
8. **标准 OAuth 接口（v0.2.0 迭代）**：Federation 后续迭代在 Service 层之上补标准 OAuth 端点薄层（`/authorize` `/token` `/userinfo` `/.well-known/openid-configuration`）——异构客户端（.NET/Node.js/静态页）经标准 OAuth 库对接（前瞻，本 ADR 记录方向，v0.2.0 细化）。

## 六、后果

### 正面影响

- 职责清晰：联盟核心/外交部/大使馆/引擎四层各司其职，命名语义准确（Federation 取代误导性的 SSO）。
- "不是所有 AuthCenter 都需要 Federation"按需装配，单应用认证中心不背对外接口层。
- 平台适配成库（单包多类），消除每平台一个扩展的包泛滥。
- 异构客户端可经标准 OAuth 对接（v0.2.0），消除自研对接成本。
- 数据层已耦合的 AuthCenter/Federation 名实归一（联邦锚点实体在主包、主包服务多接口实现契约）。

### 负面影响 / 风险

- **破坏性重构**：合并已提交代码（Authentication V0.5.3 + SSO V0.1.0 → AuthCenter/Federation），107 测试用例（88+19）需迁移重定向（`SsoContractTests` 7 例跨项目）。
- **ADR 修订**：ADR-Authentication（选项 A 单包）与 ADR-SSO（选项 A 独立模块）需标注「已废弃」+ 引用本 ADR（AGENTS §4 生命周期）。
- **契约包 public API 位置变更**：若 `Authentication.Abstractions` 内联/更名，影响 V0.5.x 消费方（ADR-SSO §六已登记风险）——评审后定去留。
- **密钥体系隔离**：RS256（AuthCenter）与 ES256（Federation）独立密钥域——合并后仍须保持隔离（`DevRsaKeyCache`/`DevEcKeyCache` 两套缓存并存）。

### 后续待办

- ✅ **Oracle 评审完成（bg_3518b273，PASS WITH CONDITIONS）**——P1 四项修订已落实本 ADR（存储契约设计 §五.3 / 契约包定论 §五.7 / 迁移路径 §六.1 / Utility 归属转达标注 §五.1），P2 二项标注为开发方案期细化。
- **修订开发方案**：SSO 联邦层开发方案 → Federation（**自含实体 + 数据访问直接经 SG1 DataService**，不建 Store）；SSO.WeChat → `TKWF.Federation.WeChat` 库。t` 库。
- 实施迁移（见 §六.1 分阶段）。

### 6.1 迁移路径（2026-10-05 skill 复核修订——不建 Store、不拆实体）

**原则（tkwf-extension skill §5 铁律）**：Federation **保留自有实体**（SsoClientEntity/SsoAccessCodeEntity），数据访问直接经 SG1 DataService（`User.Use<DataService>()`）——**不创建 Store 伪层**（阶段 1 曾试建 ISsoClientStore/ISsoAccessCodeStore，已回退 763e614）。

**阶段 1（无 NuGet 破坏——SSO 未发布窗口期）**：
1. **代码零移动**（SSO 实体/服务/测试保持现状——已满足 skill 正确路线：门面继承 DomainServiceBase + 直接 User.Use<DataService>）
2. **命名归层**：SSO → Federation 纯命名调整（项目/命名空间/README 表述；实体 DataService 等生成物不变）——`TKWF.Ext.SSO` → `TKWF.Ext.Federation`（MinVerTagPrefix SSO→Federation，未发布无破坏）
3. `Authentication.Abstractions` 契约包随命名调整（`AuthCenter.Abstractions` 或保留——评审后定）；**不新增存储契约**（Federation 自含实体，跨扩展消费面仅账号/映射契约）
4. SSO.WeChat → `TKWF.Federation.WeChat` 降级为库（无 Initializer 无 `[TKWFExtension]`——平台网关从扩展降为库）
5. 测试路径对齐：生产路径集成（真实 DI + BindScope + `User.Use<接口>()`）+ 分层单测（可配置 stub 直构门面）——**测试宿主不得手写 Store/掩盖守卫**（skill §4.5）
6. 全量回归（107 + 全量 1647）

**阶段 2（有 NuGet 破坏——需消费方协同）**：
7. `TKWF.Ext.Authentication` → `TKWF.Ext.AuthCenter` 包名变更（破坏性——EduPlatform 等已消费方）+ Initializer 类名（`AuthCenterExtensionInitializer` 已吻合）
8. 消费方（EduPlatform）迁移 + NuGet 包 deprecated 过渡
9. **阶段 2 破坏性评估（Oracle P1-3）**：Authentication 已打 tag + 已发布 NuGet（v0.5.x，消费方 EduPlatform）——重命名为 AuthCenter 的成本/收益比需重新评估；**折中方案**：保留 `TKWF.Ext.Authentication` 包名（NuGet 契约）+ 内部归层用 AuthCenter 概念（文档/命名空间按需），或 `TKWF.Ext.AuthCenter` 新包名 + 旧包 deprecated 过渡期（评审后定）。

**Initializer 说明（Oracle P2-2 + skill 复核）**：Federation 自含实体（SG1 DataService 自动注册）——`FederationExtensionInitializer`（原 SsoExtensionInitializer）独立注册门面（AddConstructibleService）+ 多实现集合（TryAddEnumerableConstructible ISsoChannel）；AuthCenter（`AuthCenterExtensionInitializer`）独立注册其门面；**两 Initializer 并存**，消费方按需白名单声明（AuthCenter 单装 / AuthCenter+Federation 组合 / 未来 Federation+Connectors 纯外部）。零 Store 契约注册（skill 铁律）。

## 七、关联文档

- 既有 ADR（本 ADR 将标注其「已废弃」或修订关系）：`ADR-Authentication-认证中心命名与边界.md`（选项 A 单包）、`ADR-SSO-模块立项与契约归属.md`（选项 A 独立模块 + Oracle PASS WITH CONDITIONS）。
- 需求设计文档：`_TCloud/docs/协作/记录/20261005-01-认证中心设计方案.md`（v4，将归档）。
- 先例：`TKW.Framework.Utility.Cryptography`（`WeChatPaySignUtil`/`AlipaySignUtil`——"按平台分函数"验签资产，Utility 归层先例）、`UserCenter.Abstractions`（契约归中立层、实现方提供——Federation 存储契约模式对齐）。

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-05 | 初始草案——认证体系四层归层模型（AuthCenter/Federation/Federation.{平台}/Utility.OAuthClient）："不是所有 AuthCenter 都需要 Federation"按需装配；Federation 无持久化（存储契约归调用者）；单/多身份取决配置；SSO 命名废弃→Federation；平台适配=库非扩展（单包多命名空间）；标准 OAuth 接口前瞻（v0.2.0） |
| 2026-10-05 | **Oracle 评审（bg_3518b273）PASS WITH CONDITIONS，P1 四项 + P2 二项落实**：① P1-1 §五.3 存储契约设计——定义 `ISsoClientStore`/`ISsoAccessCodeStore`/`ISsoSigningKeyStore`（Federation 定义、AuthCenter 实现）+ `SsoClientEntity`/`SsoAccessCodeEntity` 迁回 AuthCenter + Federation 层厚度（协议编排层）；② P1-2 §五.7 契约包定论——保留并更名 `TKWF.Ext.AuthCenter.Abstractions` + 内容扩展（加存储契约）+ V0.5.x 非破坏性设计（命名空间保持 + TypeForwarded）；③ P1-3 §六.1 迁移路径分阶段——阶段 1（无破坏：Federation 拆解/实体迁回/契约包更名）+ 阶段 2（破坏性：Authentication→AuthCenter 包名 + 消费方协同 + 保留包名折中评估）；④ P1-4 §五.1 Utility 归属标注转达框架组（本 ADR 是扩展仓库文档，主框架归属不越权裁定）；⑤ P2-1 命名一致性——`TKWF.Federation.{平台}`（用户裁定少 Connectors 中间层）以"独立库域 + 命名空间表归属、程序集保独立"回应 Oracle 关切；⑥ P2-2 §六.1 Initializer 并存说明（AuthCenter + Federation 两 Initializer，Store 实现归 AuthCenter） |
| 2026-10-05 | **skill 复核修订（用户裁定 + tkwf-extension skill §5 铁律——Store 伪层回退 763e614）**：阶段 1 曾实施 Store 拆解（ISsoClientStore/ISsoAccessCodeStore 契约 + 实体迁回 AuthCenter + Federation 改调 Store）——经 tkwf-extension skill 检查认定违反「不创建 Store 伪 DataService 层」（CRUD 归 SG1 DataService，组合归门面；Settings ISettingStore 反例同型），**完整回退**（Revert d1c50da + 工作区 46 项变更丢弃）。修订：§五.3 **Federation 保留自有实体**（SsoClientEntity/SsoAccessCodeEntity 不迁回），数据访问直接经 SG1 DataService（门面 `User.Use<DataService>()`），零 Store 合约；§五.7 契约包不再新增存储契约；§六.1 迁移路径改**代码零移动、纯命名归层**（SSO→Federation 命名 + 平台适配降级为库 + 测试路径对齐）；Initializer 说明改写（两 Initializer 并存，零 Store 注册）。AGENTS.md 显式强化「禁手写 Store」红线 + 新增「扩展开发强制 Skill 路由」（扩展= TKWF 消费方，参考 Agent_Use_TKWF.md §7） |
