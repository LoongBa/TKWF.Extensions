# QQ 互联连接器-使用指南

> **库**: `TKWF.Federation.QQ`（QQ 互联连接器——平台网关库/大使馆）| **版本**: V0.1.0 | **框架**: .NET 10
> **定位**: 认证中心**对外（上游）**接 QQ IdP 的出站网关——OAuth **跳转授权**（身份获取，`qq_oauth`）；作为 **RP/OAuth Client** 消费 QQ，把 `(channel_id, openid) → uid` 归一认证中心 Federation token2。**SSO 是认证中心对下游的能力，不是本库的协议**（归层命名说明见 §六）
> **关联文档**: 技术规范见 `_Framework/Federation.QQ/README.md`；N3 开发方案 `docs/Federation/N3-QQ平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS，P1 五项）；N1 平台网关通用模板 `docs/Federation/平台网关模板-开发指南.md`

> ✅ **实施进度（2026-10-06）**：V0.1.0 全部落地（N3 T2-T6）——`QqApiClient` 出站（code→access_token→/me→openid 一次性链 + redirect_uri 一致性 + fmt=json + JSONP 剥壳 + 用户级 token 不缓存）+ `QqOauthChannel`（qq_oauth）+ `AddQqFederationChannels()` 注册方法 + 测试 16 用例全绿。文档即完成态。（注：preview 0.1.1-preview.0.1 为 N1 骨架版，`AuthenticateAsync` 抛 `NotImplementedException`——本 V0.1.0 稳定版已实现。）

### 1. OAuth 网页授权流程（`qq_oauth`——出站-only）

```
用户 在目标 H5 应用 点击"QQ 登录"
  → 应用跳 /sso/login?target_app_id=X
  → Federation 按 channel 路由 qq_oauth → 302 QQ authorize（graph.qq.com/oauth2.0/authorize）
       (client_id + redirect_uri + scope=get_user_info 默认 + state 必填绑定会话防 CSRF + display)
       —— authorize 构造归装配层（Federation/消费方持 HTTP 上下文），本库通道不感知 state（N3 P1-3）
  → QQ 授权页（跳转；无扫码；APP 内嵌 H5 不支持）→ 回调 /sso/oauth/qq/callback?code=&state=
  → 验 state（装配层）→ 回调 context 注入 redirect_uri（与授权时一致，P1-4）
  → QqOauthChannel.AuthenticateAsync：code 缺拒 / redirect_uri 缺拒
       → QqApiClient: code+client_secret+redirect_uri → access_token（fmt=json）→ /me → openid（+unionid 可选）
  → (channel_id=qq, openid) → uid → Federation 签 token2 → 302 注册 origin #t=
```

- **redirect_uri 一致性（N3 P1-4 强制）**：code 换 token 时 redirect_uri 须与授权时**完全一致**（OAuth RFC 6749 §4.1.3——防 code 窃取后换 token）。装配层须在回调时把授权时使用的 redirect_uri 注入 `SsoChannelAuthContext.Parameters["redirect_uri"]`；缺省 → 本通道拒（`QQ_REDIRECT_URI_REQUIRED`），出站不一致 → QQ 服务端拒（error 100030）。
- **用户级 token 不缓存（N3 P1-5）**：QQ token = 用户级 authorization_code grant（非微信应用级 client_credential）——联邦认证面一次性消费，不缓存不续期（refresh_token 续票归 L7 非认证面 YAGNI）。
- **userinfo 降级**：`GetUserInfoAsync`（get_user_info 裁剪 nickname/头像/gender/province/city）失败降级 null——QQ **无手机号无邮箱**（官方明示性别/省市非真实数据），联邦只能**匿名关联**（openid/unionid），账号绑定靠 N2 锚点策略。

### 2. unionid 联盟锚点（可选增强——N3 P1-2）

| 项 | 说明 |
|----|------|
| 默认 | 仅 openid（应用维度唯一——`external_uid` 稳定映射键） |
| `EnableUnionId=true` | `QqApiClient.GetMeAsync` 额外请求 `unionid=1`（需**官网自助申请**：connect.qq.com 应用接口→Unionid 申请，审核须"通过"，≤60 应用硬上限）并返回 unionid |
| 写锚点 | unionid 为**联盟锚点写入辅助**——写 `FederationAnchorOpenId` 归装配层（经 `ISsoAccountLinkService.SetFederationAnchorAsync`，对齐 N2 §3.1 选项 B：锚点列存平台无关自生成值）；未申请权限时 QQ 返 100048 companyid not set → 抛错 |

---

## 三、信任根安全说明（降级——出站换取链）

QQ **无入站回调**（无事件推送）→ 无 P5 验签面（非违规）。信任根 = **出站换取链**：

| 要素 | 说明 |
|------|------|
| **code 一次性** | 10 分钟过期，换取后即废 |
| **state 强校验** | 必填（QQ 官方标注"必须"）——会话绑定防 CSRF，生成 + 校验归**装配层**（库通道不感知 state，P1-3） |
| **client_secret 换取** | 对称密钥服务端持有，永不明文进配置库（生产 AES-GCM 密文或装配注入） |
| **redirect_uri 一致性** | code 换 token 时须与授权时一致（P1-4）——防 code 窃取后换 token |

无可伪造 openid 的入站面——安全重心在"我方向 QQ 发起的授权 + code 一次性"。F2/F3 验收：`redirect_uri 不一致拒`（P1-4）为**装配层集成测试**（库内 `AuthenticateAsync` 做 code/redirect_uri 缺省拒 + API 失败负路径，已落地）。

---

## 四、端点归装配层（库只做出站逻辑类）

**本库不引 AspNetCore（Oracle 评审点 5）**——出站调用经 `QqApiClient`（typed client，`IHttpClientFactory` 生命周期托管），入站端点归 Federation 扩展/消费方**装配层**：

| 端点 | 方法 | 功能 | 归属 |
|------|------|------|------|
| `/sso/login?target_app_id=X` | GET | OAuth 代理入口（按 channel 路由：qq_oauth → 302 QQ authorize——state 生成+会话绑定） | Federation 装配映射 |
| `/sso/oauth/qq/callback` | GET | QQ OAuth 回调（验 state → 注入 redirect_uri → channel.AuthenticateAsync → uid → 签 token2 → 302 `#t=`） | Federation/消费方装配 |

---

## 五、故障排查

| 症状 | 原因 | 处置 |
|------|------|------|
| `QQ 凭证未配置：TKWF:Federation:QQ 节 Channels 为空` | `QqOptions.Channels` 未配置/空列表 | 补 Channels 列表；`[Options]` 自动绑定后重启宿主 |
| `QQ 凭证未配置：appId=xxx（Channels 无匹配项）` | 出站调用传入的 appId 与 Channels 任一项 AppId 不精确匹配 | 核对 AppId 精确值（Ordinal 匹配；QQ AppId 为纯数字） |
| `QQ access_token 获取失败：error=100016...` | AppSecret 错误 / code 过期（10min）或已消费 / code 与 AppId 不绑定 | 核对 AppSecret（生产为 AES-GCM 密文）；code 一次性且 10 分钟内使用；code 须由同一 AppId 授权产生 |
| `QQ access_token 获取失败：error=100030...` | **redirect_uri 不一致**（P1-4——code 换 token 时须与授权时一致） | 核对回调注入的 `redirect_uri` 与授权时构造的完全一致（含 host 大小写/路径/query） |
| `QQ openid 获取失败：error=100048...` | EnableUnionId=true 但**未申请 unionid 权限**（companyid not set） | 官网自助申请 unionid（≤60 应用上限）；未申请则 `EnableUnionId=false` |
| `QQ redirect_uri 必填...` | 回调 context 未注入 `redirect_uri` | 装配层回调时把授权时使用的 redirect_uri 注入 `SsoChannelAuthContext.Parameters["redirect_uri"]` |
| `QQ code 换取身份失败`（非 JSON 响应） | 网络层代理/QQ 侧异常页返回 HTML | 检查出站代理配置；重试一次 |

---

## 六、归层命名说明（SSO → Federation → Federation.QQ）

认证体系四层归层模型（ADR-AuthCenter-归层与命名，Oracle PASS WITH CONDITIONS）：

| 层 | 包 | 形态 | 职责 |
|----|----|------|------|
| 联盟核心/认证中心 | `TKWF.Ext.AuthCenter` | 扩展（对内，持久化+装配） | 账号/令牌/登录保护/票据 |
| 外交部/对外身份接口 | `TKWF.Ext.Federation`（**联邦互联**） | 扩展（配置装配、自含实体） | token2/accesscode/JWKS + `ISsoChannel` 窄适配 |
| 大使馆/平台网关库 | **`TKWF.Federation.QQ`**（本库） | **纯库**（无 Initializer 无 `[TKWFExtension]` 无持久化） | QQ 出站网关（OAuth 跳转授权，无入站回调） |
| 引擎/协议 | `TKWF.Utility.OAuthClient` | 库（BCL，归主框架） | authorize 构造/code 交换/令牌解析 |

**`无 Ext` = 纯库判据**（AGENTS §8）：`TKWF.Ext.*` = 扩展（有装配/持久化）；`TKWF.*`（无 Ext）= 库（纯逻辑/无状态）。QQ 平台网关**无持久化、纯协议逻辑** → 应为库非扩展；`TKWF.Federation.{平台}` 单库多命名空间收敛。本库 `QqApiClient` 零持久化零 Store（tkwf-extension 铁律落地实证——不创建 `IQqStore` 等 DataService 薄包装伪层，出站 API 能力直接内聚于客户端类）。

---

## 七、边界

| 资产 | 归属 | 定位 |
|------|------|------|
| 微信 `WeChatOauthChannel` | `TKWF.Federation.WeChat` | 微信 IdP 适配（双向——OAuth + 事件推送） |
| **QQ `QqOauthChannel`** | **本库** | **QQ IdP 适配（出站-only）**——无事件推送，信任根降级 |

- **集合并列**：QQ 通道与其他平台通道并列进 `IEnumerable<ISsoChannel>`——未装配库 = 空集合"未注册通道自然跳过"（F4 验收）。
- **无内部 Provider**：QQ 仅联邦 IdP 适配（token2 联邦流）；认证中心内部 QQ 认证（token1）非本库范围（QQ 无 PC 扫码/简化认证形态，暂不涉及）。

---

## 八、变更记录

| 日期 | 版本 | 内容 |
|------|------|------|
| 2026-10-06 | V0.1.0 | N3 协议实现落地：`QqApiClient`（code→access_token→/me→openid 链 + redirect_uri 一致性 P1-4 + fmt=json 强制 + JSONP 剥壳兜底 + 用户级 token 不缓存 P1-5 + GetMeAsync unionid 开关 + get_user_info ret/msg 裁剪）+ `QqOauthChannel`（qq_oauth：code 缺拒 / redirect_uri 缺拒 / AuthLevel=2 / 不感知 state P1-3）+ `QqUserInfo`/`QqMeResult` 响应裁剪 + `AddQqFederationChannels()` 注册方法（保留）+ 测试 16 用例全绿（生产路径通道正负 + QqApiClient 直构单测 + 协议结构断言 + unionid 开关 + StripJsonp 剥壳）；信任根降级（出站换取链）+ 故障排查 + 归层命名说明 |

<!-- EOF -->