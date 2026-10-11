# ADR-Federation-对外路由命名空间

> **日期**: 2026-10-11｜**状态**: 已批准（Oracle 评审 PASS WITH CONDITIONS，条件 1-5 全部吸收）｜**关联**: 开发方案 `docs/Federation/对外路由命名空间-开发方案.md` · 多通道联邦方案 `docs/Federation/多通道联邦-开发方案.md`（§3.2 默认通道语义，本 ADR 收紧）· ADR-Federation-子应用委托授权URL构造契约化

---

## 一、目的与目标

Federation 对外路由从 `/sso/*`（体验词，SSO=单点登录）语义化重构为 `/feberation/*`（结构词，对齐中文名「联邦互联」+ 项目惯例拼写 Feberation）——路由读出来即知是"对外联邦互联"。引入**二级平台段**（`{platformId}` = 平台族，开放注册表）使多平台路由一目了然，不依赖 channelId 命名猜平台；同时**收紧默认通道降级语义**（多通道 + 省略通道段 → 硬失败，消除 `GetDefaultAsync` 非确定性静默选区）。目标状态：`/feberation/{platformId}/...`（平台集成面）+ `/feberation/jwks|authorize/*|trust/*|identity/*`（信任/委托面）双面共存；路由即文档（读路径知平台）；event 端点可用（缺陷修复）。

## 二、问题

**问题现象**：

1. **`/sso` 语义模糊**——SSO 是体验词（用户感知的"单点登录"），Federation 是结构词（系统间联邦互联）；`/sso` 无法表达"这是对外连接层"的架构语义，与扩展中文名「联邦互联」不对齐。
2. **路由无平台维度**——`/sso/event/{channelId}`、`/sso/oauth/{channelId}/callback` 从路径看不出平台（"哪个平台的事件"），多平台并存时路由可读性差，须依赖 channelId 命名惯例或查配置。
3. **`HandleEvent` event 通道不可达（实证缺陷）**——`HandleEvent` 硬编码 `CreateAsync(channelId, null)`，工厂 null 分支谓词 `IsOAuthChannel` 只白名单 `*_oauth/*_oidc`（`EndsWith` 显式排除 `*_event`）——`wechat_event`/`dingtalk_event`/`wecom_event` 通道不可达：双注册场景命中错误通道（误导性 400），单注册场景 404。
4. **默认通道降级非确定性（自认不守卫）**——`GetDefaultAsync` 多通道下取 IsDefault → 首项（DI 注册序依赖），使用指南已自认"不承诺多公众号下业务合理默认"——**自认但不守卫 = 静默失效**，违反 TKWF 设计原则"缺省=守卫硬失败不静默失效"。

**触发场景**：2026-10-11 用户裁定评估 Federation 对外路由命名设计建议——"不用理会破坏面，关键是是否最优设计"；"立迭代开发任务，实施迭代"。

**现有方案不足**：`/sso` 前缀已由 `FederationEndpointOptions.RoutePrefix` 承载可配（默认 `/sso`），但平台维度缺失是结构性的（路由模板无平台段）；默认降级语义（候选 A 全保留）在多通道下固化静默选区反模式。

## 三、使用场景

- **多平台 IdP 并存**（微信 + 钉钉 + 企微 + QQ + 支付宝 + OIDC 系同装配）：`/feberation/wechat/event/wx-1001` vs `/feberation/dingtalk/event/ding-2001`——路径即平台，后台配置/故障排查零猜测。
- **平台 IdP 后台回填**（微信服务器配置 URL / OAuth 回调 redirect_uri）：完整 URL 含平台段 + 通道段，逐通道精确定位。
- **单通道部署**（大量存量/测试）：通道段可省略 → 单通道降级（活跃数 ==1 无歧义），零成本。
- **多通道 + 省略通道段**：400 `CHANNEL_REQUIRED` 硬失败——部署配置错误 fail-fast（如微信后台漏填通道段）。
- **子应用消费方委托**（authorize/start · trust/issue · identity/claim）与 **JWKS 分发**：跨平台根级端点，不挂平台段（语义不属任何平台）。
- **OIDC 系**：`oidc`/`google`/`microsoft` 三库 PlatformType 各自独立（`oidc_oidc`/`google_oidc`/`microsoft_oidc`）——平台段无歧义映射；多 Keycloak 实例共享 `oidc` 平台段、经 channelId 区分（`/feberation/oidc/login/{channelId}`）。

**不适用边界**：
- **消费方自定义前缀**（`RoutePrefix` 可配任意值）——语义化默认值不强制，消费方可保留 `/sso` 或自定义。
- **领域门面**（`ISsoLogin`/`ISsoChannelFactory` 等）不感知路由——平台段校验/B 守卫在 Web 层 handler + 领域消费者层，门面零路由耦合。
- **历史方案文档**（docs/SSO/*、多通道联邦方案等）不改——契约变更记录于变更记录，历史文档保持原样。

---

## 变更记录

| 日期 | 内容 |
|------|------|
| 2026-10-11 | 初始裁定（用户裁定"最优设计优先，破坏面不构成约束"）——`/sso` → `/feberation` + 平台段 + B 守卫 + event 缺陷修复；Oracle 评审 PASS WITH CONDITIONS 条件 1-5 全部吸收（条件 1：决策 5 选 B——按活跃通道数守卫、4 处消费者；条件 2：统一 404 防枚举；条件 3：event 推导无需回退兜底；条件 4：无需平台名白名单（文档备注保留字）；条件 5：OIDC Platform 非双源补注 / ExternalIdpAuthenticator+StartAsync 一并守卫 / ChannelAlias 正交补注） |

<!-- EOF -->
