# ADR-SSO-模块立项与契约归属

## 状态

**已废弃**（2026-10-05）——被 [ADR-AuthCenter-归层与命名](../AuthCenter/ADR/ADR-AuthCenter-归层与命名.md) 取代：认证体系归层为 AuthCenter（对内核心）/ Federation（对外接口）/ Federation.{平台}（平台网关库）/ Utility.OAuthClient（引擎）四层。本 ADR 的"SSO 独立模块 + 平台卫星扩展"裁定被修订：SSO 联邦层能力拆解为 `TKWF.Ext.Federation`（无持久化外交部，存储契约归调用者）+ 持久化状态归 `TKWF.Ext.AuthCenter`；平台适配降级为库（`TKWF.Federation.{平台}`，非扩展）；SSO 命名废弃→Federation（体验词→结构词）。历史裁定保留作演进记录，不删除。原 Oracle 评审（PASS WITH CONDITIONS，bg_2ab92af3）结论在新模型下部分仍适用（token2 独立密钥域 / 契约包形态），部分被覆盖（平台卫星扩展形态）。

## 一、目的与目标

确立认证中心（联邦枢纽）的**模块化落地形态**：认证中心联邦层拆分为独立扩展 `TKWF.Ext.SSO`，**组合式消费**认证内核 `TKWF.Ext.Authentication`（经 `TKWF.Ext.Authentication.Abstractions` 契约包，ADR48 D7 L2 门控合规）；外部认证平台（IdP）经**卫星适配扩展** `TKWF.Ext.SSO.{平台}` 接入；术语统一采用国外联邦词汇（Federation / Federated Identity / IdP / SP / Federated SSO）。读者应在 3 句话内明白：SSO = 联邦层独立模块（应用注册 / 授权码 / scope / token2 契约 / JWKS / profile API），经 **Abstractions 契约包**依赖认证中心（不复用其令牌签发——token2 ES256 独立密钥域）；平台适配 = 每外部 IdP 一个卫星扩展 `TKWF.Ext.SSO.{平台}`（微信/QQ/Google/Apple/微软）；本决策与既有 ADR「认证中心命名与边界」选项 C（认证面 + 适配层拆包被否决）**不冲突**——SSO 是**上层新增能力**而非把认证中心切两半。

## 二、问题

### 问题现象

- 认证中心设计文档（`_TCloud` 协作记录 20261005-01）描述的 SSO 能力（`/sso/login`、`/sso/issue`、accesscode、token2、应用注册、scope、profile API、JWKS）**大多数是既有 `TKWF.Ext.Authentication`（V0.5.3）尚未具备的**——它是「联邦枢纽」层（跨应用共享登录态），比既有「认证内核 + 单应用登录」高一层。
- 既有 ADR（认证中心命名与边界）将认证中心裁定为**单包**（选项 A），未预留给联邦层独立装配路径；「包体较大」风险已登记为缺点但未触发拆包。
- `ProviderAuthenticateContext` 为**扁平 record**（Phone/Code/WechatCode/WechatScope/Password/DeviceInfo 显式枚举）——每加渠道需改签名（破坏性变更）；`AuthTypes` 为硬编码常量类——均非可扩展渠道模型。

### 触发场景

- **已有认证体系的消费方**只想要多应用免登：已用 `TKWF.Ext.Identity` 或自建账号+密码，不想引入认证中心 AuthAccount/短信/微信 Provider 全量——只要「跨应用共享登录态」。
- **V5 对外开放（主要客户 = 国外中小企业）**：普遍 BYO IdP（Google Workspace / Microsoft 365 即其企业账号）——需要「SSO + Google/Apple/微软适配器」让员工用企业账号登所有应用，非短信/微信路径。
- **我方多认证中心实例互通**（用户裁定 2026-10-05）：实例间互认 = 按外部联邦处理——独立 SSO 承载标准 token2 消费契约，比绑死在某个认证中心内更干净。

### 现有方案不足

- **SSO 内置认证中心包**：消费方被迫引全量（账号/短信/微信/登录保护），纯外部联邦场景无法独立装配——与「独立需求 → 独立立项」的仓库决策模式（先例：UserCenter 拆分）相悖。
- **无独立 SSO 模块**：V5 国外客户（BYO IdP）无装配路径；已有认证体系的消费方被强制捆绑认证中心全量。

## 三、使用场景

### 适用场景

| 消费方装配 | 获得能力 | 适用 |
|---|---|---|
| 只引 `TKWF.Ext.Authentication` | 内部认证（单应用登录 + 短信/微信 Provider） | 单应用、最小化 |
| Authentication + SSO | **认证中心实例** = 内部认证 + 多应用联邦 SSO（设计文档 20261005-01 完整场景） | 乐学/乐教、DMP 多商户 |
| SSO + 平台适配扩展 | 纯外部联邦登录（BYO IdP，不启用认证中心账号体系）——经 **`Authentication.Abstractions` 契约包**消费账号查询/映射，**不传递引 Authentication 全量** | **V5 国外客户主力形态** |

### 不适用边界

- SSO **不承载认证内核**（令牌签发/账号/Provider/登录保护归 `TKWF.Ext.Authentication`）；SSO 经 Abstractions 契约包**只读消费**账号查询/映射，不触发认证中心主包接线。
- 平台适配扩展**不参与 token2 签发**（只做渠道/身份获取）——不持有 SSO 签名密钥。
- 我方多实例互通**默认不做**（iss 域隔离）；需要时按外部联邦处理（标准 token2 消费路径，无需特殊模块）。
- 既有 `IOAuthTicketService`（单应用票据）**不迁移**——保留在 Authentication 服务 V0.5.x 消费方；SSO accesscode 为联邦授权码，独立实现。

## 四、选项

### 选项 A：SSO 独立模块（`TKWF.Ext.SSO`），组合式依赖认证中心（选定）

- 描述：`TKWF.Ext.SSO` 独立扩展，经 **`TKWF.Ext.Authentication.Abstractions` 契约包**（而非主包）消费认证内核的账号查询/映射契约（ADR48 D7 L2 门控合规，避免 NuGet 传递引全量）；承载联邦层能力（应用注册/授权码/scope/token2 契约/JWKS/profile API）；token2 用 **ES256 独立密钥域**（不复用既有 RS256 TokenService 签发路径）。平台适配 = 卫星扩展 `TKWF.Ext.SSO.{平台}`（`ProjectReference` SSO + 平台适配所属的 IdP 契约）。
- 优点：消费方按需装配（组合矩阵三形态，第三形态零认证中心全量传递）；职责清晰（认证 vs 会话共享）；与 V5 BYO IdP 战略一致；层级拆包有先例（`Notifications.SignalR → Notifications`）+ 契约包先例（UserCenter.Abstractions）；与 UserCenter 拆分同型（独立需求 → 独立立项）。
- 缺点：需新建 `Authentication.Abstractions` 契约包（SSO 消费面抽契约，一次性成本）；新增 SSO 包 + ADR/文档；token2 独立密钥域 = 两套密钥运维。

### 选项 B：SSO 内置为认证中心一部分

- 描述：SSO 能力并入 `TKWF.Ext.Authentication`（V0.6.0+），消费方引一包全得。
- 优点：简单；消费方零选择成本。
- 缺点：包体继续增大（既有 ADR 已登记风险）；纯外部联邦场景被迫引全量；与「独立需求 → 独立立项」模式相悖；V5 国外客户无轻量装配路径。

### 选项 C：SSO 完全独立（不依赖认证中心）

- 描述：`TKWF.Ext.SSO` 自含最小身份映射（uid 表），不消费认证中心令牌/账号内核。
- 优点：最小依赖；纯外部场景最轻。
- 缺点：重复建设令牌/密钥/账号设施（认证中心已有且冻结契约）；两套 uid/密钥体系分裂风险；与「组合式复用」原则相悖。

## 五、决策

选定：**选项 A——`TKWF.Ext.SSO` 独立模块，经 `Authentication.Abstractions` 契约包组合式依赖 `TKWF.Ext.Authentication`**。

> **Oracle 评审（bg_2ab92af3，2026-10-05）PASS WITH CONDITIONS**——P1 三项（§五.1 契约包形态 / §五.3 联盟锚点数据模型 / §五.5 UnionId 语义）已按定稿版意见落实；P2 五项标注为开发方案期细化，不阻塞立项。

1. **模块形态与依赖方向（P1-3 落实：契约包形态修正）**：
   - `TKWF.Ext.SSO`（联邦层）+ `TKWF.Ext.SSO.{平台}`（卫星适配扩展，微信/QQ/Google/Apple/微软按需引入）。
   - **依赖方向**：`SSO → ProjectReference TKWF.Ext.Authentication.Abstractions`（契约包，ADR48 D7 L2 门控合规——SSO 与 Authentication 是**平级跨扩展依赖**，非卫星关系）；`平台适配 → ProjectReference SSO`；Authentication **不依赖** SSO（单向无循环）。
   - **为何拆 Abstractions 而非直接引主包（Oracle P1-3）**：① SSO 消费面不小（`IAuthAccountQueryService`/`IPlatformAccountMapService`/`IUserProfileSource` 等核心契约）；② NuGet 传递依赖——SSO 引主包 → 消费方引 SSO 即传递引 Authentication 全量（短信/微信 Provider + 登录保护 + 8 实体），**「组合矩阵第三形态」（V5 国外客户 BYO IdP）无法成立**；③ 违反 ADR48 D7 L2 门控（TKWF0022 Error）。拆包与 UserCenter.Abstractions 先例一致。
   - **`Authentication.Abstractions` 拆包内容（开发方案期细化）**：SSO 真正消费的契约子集——`IAuthAccountQueryService`、`IPlatformAccountMapService`、`IUserProfileSource` 及 `AuthAccountEntity`/`PlatformAccountMapEntity` 的 SSO 消费面 DTO；**令牌签发/Provider/登录保护不进 Abstractions**（SSO 不复用）。
2. **术语统一（用户裁定 2026-10-05）**：Federation / Federated Identity / IdP / SP(RP) / Federated SSO / Federation Hub——文档、接口、README 一律英文术语为准，中文仅注释（国内无统一称谓，且 V5 面向国外中小企业）。既有 `IOAuthTicketService` 等语义命名**保留**（不重命名，可选优化 3 已覆盖）。
3. **契约归属裁定（P1/P2 落实）**：
   - **3.1 SSO 是否拆 `Authentication.Abstractions`（P1-3）**：**立即拆**——见 §五.1。YAGNI 判断（原草案"消费面小先引主包"）在 NuGet 传递依赖场景下**不成立**。
   - **3.2 token2 密钥域（P2-5 表述修正）**：**独立密钥域**——SSO 实例 = 一个 iss 域，密钥归 SSO 管理；Authentication 密钥继续服务内部 token。**此即新建独立签发路径，不修改既有 token1 契约**（原草案"动既有密钥契约"表述修正——实际上不动既有契约）。理由：ES256 vs RS256 算法不同（ECDSA P-256 vs RSA）、token2 语义不同（sub=uid/aud=target_app_id/TTL 300s/离线验签+JWKS vs token1 的 Bearer 中间件）、iss 不同、消费方不同（外部 H5 应用 vs 业务系统）。
   - **3.3 既有 `IOAuthTicketService` 归属（P2-1 落实：并存路径）**：**SSO 自建 accesscode，既有 `IOAuthTicketService` 保留在 Authentication**（V0.5.x 消费方兼容，语义 = 单应用票据）。**不升级不迁移**——既有是单应用登录流（login/bind + PKCE + TTL 5min），SSO accesscode 是联邦授权码（channel_id + target_app_id + scope + TTL + CAS + SHA256 存储），语义不同。**accesscode 保留 PKCE**（既有已有，defense in depth，OAuth 2.1 BCP）。
   - **3.4 与 ADR 选项 C 边界（P2-4 落实：对比表）**——既有 ADR「认证中心命名与边界」选项 C 否决理由逐条对比：

| 选项 C 否决理由 | SSO 拆分情况 |
|---|---|
| 共享同一令牌契约 | **不共享**——token2 ES256 vs token1 RS256，独立密钥域（§五.3.2） |
| 共享密钥体系 | **不共享**——独立密钥域（新建，不动既有） |
| 适配层很小单独成包收益低 | SSO 是**上层独立能力**（应用注册/scope/profile/JWKS），非适配层 |
| 拆开增加契约包依赖复杂度 | 拆 SSO 反而**减少 V5 消费方负担**（经 Abstractions 引契约，零认证中心全量传递） |

4. **平台适配扩展形态（P2-3 落实：命名与边界）**：
   - **命名 `TKWF.Ext.SSO.{平台}`**（非 `Authentication.{平台}`——平台适配是 SSO 的卫星，依赖 SSO 而非 Authentication，命名反映归属）。
   - 对齐 `Notifications.SignalR`：独立 `[TKWFExtension]` Initializer + 消费方双白名单声明、无初始化顺序依赖（`TryAddEnumerable` 追加，未注册通道自然跳过）、独立 `MinVerTagPrefix`（`SSO.WeChat/v` 等）、文档并入主扩展指南。
   - **与既有 Provider 矩阵边界（Oracle P2-3）**：既有 `IAuthenticationProvider`（Sms/WeChat）= 认证中心**内部认证方式**（服务 token1 签发）；平台适配扩展 = **联邦 IdP 适配**（Google/Apple/微软等外部身份源，服务 token2 联邦流）。微信场景双轨并存（内部 Provider + 联邦适配器）的边界在开发方案期显式声明，**避免重复建设**。
5. **联盟锚点数据模型（P1-1 + P1-2 落实——方案 A，Oracle 推荐）**：
   - **采用方案 A**：联盟锚点（自建 unionid）openid 一对一落 AuthAccount **新列 `FederationAnchorOpenId`**（**保留 `UnionId` 列原语义 = 微信开放平台 unionid**——UserCenter 承接 ADR §五.4 微信绑定推导依赖它，不可重定义，P1-2）；商户 openid2 一对多落 **`PlatformAccountMapEntity`**（扩展承载 channel_id + openid2 语义，每行一个 `(channel_id, openid2) ↔ uid`——AuthAccount 单行无法承载一对多，P1-1）。
   - **备选（登记不选）**：方案 B 新建 `SsoChannelIdentityEntity` 表（uid ↔ channel_id ↔ external_user_id，每渠道身份一行）——表形态更规整但新增实体；方案 C 复用 `UnionId` 列叠加语义——破坏既有语义，否决。
   - **降级路径（可选优化 1）**：若联盟服务号不可行（如未认证），降级"手机号 OTP 绑定合并"为融合锚点（设计文档 §7 已列手机号为通用锚点）。

## 六、后果

### 正面影响

- 消费方按需装配（组合矩阵三形态），纯外部联邦场景（V5 国外客户）可独立装配。
- 职责清晰：认证中心 = 认证（how），SSO = 跨应用会话共享（across-app）。
- 我方多实例互通获得标准路径（token2 消费契约），无需特殊模块。
- 与 UserCenter 拆分同构，仓库决策模式一致。

### 负面影响 / 风险

- SSO 与认证中心共享令牌契约，边界须持续维护（防止契约漂移）。
- 新增 `Authentication.Abstractions` 契约包 + SSO 包 + 文档/ADR 维护成本；Abstractions 拆包内容需开发方案期核定（避免拆多/拆少）。
- token2 独立密钥域 = 两套密钥运维（SSO ES256 + Authentication RS256 并存）。
- **联盟锚点数据模型扩展迁移成本（Oracle 可选优化 2，登记）**：`PlatformAccountMapEntity` 扩展承载 channel_id + openid2 语义 + `AuthAccount` 新增 `FederationAnchorOpenId` 列——动既有实体表结构，需评估对 V0.5.x 消费方/既有数据的迁移影响（开发方案期细化）。
- `Authentication.Abstractions` 契约拆包若破坏既有 `IAuthAccountQueryService`/`IPlatformAccountMapService` 的 public API 位置，影响 V0.5.x 消费方（开发方案期按非破坏性迁移设计——命名空间不变、接口签名不变）。

### 后续待办

- ✅ **Oracle 评审已完成（bg_2ab92af3，PASS WITH CONDITIONS）**——P1 三项修订已落实本 ADR，P2 五项转开发方案期细化。
- 设计文档 20261005-01 同步 SSO 模块归属（已完成 v3 同步，§4.2 模块化落地 + §7 联盟锚点；待 Oracle 意见二次同步：`FederationAnchorOpenId` 新列 + PlatformAccountMap 扩展 + accesscode TTL 120-180s）。
- SSO 开发方案起草（能力映射、`Authentication.Abstractions` 拆包内容核定、实体/契约设计（含联盟锚点数据模型）、测试宿主）——P1 修订后立项。

## 七、关联文档

- 设计文档：`_TCloud/docs/协作/记录/20261005-01-认证中心设计方案.md`（需求方，将归档；v3 已同步模块化落地 + 联盟锚点）。
- 既有 ADR：`ADR-Authentication-认证中心命名与边界.md`（选项 C 否决先例 + 单包裁定）、`ADR-Authentication-令牌契约与密钥管理.md`（令牌契约冻结）、`ADR-Authentication-UserCenter契约承接.md`（独立需求 → 独立立项先例 + ADR48 D7 新形态）。
- 先例：`Notifications + Notifications.SignalR`（卫星扩展 ProjectReference 主包）；`UserCenter.Abstractions`（契约包拆分）。
- Oracle 评审：bg_2ab92af3（PASS WITH CONDITIONS，2026-10-05）。

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-05 | 初始草案——SSO 独立模块立项（选项 A，组合式依赖认证中心）+ 术语统一英文联邦词汇 + 契约归属 4 项待评审裁定 + 平台适配卫星扩展形态 + 联盟锚点复用 AuthAccount 零新列 |
| 2026-10-05 | **Oracle 评审 PASS WITH CONDITIONS 修订（P1 三项 + P2 五项落实）**：① P1-3 §五.1 契约包形态修正——**立即拆 `TKWF.Ext.Authentication.Abstractions`**（SSO 经契约包消费，不引主包；NuGet 传递依赖下 YAGNI 不成立，第三形态成立前提）；② P1-1 §五.5 联盟锚点数据模型重做——**方案 A（Oracle 推荐）**：`AuthAccount` 新列 `FederationAnchorOpenId` + `PlatformAccountMapEntity` 扩展承载 channel_id+openid2（一对多），否决零新列；③ P1-2 §五.5 **UnionId 列语义保护**——保留微信开放平台 unionid 原语义（UserCenter 推导依赖），不可重定义；④ P2-1 §五.3.3 **并存路径**——SSO 自建 accesscode（+PKCE），既有 `IOAuthTicketService` 保留；⑤ P2-2 accesscode TTL 120-180s（开发方案期）；⑥ P2-3 §五.4 平台适配命名 **`TKWF.Ext.SSO.{平台}`** + 与既有 Provider 矩阵边界；⑦ P2-4 §五.3.4 与选项 C 逐条对比表；⑧ P2-5 §五.3.2 表述修正（独立密钥域 = 新建，不动既有契约）；⑨ 可选优化 1-2 登记（联盟锚点降级路径 / §六 迁移成本风险） |
