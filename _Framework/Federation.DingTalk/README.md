# TKWF.Federation.DingTalk 钉钉开放平台网关库技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律——纯内存态企业 token 缓存）/ **双向**（OAuth 身份获取 + 事件订阅推送验签）/ **凭证自持**（`DingTalkOptions`——Oracle P2-4，不复用 Authentication `IPlatformCredentialService`）/ **三道闸门信任根**（signature 验签 + AES 解密 + **receiveid=corpId 校验——Oracle 评审 P1-1：forged org-change event 可授予企业访问权，敏感度高于微信消息事件**）/ **1200ms 硬约束**（库 `AuthenticateAsync` CPU-only 无出站 <50ms，落库即返 success 归装配层 persist-then-ack——Oracle 评审 P1-2）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类）

> ✅ **实施进度（2026-10-06）**：V0.1.0 **全部落地**——`DingTalkApiClient` 出站客户端（userAccessToken 一次性 + 新旧双企业 token L1 缓存并发锁提前刷新 + getbyunionid + /contact/users 裁剪）+ `DingTalkOptions`/`DingTalkChannelConfig` 配置 + `DingTalkUserInfo` 响应裁剪（T1-T2）+ **`DingTalkOauthChannel`（dingtalk_oauth）/ `DingTalkEventChannel`（dingtalk_event）+ `DingTalkEventCrypto`（三道闸门含 receiveid 校验）/ `AddDingTalkFederationChannels()` 注册方法**（T3-T6）+ 测试 23 用例全绿（生产路径 + 信任根正负路径 + receiveid 篡改拒）。组件清单全部 ✅ 已实现。

---

## 一、定位

认证中心**对外（上游）接钉钉 IdP** 的平台网关库（大使馆）——把钉钉**新 OAuth2 网页授权**（`dingtalk_oauth`，扫码内嵌/跳转）与**事件订阅推送**（`dingtalk_event`）两种身份获取流程，翻译成 `(channel_id, openId) → uid`，归一认证中心 Federation **token2**（ES256 联邦流）。钉钉是国内 To B **企业员工登录标配**（B 端组织/教育线企业场景）。

| 方向术语 | 说明 |
|---------|------|
| **对外（上游）** | 本库方向对外接钉钉 IdP——认证中心作为 **RP / OAuth Client**（对钉钉）消费钉钉身份 |
| **对内（下游）** | **SSO 是认证中心对下游的能力（token2 统一断言），不是本库的协议**——本库只管从上游"进货"身份（OAuth/事件推送），不产 SSO 断言 |
| 一句话 | 钉钉库用 OAuth/"事件推送"从上游进货身份，认证中心用 SSO 对外出货——本库 = FedSSO **上侧接入器**（IdP Adapter），非 FedSSO 本身 |

**组合矩阵**（消费方按需装配，归层模型"不是所有 AuthCenter 都需要 Federation"）：
- 只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录）
- **Authentication + Federation = 认证中心实例**（内部认证 + 多应用联邦 SSO）
- **Federation + `TKWF.Federation.DingTalk` = 纯外部联邦登录（BYO IdP）**：本库引 Federation 扩展实现 `ISsoChannel`，经 Federation 窄适配编排身份获取方向，消费方零内部 Provider 全量

**不包含**：旧 `qrconnect`/`snsapi_login` 协议（法律旧码——文档迁历史，本库**不含**，仅注记）；**Stream Mode**（官方建议新应用替代 Webhook 的事件接收方式——长连接消费端 + 独立 SDK 语义，与"纯库不引 AspNetCore"约束冲突，归装配层另立项；Webhook 验签 crypto 基元 `DingTalkEventCrypto` 与 Stream 解密共用，Stream 落地时复用）；`exclusiveLogin`（专属组织二维码——扫码登录增强形态归装配层调用，库提供 authorize URL 构造能力）；`/{prefix}/*` 端点映射（归 Federation 扩展/消费方装配层）；其他平台网关（微信/QQ/Google——`TKWF.Federation.{平台}` 模式复制）。

## 二、安装与接线

### 1. 消费方引用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.DingTalk\TKWF.Federation.DingTalk.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- 组合式：Federation 扩展（ISsoChannel 契约 + 窄适配编排） -->
```

### 2. 注册通道（T6 已实现——库提供 `AddDingTalkFederationChannels()`）

库无 Initializer（纯库）——经库提供 `AddDingTalkFederationChannels()` 扩展方法（`Microsoft.Extensions.DependencyInjection` 命名空间）注册：

```csharp
// 消费方 ConfigureServices（DomainHostConfigureServices）
services.AddDingTalkFederationChannels();   // 内部 TryAddEnumerableConstructible<ISsoChannel, DingTalkOauthChannel/DingTalkEventChannel>
```

### 3. 配置 `TKWF:Federation:DingTalk`

```jsonc
{
  "TKWF": {
    "Federation": {
      "DingTalk": {
        "Channels": [
          {
            "ChannelId": "dingtalk-app-main",       // channel 实例 id（应用 id——ISsoChannel.ChannelId 选区依据）
            "CorpId": "ding1234567890",             // 企业 CorpId——事件回调 receiveid 校验第二道闸门（P1-1）+ getbyunionid 企业上下文
            "AppKey": "dingAppKey000000000000",     // 应用 AppKey（凭证解析键——新旧协议企业 token 凭证，P1-5）
            "AppSecret": "<AES-GCM 密文或装配注入>", // 生产永不明文进配置库（Oracle P2-4：凭证自持）
            "Token": "<事件推送验签 signature 令牌>", // T5 入站信任根
            "EncodingAESKey": "<43 字符消息加解密密钥>"  // T5 AES 解密（Base64Decode 派生 32 字节 AESKey）
          }
        ]
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:DingTalk")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定（对齐 WeChatOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。
- **凭证承载形态**（Oracle 评审点 4 P2）：开发——明文 `appsettings.json` 配置；生产——**AES-GCM 密文**或装配注入（K8s secret mount）；**AppSecret/EncodingAESKey 永不明文进配置库**。channel 凭证归本库自持，与 Federation `SsoSecretKeyStore`/Authentication `PlatformCredentialService` 均独立。
- **授权 scope**：新协议 `openid`（或 `openid corpid`——后者 token 响应含 corpId）；`prompt=consent` 必填。

### 4. 编排（Federation 白名单 + 装配层路由）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]   // Federation 扩展白名单（本库自身无 Initializer）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

Federation 扩展按 channel 类型路由：`dingtalk_oauth` → 302 钉钉 authorize（或 DTFrameLogin iframe 内嵌）；`dingtalk_event` → 回调接收端点（装配层映射，验签归库侧 `DingTalkEventCrypto`）。

## 三、组件清单

| 组件 | 形态 | 方向 | 状态 |
|------|------|------|------|
| `DingTalkApiClient` | 出站 API 客户端——`GetUserAccessTokenAsync`（`/v1.0/oauth2/userAccessToken` **code→用户级 token，一次性交换不缓存**）/ `GetLegacyAccessTokenAsync`（`gettoken` 旧版企业 token **L1 缓存 + SemaphoreSlim 并发锁 + 提前 5 分钟刷新**——供 getbyunionid）/ `GetV1AccessTokenAsync`（`/v1.0/oauth2/accessToken` 新版企业 token 同缓存——供 contact API）/ `GetUserIdAsync`（`topapi/user/getbyunionid` **unionId→userid——供装配层按需调用，非 channel 路径 P1-4**）/ `GetUserInfoAsync`（`/v1.0/contact/users/{unionId}` 用户信息裁剪，mobile 三方脱敏） | 出站（对钉钉 API） | ✅ 已实现（T2） |
| `DingTalkOptions` + `DingTalkChannelConfig` | `TKWF:Federation:DingTalk` 节——`Channels` 列表按应用区分（ChannelId/CorpId/AppKey/AppSecret/Token/EncodingAESKey）；凭证自持 | 配置 | ✅ 已实现（T1） |
| `DingTalkUserInfo` | 响应裁剪 record（OpenId/UnionId/Userid/Nick/Avatar/Mobile——B 端员工组织字段）；**两步填充（P1-4/P2-5）：/contact/users → nick/avatar/mobile + getbyunionid → userid** | DTO | ✅ 已实现（T2） |
| `DingTalkOauthChannel` | `ISsoChannel`（`ChannelType="dingtalk_oauth"`）——回调 code→openId/unionId（AuthenticateAsync 内 userAccessToken 换）；**external_uid 恒 = openId（P1-4 两步流）** | 对外（上游 IdP 适配） | ✅ 已实现（T3） |
| `DingTalkEventChannel` + `DingTalkEventCrypto` | `ISsoChannel`（`ChannelType="dingtalk_event"`）——事件推送三道闸门（signature 验签 + AES 解密 + **receiveid=corpId 校验 P1-1**）+ FromUserId→openId；CPU-only <50ms | 对外（上游 IdP 适配） | ✅ 已实现（T4/T5） |
| `AddDingTalkFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel>`——channel 业务参数经 Options 注入，不占 ctor `IDomainUser` 槽，ADR92 兼容） | 装配 | ✅ 已实现（T6） |

## 四、双通道契约（T3/T5 已实现）

| 通道 | 场景 | 流程 |
|------|------|------|
| `dingtalk_oauth` | 新 OAuth2 网页授权（扫码内嵌 DTFrameLogin/302 跳转） | 装配层构造 authorize URL（`login.dingtalk.com/oauth2/auth`？client_id+redirect_uri+scope=openid+**prompt=consent 必填**+state——iframe 内嵌须与 redirect_uri 同源）→ 回调 code → `GetUserAccessTokenAsync`（**token 端点不接受 redirect_uri——P1-3，一致性校验归装配层**；accessToken 7200s 一次性不缓存）→ openId/unionId/corpId → `SsoChannelAuthResult(ExternalUserId=openId)` → 编排层 `(channel_id, openId) → uid` → Federation 签 token2 |
| `dingtalk_event` | 事件订阅推送入站（扫码成功/组织变更） | 钉钉推送事件 → 接收端点（**三道闸门：signature 验签 + AES 解密 + receiveid=corpId 校验**；`FromUserId`=openId）→ 落库即返 success（**1200ms 硬约束——装配层 persist-then-ack**，异步业务不阻塞回调响应）→ 编排层映射 |

- **联盟锚点配合**：unionId 写联盟锚点辅助（开启 `EnableUnionId` 时经 `ISsoAccountLinkService.SetFederationAnchorAsync` 写 `FederationAnchorOpenId`——N2 选项 B：anchor 列存 TKWF 自生成平台无关锚点值，unionid 是辅助写入——兼容）；钉钉 unionId 封闭于开发者账号（不可跨开发者）——锚点须 TKWF 侧自建映射。
- **userid（企业维度员工 id）**：B 端需要组织身份时装配层按需调 `DingTalkApiClient.GetUserIdAsync(unionId, corpId)` 经 `DingTalkUserInfo` 承载——**非 external_uid**（依赖企业内成员关系，随组织变动不稳定，不参与联邦映射键；P1-4 两步流）。

## 五、信任根（事件推送验签——三道闸门一票否决）

`signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))` + AES 解密（EncodingAESKey 43 字符 `Base64Decode` 派生 32 字节 AESKey）+ **receiveid=corpId 校验**——**漏验 = 可伪造任意 unionId/openId = 体系崩溃**（M3 方案 §3.4）：

- **闸门 ① 验签失败即拒，无降级无旁路**（一票否决）；
- **闸门 ② AES 解密**：明文布局 = `random(16) + msg_len(4 网络序) + msg(JSON) + receiveid(corpId)`（Oracle 评审 P1-6——msg 为 JSON 非 XML）；
- **闸门 ③ receiveid 校验（Oracle 评审 P1-1——第二道闸门）**：解密后提取明文尾部 receiveid 与 `DingTalkChannelConfig.CorpId` **恒定时间比对**（`CryptographicOperations.FixedTimeEquals`），不符抛 `CryptographicException`——forged org-change event 可授予企业访问权，敏感度高于微信消息事件（微信无此闸门）；
- 正向：合法 `signature` + AES 密文 + receiveid 匹配通过；负向：篡改签名拒 / 篡改密文拒 / **receiveid 篡改拒**（信任根自动化测试正负路径——T7 验收 F3）；
- **1200ms 硬约束**（最严，Oracle 评审 P1-2）：库 `AuthenticateAsync` = 验签 + 解密 + receiveid 校验 + FromUserId 提取（**CPU-only，无出站调用，<50ms**）；落库即返 success 是装配层职责（persist-then-ack + `IHostedService`/`Channel<T>` 队列推荐）；2500ms 为日常事件响应超时，实现按 1200ms 计（更严覆盖）。

## 六、安全边界

- **数据访问红线合规**：零持久化零 Store（tkwf-extension 铁律）——纯内存态 token 缓存（新旧双域各自独立）；channel 业务参数经 Options 注入不占 ctor `IDomainUser` 槽；`DingTalkApiClient` 直构（非 `IDomainService`），HttpClient 经 `IHttpClientFactory`（DI 生命周期托管，避免 HttpClient 悬挂 socket）。
- **凭证自持**（Oracle P2-4）：AppSecret/Token/EncodingAESKey 从 `DingTalkOptions.Channels` 按 AppKey 精确匹配解析——**不经** Authentication `IPlatformCredentialService`；生产 AES-GCM 密文或装配注入，永不明文进配置库。企业级 token 凭证复用 OAuth 凭证（钉钉新模式无独立 enterprise secret——Oracle 评审 P1-5）。
- **三道闸门信任根**：事件推送验签（signature + AES 解密 + receiveid=corpId）漏验 = 伪造任意 unionId/openId（见 §五）。
- **不引 AspNetCore**（Oracle 评审点 5）：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`）——入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类（验签逻辑收 `string` 参数，端点映射在外）。
- **企业级 token 时效**：新旧双 token L1 缓存提前 5 分钟过期刷新（对齐 Authentication 语义），防 stampede（SemaphoreSlim 并发锁 + 双检）。
- **OAuth 安全**：token 端点不接受 redirect_uri（P1-3——redirect_uri 仅在 authorize 回调用，一致性校验归装配层 state 会话绑定 + code 一次性 + clientSecret 对称换取）；回调无签名（仅 state 防 CSRF——钉钉平台限制）。

## 七、与既有资产边界（三问注记）

| 资产 | 归属 | 定位 |
|------|------|------|
| `DingTalkOauthChannel` / `DingTalkEventChannel` + 轻量 `DingTalkApiClient` | **本库（`TKWF.Federation.DingTalk`）** | **联邦 IdP 适配**（外部身份源 → token2 联邦流） |
| Authentication 钉钉认证方式（如有） | Authentication **主包** | 认证中心**内部认证方式**（→ token1 签发）——单应用登录 |

- **为什么是库不是扩展**：平台网关无持久化、纯协议逻辑——按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库"判据应为**库**（AGENTS §8：`TKWF.*`=库）。每次平台适配一个扩展 = 包泛滥；`TKWF.Federation.{平台}` 单库多命名空间收敛。
- **两会话永不交叉**：装配实例按需选装（认证中心实例用联邦流；单应用业务系统用内部 Provider）——凭证各自持有、流程各自走、产物不同（token1 vs token2）。

## 八、架构决策记录

- 平台网关立项依据：`docs/Federation/平台网关-总规划.md`（近中远 M3 中期 P1——国内平台 B 端代表）
- M3 开发方案：`docs/Federation/M3-钉钉平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS——P1×6：receiveId/corpId 校验 / 1200ms 边界 / redirect_uri 删除 / userid 两步流 / EnterpriseCredentials 删除改复用 OAuth 凭证 / 解密明文格式 JSON；P2×8 同步落实）
- 归层模型 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（`TKWF.Federation.{平台}` = 大使馆/平台网关库）
- 微信适配先例：`docs/Federation.WeChat/微信公众平台连接器-使用指南.md`（本库为同构双通道形态 + 钉钉增量三道闸门）

## 九、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | M3 钉钉平台网关库全量落地：`DingTalkApiClient` 出站（userAccessToken 一次性 + 新旧双企业 token L1 缓存并发锁提前刷新 + getbyunionid 供装配层 + /contact/users 裁剪）+ `DingTalkOptions`/`DingTalkChannelConfig`（凭证自持含 CorpId）+ `DingTalkUserInfo`（两步填充）+ `DingTalkOauthChannel`（dingtalk_oauth——external_uid 恒=openId）+ `DingTalkEventChannel` + `DingTalkEventCrypto`（三道闸门含 receiveid=corpId 校验，CPU-only <50ms）+ `AddDingTalkFederationChannels()`；纯库（无 Initializer 无 `[TKWFExtension]`），csproj 引 Federation 扩展实现 ISsoChannel + `Microsoft.Extensions.Http`；测试 23 用例全绿（生产路径 + 探针门面 + 信任根正负路径 + receiveid 篡改拒）；slnx 接线 + 全量回归 38 测试项目全绿 |

<!-- EOF -->
