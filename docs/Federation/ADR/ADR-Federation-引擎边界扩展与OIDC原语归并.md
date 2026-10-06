# ADR-Federation-引擎边界扩展与OIDC原语归并

> **状态**: 活跃 ｜ **日期**: 2026-10-06 ｜ **关联方案**: `docs/Federation/OIDC原语归并Utility引擎-开发方案.md`（Oracle PASS WITH CONDITIONS）｜ **关联裁定**: 转达 `转达-Utility.OAuthClient引擎归属-请框架组裁定.md`（§七"四职责不扩"）+ 主框架 `v4.10.60-OAuthClient引擎-开发方案.md`（三处 YAGNI）

---

## 一、目的与目标

**OIDC Client 协议原语归并主框架 `TKW.Framework.Utility.OAuthClient` 引擎**——M1 OIDC 基座（`TKWF.Federation.Oidc`）内联三核心（`OidcAuthFlow`/`JwksManager`/`OidcIdTokenValidator`）中：authorize 构造/code 交换/userinfo **委托引擎**（删内联重复）；JWKS 拉取/kid 多密钥路由/discovery/验签增强 **上移引擎**（引擎侧变更）；扩展侧仅保留通道装配层（`OidcChannelBase` 等）——达成 ADR52"算法归 Utility、装配归扩展"边界。读者 3 句话内明白目标状态：扩展侧 OIDC 原语归零，引擎成为 OIDC Client 协议原语单一事实源，任何新 OIDC 平台接入复用引擎而非复制。

## 二、问题

**解决什么问题？**

- **现象**：M1 OIDC 基座（V0.1.0，2026-10-06 落地）三核心文件内联实现 OIDC Client 协议原语（`OidcAuthFlow.cs` 273 行 / `JwksManager.cs` 161 行 / `OidcIdTokenValidator.cs` 209 行）——但主框架 `TKW.Framework.Utility.OAuthClient` 引擎（v4.10.60，2026-10-05 落地）已提供 authorize 构造/code 交换/userinfo/基础验签。两者**职责重叠**：M1 内联原语无跨库复用价值（任何新 OIDC 平台接入都需复制），与 ADR52"算法归 Utility"背道而驰。M1 方案 Oracle P1-3 已裁定"引擎可用则委托、否则内联标注待迁移"——现状正处"待迁移"态。
- **触发场景**：新 OIDC 平台接入（M2 Google/Microsoft 派生库 + 未来 LinkedIn/Slack/Apple）复用协议原语时；引擎验签能力缺口（单 PEM 无 JWKS kid 路由）暴露时。
- **现有方案的不足**：
  1. **引擎三处 YAGNI 排除关键能力**：JWKS 拉取（README §五）/ OIDC discovery（方案 Q3）/ kid 路由（经 JWKS 排除隐式）——恰是 M1 `JwksManager`/`OidcIdTokenValidator` 的既有增量；归并要求引擎侧变更（推翻 YAGNI）。
  2. **转达 §七"四职责不扩"裁定冲突**：引擎归属裁定（2026-10-05）明示"实施边界：authorize 构造 / code 交换 / 令牌解析 / 验签调用四职责不扩"——JWKS/discovery 不在四职责内（**真边界扩展**需框架组重批）；kid/iss/azp/leeway 属"验签调用"职责内部（**职责增强**——引擎 v4.10.60 方案 §6.3 是"已考虑并排除"，非"未含"）。
  3. **引擎 API 已发布**（v4.10.60，23 测试全绿）——验签增强若破坏性改 `IdTokenValidationParameters`（`Issuer` 单值 → `TokenIssuers` 列表）将影响既有消费方，须向后兼容。
  4. **ADR102 文档/代码不一致**：ADR102（提议态）声称引擎支持 client_credentials，但引擎 `grant_type` 硬编码 `authorization_code`（`OAuthClient.cs:280`）。

## 三、使用场景

**用于什么场景？**

- ✅ **OIDC Client 平台接入**（Google/Microsoft/LinkedIn/Slack/自托管 Keycloak/Okta/Auth0）——引擎统一提供 authorize/code 交换/userinfo/JWKS 验签，平台库只写通道差异（Defaults/MergeConfig/BuildChannelId）；
- ✅ **JWKS 验签需求**（kid 多密钥路由 + 轮换重取 + L1 缓存）——Google/Microsoft 等标准 OIDC IdP 通用；
- ✅ **iss 通配/正则白名单**（Microsoft common tenant iss 含实际租户 GUID）+ **aud 数组/azp 多方受众** + **exp/iat/nbf leeway**——标准 OIDC 验签增强；
- ✅ **标准 OAuthServer（OP）立项后**（关联转达）——JwksManager 复用为 JWKS 分发侧 + 引擎对称原语（Pkce/StateGenerator）复用；
- ⛔ **不适用边界**：非 OIDC 自有协议平台（微信/钉钉/企业微信——`WeChatOauthChannel`/`DingTalkOauthChannel`/`WeComOauthChannel` 不走引擎）；通道装配层（`OidcChannelBase` 继承 `DomainServiceBase, ISsoChannel`——DI/领域依赖，ADR52 排除项留扩展）；private_key_jwt 低频企业证书（YAGNI 倾向扩展侧保留内联）。

---

## 决策内容（用户裁定 2026-10-06）

1. **归并**：OIDC Client 协议原语归并 Utility.OAuthClient 引擎（委托 + 上移），扩展侧保留通道装配层；
2. **引擎侧变更转达框架组**：A 类边界扩展（JWKS/discovery——重批推翻 YAGNI）+ B 类职责增强（kid/iss/azp/leeway——既有验签职责内部）——API 向后兼容方案 A 优先（新增可选字段 null 回退，`ValidateIdTokenAsync` + `JwksUri` 分支）；
3. **跨仓库时序零阻塞**：扩展侧先委托引擎可用部分（authorize/code/userinfo），验签链路过渡期保留内联；
4. **private_key_jwt 保留内联**（过渡期 + 终态，除非引擎扩展 scheme）；
5. **OAuthServer 转达框架组前瞻评估**（不等待触发条件——用户裁定"框架需前瞻考虑"）。

**否决/降级路径**：框架组否决 A 类边界扩展 → 扩展侧保留 JwksManager 内联（现状维持，仅委托 authorize/code/userinfo——F1 部分达成，本 ADR 标注"部分达成"状态）。

---

<!-- EOF -->
