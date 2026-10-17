# ADR-Federation-事件回复handler基类与内置测试设施

> **版本**：v0.1（随开发方案评审——Oracle APPROVE WITH CONDITIONS 后实施落地）
> **状态**：⚠️ **已废弃**（2026-10-17）——被 `ADR-Federation-平台特定事件回复Handler基类与二级分发.md` 演进替换。原决策 `EventReplyHandlerBase`（保留，平台无关签票基类）与 `TestEventReplyHandler`（已删除，能力迁平台层 Template/Probe）的接受与推翻记录见新 ADR。本文件永久保留（AGENTS §4 生命周期）。
> **关联**：开发方案 `docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md`（Oracle APPROVE WITH CONDITIONS——2 P0 + 2 P1 + 3 P2 全部吸收）；ADR-Federation-外部身份联邦与密封票据适配（E3 事件回复契约）；TrustCenter.Abstractions（`ISsoChannel`/`IAccessCodeService`）；联邦互联指南 `docs/Federation/联邦互联-使用指南.md` §5.1
> **日期**：2026-10-15

---

## 目的与目标

为 Federation **事件回复链路**（外部身份联邦 E3——微信未认证号事件推送 → handler 签 accesscode → 图文回复 → 用户点击兑换）补齐**业务 handler 开发高内聚设施**与**开发期测试能力**。目标状态：① `EventReplyHandlerBase` 高内聚基类——模板方法 `HandleAsync` 内置"签 accesscode → 构造图文链接"，子类只实现 `BuildReplyAsync` 业务规则；② `TestEventReplyHandler` 内置测试 handler——默认关闭，开启即完成"事件推送 → 图文回复 → 点击 → 测试端点兑换显示载荷"全链路闭环；③ 测试设施（测试 handler + 测试端点）默认关闭 fail-safe、只配合使用、接入业务 handler 后整体下线。读者 3 句话内明白：基类已内置签票→图文样板、测试 handler 零配置验证微信链路、测试设施默认关且"换 handler 即关"。

## 问题

### 问题现象

1. **开发期微信接入无内置验证手段**：`IEventReplyHandler` 是裸接口（`HandleAsync` 单方法），Federation 默认**不注册任何 handler**（`FederationExtensionInitializer` 注释明示"事件回复是业务决策，归消费方"）——消费方要验证微信事件链路，必须先写一个业务 handler 才能测通，无法先验证"微信→Federation→回复"基础连通性（鸡生蛋）。
2. **业务 handler 样板代码多**：签 accesscode（`IAccessCodeService.IssueAsync`）+ 构造图文链接（`EventReplyNewsArticle` + URL 拼接）+ 组装 `EventReplyResult`——每个业务 handler 重复同一套逻辑，易错且难维护（核查实证：`_Framework` 全仓搜 `EventReplyHandlerBase`/`AccessCode Helper`/`BuildNews` 零命中）。
3. **测试端点裸暴露风险**：`TestAccessCodeEndpointEnabled`（默认关）是独立开关，无"与测试 handler 生命周期绑定"语义——若消费方忘关端点而接了业务 handler，测试端点仍在生产暴露。

### 触发场景

- 新项目接入微信（未认证号）事件链路——需先验证"微信后台事件推送 → Federation 验签解密 → 回复"连通，再开发业务逻辑。
- 消费方开发业务 handler（如"发送图文菜单/欢迎消息"）——需要签 accesscode + 组图文的样板能力。
- 生产环境——必须确认测试 handler 与测试端点不会残留暴露。

### 现有方案的不足

- `IEventReplyHandler` 裸接口 + 消费方自注册：验证链路需先写代码（鸡生蛋）。
- 无任何 handler 基类/扩展方法：accesscode→payload→图文的高内聚封装缺失。
- 测试端点独立开关：无"换 handler 即关"的联动语义（靠文档提醒，易遗漏）。

## 使用场景

### 适用场景

- **开发期微信（未认证号）接入验证**——开两个测试开关 + 配 `TestAccessCodeEndpointBaseUrl` → 微信后台配置事件推送 URL → 发消息触发 → 收到图文链接 → 点击 → 测试端点显示载荷 JSON（openid/uid）——全链路闭环零代码。
- **业务 handler 开发**——继承 `EventReplyHandlerBase`，子类只实现 `BuildReplyAsync`（检查 openid/uid → 返回载荷或 null），签 accesscode→图文回复内置，样板代码收敛到 10 行内。
- **生产 fail-safe**——测试设施两开关默认 false（零暴露）；接入业务 handler 后保持关闭（测试 handler 不再回复、测试端点不再暴露）。

### 不适用边界

- **生产环境**——测试设施仅限开发期；接入业务 handler 必须关闭测试 handler 与测试端点（"换 handler 即关"文档强约束）。
- **OAuth 通道**——测试 handler `ChannelType = "wechat_event"` 只匹配事件通道（微信未认证号），不影响 OAuth 通道。
- **需 ExpectedClaimant 绑定的场景**——测试 handler 签 accesscode 用 `ExpectedClaimant=null`（可转让——由 accesscode 高熵随机码保护）；业务 handler 可按需自设 `ExpectedClaimant` 绑定兑换方。
- **运行时自动联动**——不做"测试端点开关跟随 handler 注册状态"的运行时联动（配置分层原则，见决策 7）。

---

## 决策

> 详细设计见开发方案 §三/§四。本 ADR 记扩展级决策要点。

### 1. 基类 + 模板方法高内聚形态（vs 接口扩展方法）

`EventReplyHandlerBase : DomainServiceBase, IEventReplyHandler`（方案 A 采纳）——模板方法 `HandleAsync` 固定封装"子类业务载荷 → `User.Use<IAccessCodeService>().IssueAsync` 签 accesscode（安全数据投递——载荷 JSON 落库加密、expectedClaimant 进原子 CAS）→ 构造图文（news）链接（URL 指测试端点）→ 组装 `EventReplyResult`"；子类只实现 `BuildReplyAsync`（返回 `EventReplyPayload?`——null = 不处理，first-non-null-wins）。vs 方案 B（仅扩展方法内聚签票→图文，无基类）——每个 handler 仍自实现 `HandleAsync` 样板（ChannelType/签名/返回组装），高内聚度不足。基类继承 `DomainServiceBase`（对齐 ADR90——`IDomainUser` 经基类 `User`，不注册 DI）；`User.Use<IAccessCodeService>()` 帧内懒加载（DI004——禁构造注入）。

### 2. 测试设施默认关闭 fail-safe（P1-1）

`TestEventReplyHandlerEnabled` 默认 **false**——显式开启开发期测试；与 `TestAccessCodeEndpointEnabled` 默认 false 一致。生产默认关闭（零暴露）且不阴影业务 handler（未开开关 = `IEventReplyHandler` 集合不含测试 handler）。

### 3. ExpectedClaimant=null 可转让（P0-1）

测试 handler 签 accesscode 用 `ExpectedClaimant = null`——与测试端点 `RedeemAsync(code, claimant: null)` 匹配（non-null `ExpectedClaimant` 与 null claimant 必致 `CLAIMANT_MISMATCH`）；可转让语义由 accesscode 自身高熵随机码保护（测试场景信任域内）。

### 4. 载荷 = FederatedIdentity（P0-2）

测试 handler 载荷序列化为 `FederatedIdentity`（`UId`/`ChannelId`/`ExternalUserId`/`ChannelType` 从 `context.Parameters` 填充）——与测试端点 `RedeemAsync<FederatedIdentity>` 反序列化属性对齐（匿名对象 `{openId, uid, receivedAt}` 属性不匹配 → 兑换显示空值）。

### 5. BaseUrl 空返 null（P1-2）

`BuildReplyAsync` 内 `TestAccessCodeEndpointBaseUrl` 为空 → 返回 null——不签无意义 accesscode、不回复图文、不拦截业务 handler。fail-safe：配置缺失时测试设施静默退出而非产出死链/空签。

### 6. 配置分层（AGENTS §8）

领域行为开关归**领域节** `TKWF:Federation:EventReply`（`FederationEventReplyOptions`：`TestEventReplyHandlerEnabled` / `TestAccessCodeEndpointBaseUrl` / `RoutePrefix` 对齐——AddOptions 兜底 + Initializer 条件注册 `TryAddEnumerableConstructible<IEventReplyHandler, TestEventReplyHandler>`，ADR92 集合版守卫工厂——ctor `IDomainUser` 帧内供给）；Web 暴露面开关归 **Web 节** `TKWF:Federation:Web:TestAccessCodeEndpointEnabled`（`FederationEndpointOptions` 既有）。两类配置解耦可测。

### 7. "换 handler 即关"文档强约束而非运行时联动

测试 handler 与测试端点**只配合使用**：开发期验证时两开关同时开 true；接入业务 handler 时**两开关都关**（测试 handler 关 = 测试链路不再回复；测试端点关 = 生产无暴露）。**不做运行时自动联动**（如端点开关自动跟随 handler 注册状态）——配置分层原则（AGENTS §8：领域开关归领域节、Web 暴露面开关归 Web 节，POCO 不镜像）要求两类配置解耦，自动联动需跨层读配置、破坏分层；改为**文档级强约束**（使用指南"测试设施生命周期"小节 + 两端开关注释互引）。Oracle 评审确认此取舍合理；可选增强：启动时若 `TestEventReplyHandlerEnabled=true` 且集合 >1 handler → 日志 Warning（不阻塞、不跨层读配置）。

---

## 破坏性变更

| # | 变更 | 级别 | 消费方适配 |
|---|------|------|-----------|
| 1 | 新增 `EventReplyHandlerBase`/`TestEventReplyHandler`/`FederationEventReplyOptions` | 非破坏（纯新增） | 无 |
| 2 | `FederationExtensionInitializer` 条件注册测试 handler（开关默认 false 不注册） | 非破坏（未开开关 = 集合空，既有 `IEventReplyHandler` 集合语义不变） | 无 |
| 3 | 新配置节 `TKWF:Federation:EventReply` | 非破坏（新增节，默认值兜底） | 无 |

<!-- EOF -->
