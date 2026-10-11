# ADR-Federation-子应用委托授权URL构造契约化

> **日期**: 2026-10-11｜**状态**: 已批准（Oracle 评审 PASS WITH CONDITIONS 条件 2）｜**关联**: 开发方案 `docs/Federation/子应用消费方接入-开发方案.md`（决策 2）

---

## 一、目的与目标

将"外部 IdP OAuth 授权 URL 构造"能力从装配层/平台库自由形态**收敛为 `ISsoChannel` 契约可选成员**（`BuildAuthorizeUrlAsync`，DIM 默认 null），使 Federation `authorize/start` 端点能以统一协议为多子应用构造授权 URL。目标状态：授权 URL 构造**协议单源**（通道实现）、平台协议差异收敛于通道、子应用/装配层零重复实现。

## 二、问题

**问题现象**：子应用消费方（XiaoShuTong/EduPlatform 等"仅共享 Federation"模式）需要外部 IdP 登录，但外部认证的授权 URL 构造现状散落多处、无统一入口：

- 8 平台库中仅 **Alipay**（`AlipayApiClient.BuildAuthorizeUrl` static 内联）与 **Oidc 基座**（`OidcChannelFlow.BuildAuthorizeUrl` 委托引擎）有显式构造逻辑；
- **WeChat/QQ/DingTalk/Google** 显式声明"authorize 构造归装配层"（QQ N3 P1-3 / WeChat / WeCom / Google / DingTalk）——每消费方各自实现，**协议分叉风险**（不同消费方拼 URL 参数不一致、scope/state 语义漂移）；
- **FederationWebExtension 注释明示"契约无 BuildAuthorizeUrl——不臆造契约"**（`FederationWebExtension.cs:43`），但该决策在"通知多方适配"的标准化需求下**不再成立**——多子应用统一协议的 `authorize/start` 端点必须依赖通道实现提供 URL。

**触发场景**：DMP-Lite 三站合并 + XiaoShuTong/EduPlatform 子应用接入（2026-10-11 设计讨论）——authorize/start 端点设计时发现契约缺口。

**现有方案不足**：①"装配层各自构造"= 每消费方重复平台协议细节（appid 参数名/scope 形态/state 约定），分叉风险 + 凭证泄露面（装配层须持有 channel 配置才能拼 URL）；②"端点内按 PlatformType 分发"= 装配层写死平台分支，违背"协议差异收敛于 channel 实现"预留原则；③"消费方配置 URL 模板"= 脆弱不安全（URL 含签名/参数，模板化不现实）。

## 三、使用场景

- **Federation `POST /sso/authorize/start` 端点**（本方案新增）：子应用发起外部认证 → 通道 `BuildAuthorizeUrlAsync` 返回授权 URL（含 state 票据）→ 前端跳转 IdP。
- **多子应用统一协议**：XiaoShuTong/EduPlatform 各自本地账户体系 + 共享 Platform Federation——授权 URL 构造语义（redirect_uri 回调落点/state 承载/scope 默认）须跨子应用一致，收敛于通道实现保证。
- **平台库实现**：Alipay（复用 `BuildAuthorizeUrl` static→实例）、OIDC 基座（一次实现覆盖 Google/Microsoft）、WeChat（复用引擎 OAuthClient 微信方言）、QQ/DingTalk/WeCom（新写，WeCom 双流合并）。

**不适用边界**：
- **非 OAuth 认证**（如设备令牌/扫码 webview 特殊流程）不强制——DIM 默认 null = 通道不支持，端点返回 `AUTHORIZE_NOT_SUPPORTED`；
- **消费方自有 authorize 逻辑**（历史装配层实现）不强制迁移——契约提供标准入口，既有装配层实现可继续（互不冲突）；
- **事件通道**（`wechat_event` 等）无需授权 URL——不实现。

**已废弃决策**：FederationWebExtension.cs:43"不臆造契约，授权链接构造归平台库/消费方"（N3 P1-3 等装配层委托声明）——本 ADR 生效后被有意推翻（DIM 默认 null 保持向后兼容，历史装配层实现零破坏）。

---

## 变更记录

| 日期 | 内容 |
|------|------|
| 2026-10-11 | 初始裁定（Oracle 评审 PASS WITH CONDITIONS 条件 2）——`ISsoChannel` 增 `BuildAuthorizeUrlAsync`（DIM 默认 null）+ `SsoChannelAuthorizeContext/Result` |

<!-- EOF -->
