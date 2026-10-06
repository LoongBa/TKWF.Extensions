# N3 QQ 平台网关库 开发方案（TKWF.Federation.QQ）

> **立项来源**: 平台网关立项依据（近中远）N3（P0 近期）｜ **类型**: L1 平台库（国内 C 端）
> **版本**: v0.1.0 ｜ **日期**: 2026-10-05 ｜ **状态**: ✅ **已实施**（N3 T1-T6 全部落地——T2/T3/T5/T6 于 2026-10-06 完成，测试 16 用例全绿 + 全量回归 40 项目 1841 用例零失败）
> **关联**: 国内总规划（QQ 行 + §三模板 + 信任根分档）｜ 检索事实 bg_af8d3de9（QQ 互联官方文档）｜ N1 模板｜ N2 锚点策略
> **关键约束**: 纯库（无 Initializer 无 `[TKWFExtension]` 零持久化零 Store）；出站-only 精简形态（组件 1/2/5/6/7/8）；QQ 无入站回调 → 信任根降级（P5 不适用，出站 code 一次性 + state CSRF + client_secret）

---

## 一、背景与目标

### 现状

QQ 互联（connect.qq.com）是国内 C 端第二大身份源（覆盖微信外 QQ 用户群）——但联邦互联目前仅微信（`wechat_oauth`/`wechat_event`）双通道，QQ 未接入。

### 目标

1. **`TKWF.Federation.QQ` 纯库**——OAuth 通道（`qq_oauth`）：跳转授权 → code → openid/unionid；
2. **出站-only 形态**（QQ 无事件推送）——组件 1/2/5/6/7/8；
3. **信任根降级**——QQ 无回调验签，安全重心 = 出站 code 一次性 + state CSRF + client_secret 换取；
4. 按 N1 模板复制骨架 + N2 锚点策略（QQ 无手机号→匿名关联）。

---

## 二、协议事实（官方文档核实 bg_af8d3de9）

| 项 | 事实 |
|----|------|
| 授权形态 | **跳转**（PC/手机站点共用 `graph.qq.com/oauth2.0/authorize`，`display=mobile` 切样式）；**无扫码**；APP 内嵌 H5 不支持 |
| Authorize 端点 | `https://graph.qq.com/oauth2.0/authorize`（GET）——`response_type=code` + `client_id` + `redirect_uri`（须注册主域名一致 + URLEncode）+ `state`（**必填**，CSRF 防护）+ `scope`（可选，默认 `get_user_info`）+ `display` |
| Token 端点 | `https://graph.qq.com/oauth2.0/token`（GET）——`grant_type=authorization_code` + `client_id` + `client_secret`（对称密钥）+ `code` + `redirect_uri`（一致）+ `fmt=json`；`code` 10min 过期；access_token 60 天 + refresh_token 一次有效续票 3 个月 |
| 用户标识 | `openid`（**应用维度唯一**）经 `/me?access_token=` 获取；`unionid`（**需官网自助申请**：connect.qq.com 应用接口 → Unionid 申请；应用审核须"通过"；≤60 应用硬上限；邮箱渠道 2019-09-02 停止）经 `/me?access_token=&unionid=1` |
| 用户信息 API | `https://graph.qq.com/user/get_user_info`（GET）——`access_token` + `openid` + `oauth_consumer_key`；返回 nickname/figureurl 头像/gender/province/city/year/星座/黄钻——**无手机号无邮箱**（官方明示性别/省市非真实数据） |
| 验签/安全 | **无回调签名校验**（仅 state 防 CSRF）；token 换取需 client_secret（对称） |
| scope | 默认 `get_user_info`；可选按 API 名（list_album/upload_pic/add_t 等）——非 OAuth 标准 scope |

**设计要点（从事实导出）**：
- **出站-only**：QQ 无事件推送 → 组件 3/4 缺省，信任根降级（P5 澄清）；
- **unionid 预申请**：若要联盟锚点跨应用对齐，须预先完成 QQ 官网 unionid 申请（审核通过）——非默认开放，本方案将 unionid 申请作为**可选增强**（默认仅 openid）；
- **无手机号**：QQ 联邦只能匿名关联（openid/unionid）——账号绑定靠 N2 策略（映射表），不依赖手机号。

---

## 三、组件设计（按 N1 模板 + 出站-only 形态）

```
_Framework/Federation.QQ/
├── TKWF.Federation.QQ.csproj          # ProjectReference ..\Federation\TKWF.Ext.Federation.csproj + TKWF.Domain(条件) + Microsoft.Extensions.Http；不引 AspNetCore
├── QqOptions.cs                        # [Options("TKWF:Federation:QQ")]——Channels 列表（ChannelId/AppId/AppSecret；EnableUnionId 开关）
├── QqChannelConfig.cs                  # ChannelId + AppId + AppSecret（凭证自持——Oracle 评审 P1-1：QQ 仅 AppId+AppKey 两凭证，AppKey=ClientSecret 同物，命名对齐 WeChat 先例 AppId+AppSecret）
├── QqApiClient.cs                      # 出站：code→access_token→/me→openid（一次性链）+ get_user_info 裁剪；**不缓存用户级 access_token**（Oracle 评审 P1-5：QQ token 是用户级 authorization_code grant，非微信应用级 client_credential——联邦认证面一次性消费不缓存；refresh_token 续票归 L7 非认证面 YAGNI）
├── QqOauthChannel.cs                   # ISsoChannel（ChannelType="qq_oauth"）：回调 code→openid（AuthenticateAsync 内 /me 换标识——authorize 构造归装配层，对齐 WeChat 先例）；AuthLevel=2
├── QqUserInfo.cs                       # 响应裁剪 record（Nickname/Avatar/...——无手机号）
├── QqFederationServiceCollectionExtensions.cs  # AddQqFederationChannels()——AddOptions().Configure + AddHttpClient<QqApiClient> + TryAddEnumerableConstructible<ISsoChannel, QqOauthChannel>
└── (无 Initializer 无 [TKWFExtension])
```

### 3.1 QqOauthChannel 流程

```
/sso/login?target_app_id=X → 装配层构造 QQ authorize URL（client_id + redirect_uri + state + scope=get_user_info）
→ 302 QQ 授权页（跳转）→ 回调 /sso/oauth/qq/callback?code=&state=
→ 验 state（CSRF）→ QqApiClient: code+client_secret → access_token → /me?access_token → openid（可选 unionid=1 若已申请）
→ SsoChannelAuthResult(Success, ExternalUserId=openid, AuthLevel=2)
→ (channel_id=qq, openid) → uid（Federation 编排层，N2 锚点策略）
```

### 3.2 信任根（降级——P5 澄清 + Oracle 评审 P1-3/P1-4）

- QQ **无入站回调** → P5 一票否决**不适用**（非违规）；
- 信任根 = **出站换取链**：code 一次性（10min）+ **state 强校验**（会话绑定防 CSRF）+ `client_secret` 换取（对称密钥防冒充）+ **redirect_uri 一致性校验**（Oracle 评审 P1-4——OAuth RFC 6749 §4.1.3 强制：code 换 token 时 redirect_uri 须与授权时一致，防 code 窃取后换 token 攻击；`QqApiClient` 出站换取时显式传递并比对）；
- 无可伪造 openid 的入站面——安全重心在"我方向 QQ 发起的授权 + code 一次性"。
- **state 校验层次边界（Oracle 评审 P1-3）**：库 `QqOauthChannel.AuthenticateAsync` **不感知 state**（对齐 WeChat 先例代码——仅校验 code 存在）；state 生成 + 会话绑定 + 回调校验归**装配层**（Federation 扩展/消费方，持有 HTTP 上下文）；F2/F3 "伪造 state 拒"验收为**装配层集成测试**（库内只做 code 存在 + QQ API 换取失败负路径）。

### 3.3 unionid 可选增强（Oracle 评审 P1-2 语义定案）

- 默认仅 openid（应用维度）；需跨应用对齐时走 **unionid 申请**（官网自助，≤60 应用）——`QqOptions.Channels[].EnableUnionId` 开关；
- **external_uid 始终 = openid**（应用维度稳定映射键，对齐 N2 §3.1 选项 B——映射表同一 channel_id 下语义一致）；开启 EnableUnionId 时额外拉 unionid，经 `ISsoAccountLinkService.SetFederationAnchorAsync` 写 `FederationAnchorOpenId`（**联盟锚点加速快照**，与 N2 定案的"TKWF 自生成平台无关锚点值"兼容——unionid 是写入辅助，锚点列存平台无关值）。

---

## 四、任务拆解

| 任务 | 描述 | 关联 | 工作量 |
|------|------|------|--------|
| T1 | csproj + Options/ChannelConfig（凭证自持） | N1 模板 | 低 |
| T2 | QqApiClient 出站（code→token→/me→openid 一次性链 + redirect_uri 一致性比对；get_user_info 裁剪；**不缓存用户级 access_token**——P1-5） | N1 模板组件 1 | 中 |
| T3 | QqOauthChannel（回调 code→openid + AuthLevel=2；**authorize 构造归装配层、channel 不感知 state**——P1-3） | N1 模板组件 2 | 中 |
| T4 | AddQqFederationChannels() 注册扩展 | N1 模板组件 7 | 低 |
| T5 | 测试：QqTestHost + OAuth 正负（code 缺/state 伪造/openid 换取）+ 生产路径探针门面 | N1 模板组件 8 | 中 |
| T6 | README（技术规范）+ 使用指南（QQ 行）更新 | — | 低 |

---

## 五、验收标准（对齐 F1-F8）

| 需求ID | 验收条件 | 验收方式 |
|--------|---------|---------|
| F1 | 库零 Initializer 零 `[TKWFExtension]` 零持久化零 Store（纯库判定） | 架构检查 |
| F2 | OAuth 通道：code→openid 正确；缺 code 拒；**伪造 state 拒（装配层集成测试——库 AuthenticateAsync 不感知 state，P1-3）**；**redirect_uri 不一致拒（P1-4）** | 自动化测试（正负）+ 装配层集成 |
| F3 | 信任根降级：state 会话绑定强校验 + client_secret 换取；无可伪造入站面（QQ 无事件推送） | 自动化测试 + 架构确认 |
| F4 | `AddQqFederationChannels()` 注册进 `IEnumerable<ISsoChannel>` 集合，未装配通道自然跳过 | 自动化测试 + 探针门面 |
| F5 | 红线合规：零 Store / 零构造注入 / 零裸 ORM / 不引 AspNetCore | 代码审查 + skill 自检清单 |
| F6 | 测试宿主生产路径（真实 DI + BindScope + Use<T>），不手写 Store 不掩盖守卫 | 测试宿主评审 |
| F7 | 凭证自持：ClientSecret 生产 AES-GCM 密文或装配注入，永不明文进配置库 | 代码审查 |
| F8 | 全量回归不破坏（当前基线全绿） | slnx 测试 |

---

## 六、风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| QQ 无回调验签（信任根弱） | 中 | 信任根降级 + state 强校验 + code 一次性；不开放入站通道（无伪造面） |
| unionid 需申请（非默认） | 中 | 默认仅 openid；unionid 作为可选增强（EnableUnionId 开关 + 申请指引） |
| 无手机号/邮箱 | 中 | 匿名关联（openid/unionid）——账号绑定靠 N2 映射策略，不依赖手机号 |
| access_token 60 天长寿命 | 低 | L1 缓存 + 提前刷新；refresh_token 一次有效须管理 |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-05 | v0.1.0 | 初始版本——N3 QQ 平台网关库开发方案：协议事实（官方核实：跳转无扫码/state 必填/unionid 预申请/无手机号/无回调验签）+ 出站-only 形态 + 信任根降级（P5 澄清）+ unionid 可选增强 + 组件设计（按 N1 模板）+ 任务拆解 T1-T6 + 验收 F1-F8 |
| 2026-10-06 | v0.1.0 | **Oracle 评审（bg_15e2562e）PASS WITH CONDITIONS——P1 五项落实**：① §三凭证字段澄清——`QqChannelConfig` 仅 `ChannelId + AppId + AppSecret`（QQ 仅 AppId+AppKey 两凭证，AppKey=ClientSecret 同物，命名对齐 WeChat 先例）（P1-1）；② §3.3 unionid 语义定案——`external_uid` **始终 = openid**（稳定映射键），unionid 经 `SetFederationAnchorAsync` 写联盟锚点辅助（对齐 N2 §3.1 选项 B）（P1-2）；③ §3.2 state 校验层次——库 `AuthenticateAsync` 不感知 state（对齐 WeChat 先例），state 校验归装配层，F2/F3 标注装配层集成测试（P1-3）；④ §3.2 补 **redirect_uri 一致性校验**（OAuth RFC 6749 §4.1.3——code 换 token 时 redirect_uri 须与授权时一致，防 code 窃取换 token；F2 验收补"redirect_uri 不一致拒"）（P1-4）；⑤ §三 `QqApiClient` **不缓存用户级 access_token**（QQ token = 用户级 authorization_code grant，非微信应用级——联邦认证面一次性消费；refresh_token 续票归 L7 非认证面 YAGNI）（P1-5） |

<!-- EOF -->
