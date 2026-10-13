# ADR-Federation-外部身份联邦与密封票据适配

> **版本**：v0.1（草案，随开发方案评审）
> **状态**：草案（Oracle 两轮评审 APPROVE WITH CONDITIONS——D1-D5 + E1-E3；本 ADR 记 E1-E3 扩展级决策）
> **关联**：框架 ADR107（外部身份联邦与会话身份绑定守卫——D1-D5 Core 决策，本 ADR 上游）；开发方案 `docs/Federation/外部身份联邦与会话绑定守卫-开发方案.md`（v0.2）；联邦互联指南 `docs/Federation/联邦互联-使用指南.md`；信任中心指南 `docs/TrustCenter/信任中心-使用指南.md`
> **日期**：2026-10-13

---

## 目的与目标

Federation/AuthCenter/TrustCenter 三扩展在框架 ADR107 之上提供**事件推送→密封票据→会话建立**的扩展适配层。目标状态：① AuthCenter override `OnLoginByFederatedAsync`（FederatedIdentity→AuthAccount→TUserInfo 映射）；② TrustCenter `AccessCodeService` 适配 `ISealedTicketService<FederatedIdentity>`（DB 后端，既有 API 保留）；③ Federation 事件链（平台化回复 + EventKind 提取 + handler 委托 + 密封票据签发）。读者 3 句话内明白：E1-E3 已落地、既有 API 零破坏、消费方单操作接入。

## 问题

### 问题现象

1. **AuthCenter 无 Federated 钩子 override**：`AuthenticationUserHelperBase` 预实现 4 钩子（SmsCode/WeChatScan/WeChatApplet/OAuth——OAuth 走 `RestoreFromTokenAsync` 验 AuthCenter JWT），但无"外部担保直绑"路径——`FederatedIdentity→AuthAccount→TUserInfo` 映射缺失。
2. **TrustCenter AccessCode 未实现统一票据契约**：`AccessCodeService` 已实现 `IAccessCodeService`（DB+SHA256+AES-GCM+Claimant CAS），但未实现框架 `ISealedTicketService<FederatedIdentity>`——消费方仍散调 `IssueAsync(payload, ttl, claimant)` 而非统一原语。
3. **Federation 事件链不完整**（初版 Oracle F4/F5/F7/F8 保留项）：`WeChatEventChannel` 只提取 openid 不解析 MsgType/Event（EventKind 无来源）；`HandleEvent` 返回 JSON 非微信 XML（微信事件推送要求 XML 回复，JSON 致"服务暂不可用"）；事件编排 uid 归一化未限定只读；`IEventReplyHandler` 多 handler 语义未定。
4. **消费方接入散裂**：落地页需手动串联 TrustCenter Redeem + AuthCenter 会话 + DomainUser 绑定三步（TOCTOU 窗口）。

### 触发场景

- **微信未认证号事件推送**：用户发消息/点菜单 → 微信 POST XML → Federation event 端点 → 验签+取 openid → handler 签密封票据 → 图文链接带 ticket → 用户点击 → `LoginAsFederatedAsync` 建 session
- **企微事件 / 支付宝通知**：同一 `IEventReplyHandler` 契约 + 同一 `FederatedIdentity` 票据（Phase 1 覆盖企微；支付宝 event 通道 Phase 2 收编）

### 现有方案的不足

- **"Federation 不提供事件业务委托"**（现状边界）→ 消费方必须自建微信回调端点、自己拼 XML（验证 `WeChatEventCrypto` 是 internal，消费方无法直接调）——适配负担扩散业务领域，违背 Federation 初衷（用户裁定：由连接层专业处理各平台，业务领域零平台知识）
- **"用 `identity/claim` 端点"**（子应用委托）→ 要求 SsoClient 注册 + client_secret，不适配浏览器落地页

## 使用场景

### 适用场景

- **事件推送 → 图文链接票据 → 落地建 session**（微信未认证号/企微/未来消息平台）
- **认证服务号静默授权 → 外部认证建 session**（现 `ExternalIdpLoginService` 链，Phase 1 可并入 Federated 钩子）
- **既有 AuthCenter/TrustCenter 消费方**——零破坏（`IAccessCodeService` 保留、OAuth 钩子保留）

### 不适用边界

- **单应用内部密码/短信登录**——不涉外部身份
- **需 PKCE 的授权码流**（OAuthTicket/AccessCode 既有语义）——Phase 2 经 SealedTicketOptions.Extensions 扩展
- **支付宝 event 通道**——Phase 2 收编 `AlipaySignService`（验签逻辑已有，非 Phase 1）

---

## 决策（E1-E3）

> 详细设计见开发方案 v0.2 §五。本 ADR 记扩展级决策要点。

### E1：AuthCenter override `OnLoginByFederatedAsync`

- `AuthenticationUserHelperBase<TUserInfo>` override 框架虚钩子：`FederatedIdentity → IAuthAccountQueryService.GetByUIdAsync → IsEnabled/IsFrozen 检查 → IAuthorizationMapper → CreateUserInfoFromAccount`
- 注册：消费方 UserHelper 继承 `AuthenticationUserHelperBase`（既有 `OnRegisterDomainServices` 单接线点），override 钩子即可

### E2：TrustCenter `AccessCodeService` 适配 `ISealedTicketService<FederatedIdentity>`

- 同一实现类实现两接口（`IAccessCodeService` 保留 + `ISealedTicketService<FederatedIdentity>` 附加契约）
- 映射：`FederatedIdentity` → JSON → 既有 `IssueAsync(payloadJson, ttl, claimant)`（内部 SHA256+AES-GCM 落库）；`RedeemAsync` → 解密 → `Deserialize<FederatedIdentity>`
- PKCE 字段：FederatedIdentity 无对应——null/丢弃（Phase 2 经 Extensions）

### E3：Federation 事件链

- `WeChatEventChannel` 提取 MsgType/Event → `SsoChannelAuthContext.Parameters["msg_type"]`/`["event"]`（评审 F4）
- `HandleEvent` 平台化回复：`EventReplyResult(Content, ContentType)`——微信 XML（text/xml）/ 空串 200 + 异步客服消息（规避 5 秒超时）（评审 F5）
- 事件编排 uid **只读**查询（`GetByChannelAsync` 不建号——评审 F7）；`IEventReplyHandler` first-non-null-wins（评审 F8）
- handler 经 `ISealedTicketService<FederatedIdentity>` 签票据 → 图文链接带 ticket

---

## 破坏性变更

| # | 变更 | 级别 | 消费方适配 |
|---|------|------|-----------|
| 1 | AuthCenter 增 `OnLoginByFederatedAsync` override | 非破坏（新增钩子 override） | 无 |
| 2 | TrustCenter `AccessCodeService` 实现附加接口 | 非破坏（新增契约） | 无（`IAccessCodeService` 保留） |
| 3 | Federation event 端点响应形态（JSON→平台化回复） | 破坏性（行为——event 端点消费方） | 事件端点消费方改处理 `EventReplyResult`（微信 XML） |
| 4 | `WeChatEventChannel` 增 Parameters 键 | 非破坏（新增键） | 无 |

<!-- EOF -->
