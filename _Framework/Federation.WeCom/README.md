# TKWF.Federation.WeCom 企业微信网关库技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律——纯内存态 access_token 缓存）/ **双向**（OAuth 身份获取 + 通讯录/事件回调验签）/ **凭证自持**（`WeComOptions`——Oracle P2-4，不复用 Authentication `IPlatformCredentialService`）/ **三道闸门信任根**（msg_signature 验签 + AES 解密 + **ReceiveId=corpid/suiteid 校验——Oracle 评审 P1-1：防跨企业回调混串**）/ **无独立 token 端点**（code 直接换身份）/ **external_uid 双策略**（三方成员 `open_userid` / 自建成员 `{CorpId}:{userid}` 复合 / 非成员 `openid`——Oracle 评审 P0-1）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类）

> ✅ **实施进度（2026-10-06）**：V0.1.0 **全部落地**——`WeComApiClient` 出站客户端（gettoken 双用途缓存键 + getuserinfo 三路径双策略 + getuserdetail 敏感信息即用即弃）+ `WeComOptions`/`WeComChannelConfig` 配置 + `WeComUserInfo`/`WeComSensitiveInfo` 响应裁剪（T1-T2）+ **`WeComOauthChannel`（wecom_oauth）/ `WeComEventChannel`（wecom_event）+ `WeComEventCrypto`（DecryptAndVerify receiveid 校验 + VerifyAndDecryptEcho）/ `WeComAuthorizeUrlBuilder`（双流 URL 模板）/ `AddWeComFederationChannels()` 注册方法**（T3-T6）+ 测试 41 用例全绿（生产路径 + 三路径双策略 + 信任根正负 + receiveid 篡改拒 + echostr 握手）。组件清单全部 ✅ 已实现。

---

## 一、定位

认证中心**对外（上游）接企业微信 IdP** 的平台网关库（大使馆）——把企业微信**双授权流**（内置客户端 Webview 跳转 + 桌面端扫码）与**通讯录/事件回调**（wecom_event）两种身份获取流程，翻译成 `(channel_id, external_uid) → uid`，归一认证中心 Federation **token2**（ES256 联邦流）。企业微信是国内 To B **企业员工身份**标配（与微信生态互操作但账号体系独立）。

| 方向术语 | 说明 |
|---------|------|
| **对外（上游）** | 本库方向对外接企业微信 IdP——认证中心作为 **RP / OAuth Client**（对企业微信）消费企业员工身份 |
| **对内（下游）** | **SSO 是认证中心对下游的能力（token2 统一断言），不是本库的协议**——本库只管从上游"进货"身份，不产 SSO 断言 |
| 一句话 | 企业微信库用 OAuth/回调从上游进货身份，认证中心用 SSO 对外出货——本库 = FedSSO **上侧接入器**（IdP Adapter） |

**组合矩阵**（消费方按需装配）：只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录）；**Authentication + Federation = 认证中心实例**；**Federation + `TKWF.Federation.WeCom` = 纯外部联邦登录（BYO IdP）**——本库引 Federation 扩展实现 `ISsoChannel`，经 Federation 窄适配编排身份获取方向。

**不包含**：`@wecom/jssdk` **内嵌扫码组件前端**（`ww.createWWLoginPanel`——归装配层/前端，库提供扫码 authorize URL）；**通讯录管理 API**（成员增删改/部门树 `user/get` 等组织管理能力——非认证面归 L7 YAGNI）；**企业微信-微信 unionid 打通**（双通道+主体一致性非默认——锚点 TKWF 侧自建映射 N2）；`snsapi_privateinfo` **敏感信息持久化**（user_ticket 即用即弃不落库——隐私最小化）；`/{prefix}/*` 端点映射（归 Federation 扩展/消费方装配层）。

## 二、安装与接线

### 1. 消费方引用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.WeCom\TKWF.Federation.WeCom.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- 组合式：Federation 扩展（ISsoChannel 契约 + 窄适配编排） -->
```

### 2. 注册通道（T6 已实现——库提供 `AddWeComFederationChannels()`）

库无 Initializer（纯库）——经库提供 `AddWeComFederationChannels()` 扩展方法（`Microsoft.Extensions.DependencyInjection` 命名空间）注册：

```csharp
// 消费方 ConfigureServices（DomainHostConfigureServices）
services.AddWeComFederationChannels();   // 内部 TryAddEnumerableConstructible<ISsoChannel, WeComOauthChannel/WeComEventChannel>
```

### 3. 配置 `TKWF:Federation:WeCom`

```jsonc
{
  "TKWF": {
    "Federation": {
      "WeCom": {
        "DefaultScope": "snsapi_base",          // 兜底 scope（装配层构造 authorize URL 未显式指定时；P1-5）
        "Channels": [
          {
            "ChannelId": "wecom-app-main",      // channel 实例 id（应用 id——ISsoChannel.ChannelId 选区依据）
            "CorpId": "ww1234567890abcd",       // 企业 CorpID（三方 = SuiteID——appid 参数 + 回调 receiveid 期望）
            "CorpSecret": "<AES-GCM 密文或装配注入>", // 应用 CorpSecret（三方 = SuiteSecret）——gettoken 换取（P1-2 缓存键成分）
            "AgentId": "1000002",               // 应用 AgentId（snsapi_privateinfo 必填——启用敏感信息时 fail-fast P1-5）
            "IsThirdParty": false,              // 三方应用标志（默认 false 自建）——端点/双策略/receiveid 期望切换（P0-1/P1-1）
            "EnableSensitiveInfo": false,       // 启用 snsapi_privateinfo 敏感信息增强（user_ticket 即用即弃）
            "Token": "<回调验签 msg_signature 令牌>", // T5 入站信任根
            "EncodingAESKey": "<43 字符消息加解密密钥>"  // T5 AES 解密（Base64Decode 派生 32 字节 AESKey）
          }
        ]
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:WeCom")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定（对齐 WeChatOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。
- **凭证承载形态**（Oracle 评审点 4 P2）：开发——明文 `appsettings.json` 配置；生产——**AES-GCM 密文**或装配注入（K8s secret mount）；**CorpSecret/EncodingAESKey 永不明文进配置库**。channel 凭证归本库自持，与 Federation `SsoSecretKeyStore`/Authentication `PlatformCredentialService` 均独立。
- **scope**：`snsapi_base`（静默仅 userid）/ `snsapi_privateinfo`（手动敏感信息，须成员在可见范围）；无 `snsapi_login`。scope 选取归装配层（authorize URL 构造时决定）+ `DefaultScope` 兜底。

### 4. 编排（Federation 白名单 + 装配层路由）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]   // Federation 扩展白名单（本库自身无 Initializer）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

Federation 扩展按 channel 类型路由：`wecom_oauth` → 302 企业微信 authorize（或前端 `@wecom/jssdk` 内嵌扫码）；`wecom_event` → 回调接收端点（装配层映射，验签归库侧 `WeComEventCrypto`；URL 验证 echostr 握手归装配层调 `VerifyAndDecryptEcho` 原语）。

## 三、组件清单

| 组件 | 形态 | 方向 | 状态 |
|------|------|------|------|
| `WeComApiClient` | 出站 API 客户端——`GetAccessTokenAsync`（`cgi-bin/gettoken` 企业 access_token **L1 缓存 + SemaphoreSlim 并发锁 + 提前 5 分钟刷新；缓存键 = corpid+":"+corpsecret 用途——应用级 vs 通讯录级互不覆盖 P1-2**）/ `GetIdentityAsync`（`cgi-bin/auth/getuserinfo`（自建）/`getuserinfo3rd`（三方）**code→身份一次性，无独立 token 端点**；**三路径双策略解析 P0-1**）/ `GetSensitiveInfoAsync`（`cgi-bin/auth/getuserdetail` **user_ticket 换敏感信息即用即弃不落库 P1-4**） | 出站（对企业微信 API） | ✅ 已实现（T2） |
| `WeComOptions` + `WeComChannelConfig` | `TKWF:Federation:WeCom` 节——`DefaultScope` 兜底 + `Channels` 列表（ChannelId/CorpId/CorpSecret/AgentId/IsThirdParty/EnableSensitiveInfo/Token/EncodingAESKey）；凭证自持 | 配置 | ✅ 已实现（T1） |
| `WeComUserInfo` + `WeComSensitiveInfo` | 响应裁剪 record（UserId/OpenId/OpenUserid/ExternalUserid/UserTicket——三标识双策略来源）；敏感信息独立 record（**敏感字段标注防日志泄露 P2-3，DeviceId 不透出**） | DTO | ✅ 已实现（T2） |
| `WeComOauthChannel` | `ISsoChannel`（`ChannelType="wecom_oauth"`）——回调 code→身份（**双授权流统一 getuserinfo**）；**external_uid 双策略（P0-1）**；**AgentId fail-fast（P1-5）** | 对外（上游 IdP 适配） | ✅ 已实现（T3） |
| `WeComEventChannel` + `WeComEventCrypto` | `ISsoChannel`（`ChannelType="wecom_event"`）——通讯录/事件回调三道闸门（msg_signature 验签 + AES 解密 + **ReceiveId=corpid/suiteid 校验 P1-1**）+ XML 身份提取；**URL 验证 echostr 原语（P1-6）** | 对外（上游 IdP 适配） | ✅ 已实现（T4/T5） |
| `WeComAuthorizeUrlBuilder` | authorize URL 构造辅助（静态纯字符串——**双授权流 URL 模板 P1-3/P2-4**：Webview `#wechat_redirect` vs 扫码 `login_type`；agentid 条件拼接） | 装配辅助 | ✅ 已实现（T3） |
| `AddWeComFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel>`——channel 业务参数经 Options 注入，不占 ctor `IDomainUser` 槽，ADR92 兼容） | 装配 | ✅ 已实现（T6） |

## 四、双通道契约（T3/T5 已实现）

| 通道 | 场景 | 流程 |
|------|------|------|
| `wecom_oauth` | 双授权流（内置客户端 Webview 跳转 / 桌面端扫码）统一身份获取 | 装配层构造 authorize URL（`WeComAuthorizeUrlBuilder`——Webview `open.weixin.qq.com/connect/oauth2/authorize` + `#wechat_redirect` 尾缀 / 扫码 `login.work.weixin.qq.com/wwlogin/sso/login` + `login_type=CorpApp\|ServiceApp`）→ 授权 → 回调 code → `GetIdentityAsync`（gettoken 企业 token + getuserinfo/getuserinfo3rd **code 直接换身份**）→ **external_uid 双策略（P0-1）** → `SsoChannelAuthResult` → 编排层 `(channel_id, external_uid) → uid` → Federation 签 token2 |
| `wecom_event` | 通讯录变更/事件推送入站 | 企业微信推送 → 接收端点（**三道闸门：msg_signature 验签 + AES 解密 + ReceiveId=corpid/suiteid 校验**；XML 提取 UserID/OpenUserID → external_uid 双策略）→ 落库即返（回调不阻塞业务——官方"无法保证 100% 回调成功"不强依赖；URL 验证 echostr 握手归装配层）→ 编排层映射/解绑 |

- **external_uid 双策略（Oracle 评审 P0-1 定案）**——按应用类型 + 成员状态选取：

```
external_uid 选取策略：
├─ 三方应用企业成员：open_userid   ← 体系内明示全局唯一（同服务商跨应用相同），最佳稳定键——直接作映射主键
│    （完全规避 userid 复用风险；corpid + userid 经 DTO 透出作组织维度辅助）
├─ 自建应用企业成员：{CorpId}:{userid}   ← 互联企业须 CorpId 复合消歧
│    （userid 复用风险经 N2 §3.2 治理：不自动并合 + 认证用户绑定 + 冲突拒绝 + 人工解绑流程——离职解绑归装配层）
├─ 非企业成员：openid   ← 对当前企业唯一
│    （external_userid 经 DTO 透出作锚点辅助）
└─ open_userid（三方应用，全局唯一）→ 优先作联盟锚点辅助写入（N2 anchor 列平台无关值）
```

- **user_ticket 敏感信息（P1-4 数据流闭环）**：`EnableSensitiveInfo` 启用时 `GetIdentityAsync` 条件性透出 `UserTicket`（自建 96442 + 三方 98179 均返回；**扫码 98177 不返回→降级仅身份**）→ 装配层按需 `GetSensitiveInfoAsync(userTicket)` → `WeComSensitiveInfo`（敏感标注）→ **编排层帧内 `User.Use<ISsoAccountQueryService>()` 消费做绑定 → 帧结束随 GC 回收即弃——不进审计日志 ArgumentsJson/不持久化**。
- **双授权流统一**：Webview 跳转与桌面扫码**换身份端点相同**（getuserinfo）——通道只收 code，两流构造差异归装配层（P1-3）。

## 五、信任根（通讯录/事件回调——三道闸门一票否决）

`msg_signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))` + AES 解密（EncodingAESKey 43 字符 `Base64Decode` 派生 32 字节 AESKey）+ **ReceiveId 校验**——**漏验 = 可伪造任意员工身份 = 体系崩溃**（M4 方案 §3.4，对齐微信同家族）：

- **闸门 ① 验签失败即拒，无降级无旁路**（一票否决）；
- **闸门 ② AES 解密**：明文布局 = `random(16) + msg_len(4 网络序) + msg(XML) + receiveid`；
- **闸门 ③ ReceiveId 校验（Oracle 评审 P1-1——增量）**：WeChat 先例 DecryptMsg 只取 msg 丢弃 receiveid——本库 **`DecryptAndVerify` 一体化提取 `plain[20+msgLen..]` 与期望（自建 corpid / 三方 suiteid）恒定时间比对**（`CryptographicOperations.FixedTimeEquals`），不符抛 `CryptographicException`——**防跨企业回调混串**（fail-closed：期望未配置亦抛）；
- **URL 验证握手（Oracle 评审 P1-6）**：回调 URL 验证（GET 带 echostr AES 密文）验签 + 解密回显明文（**1s 硬约束**）——`VerifyAndDecryptEcho(token, timestamp, nonce, echostr, msgSignature, encodingAESKey, expectedReceiveId)` 原语归装配层端点握手（非 EventChannel 业务路径）；验签失败返回 null（装配层回显失败语义）；
- 正向：合法 `msg_signature` + AES 密文 + receiveid 匹配通过；负向：篡改签名拒 / 篡改密文拒 / **receiveid 篡改拒（跨企业混串防御）**（信任根自动化测试正负路径——T7 验收 F3）；
- **回调可靠性**：官方"无法保证 100% 回调成功"——业务**不强依赖回调**（认证主路径走 OAuth 出站；回调只做增量/解绑通知）；URL 验证 1s / 业务 5s 响应——回调响应策略统一（验签+解密+落库归阻塞路径、业务异步、按平台最严超时，P2-2 回填 N1 模板）。

## 六、安全边界

- **数据访问红线合规**：零持久化零 Store（tkwf-extension 铁律）——纯内存态 access_token 缓存；channel 业务参数经 Options 注入不占 ctor `IDomainUser` 槽；`WeComApiClient` 直构（非 `IDomainService`），HttpClient 经 `IHttpClientFactory`（DI 生命周期托管，避免 HttpClient 悬挂 socket）。
- **凭证自持**（Oracle P2-4）：CorpSecret/Token/EncodingAESKey 从 `WeComOptions.Channels` 按 channel 配置选区——**不经** Authentication `IPlatformCredentialService`；生产 AES-GCM 密文或装配注入，永不明文进配置库。
- **三道闸门信任根**：回调验签（msg_signature + AES 解密 + ReceiveId 校验）漏验 = 伪造任意员工身份（见 §五）。
- **不引 AspNetCore**（Oracle 评审点 5）：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`）——入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类（验签逻辑收 `string` 参数，端点映射在外）。
- **企业 access_token 时效**：L1 缓存提前 5 分钟过期刷新（对齐 Authentication 语义），防 stampede（SemaphoreSlim 并发锁 + 双检）；**缓存键含 corpsecret 用途——应用级 vs 通讯录级互不覆盖（P1-2）**。
- **OAuth 安全**：回调无签名（仅 state 防 CSRF——企业微信平台限制）；code 5min 一次性；域名须完全匹配可信域名否则 50001（redirect_uri 一致性校验归装配层——P2-5）。
- **敏感信息隐私**：user_ticket 一次性换取即用即弃（1800s），**不落库**——数据流闭环（P1-4）；`WeComSensitiveInfo` 敏感字段（Name/Mobile/Email）标注防日志泄露（P2-3），DeviceId 不透出。

## 七、与既有资产边界（三问注记）

| 资产 | 归属 | 定位 |
|------|------|------|
| `WeComOauthChannel` / `WeComEventChannel` + 轻量 `WeComApiClient` | **本库（`TKWF.Federation.WeCom`）** | **联邦 IdP 适配**（外部身份源 → token2 联邦流） |
| `WeChatOauthChannel` / `WeChatEventChannel` | `TKWF.Federation.WeChat` | 微信公众平台适配（同家族——验签算法/回调形态几乎相同；企业微信补足**企业员工身份维度** userid/通讯录回调） |

- **为什么是库不是扩展**：平台网关无持久化、纯协议逻辑——按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库"判据应为**库**（AGENTS §8：`TKWF.*`=库）。每次平台适配一个扩展 = 包泛滥；`TKWF.Federation.{平台}` 单库多命名空间收敛。
- **同家族独立性**：WeChat/WeCom/DingTalk 三平台验签基元同构（AES-256-CBC + sha1）——独立实现避免跨库依赖（P2-1）；三平台同算法落地后登记"验签基元提炼待办"——待第 4 个同算法平台落地后评估（YAGNI 暂不实施）。
- **与微信 unionid 打通**：企业微信-微信 unionid 打通需双通道 + 主体一致性（非默认能力）——锚点 TKWF 侧自建映射（N2 平台无关），不依赖跨生态对齐。

## 八、架构决策记录

- 平台网关立项依据：`docs/Federation/平台网关-总规划.md`（近中远 M4 中期 P1——国内平台 B 端与微信生态互操作）
- M4 开发方案：`docs/Federation/M4-企业微信平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS——**P0×1 external_uid 双策略定案** + P1×6：ReceiveId 校验实现细节 / gettoken 缓存键 / 双授权流 URL 模板 / user_ticket 数据流闭环 / snsapi scope 机制 / URL 验证 echostr；P2×6 同步落实）
- 归层模型 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（`TKWF.Federation.{平台}` = 大使馆/平台网关库）
- 微信适配先例：`docs/Federation.WeChat/微信公众平台连接器-使用指南.md`（同家族形态 + 本库增量：双授权流 / 双策略 external_uid / ReceiveId 校验）

## 九、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | M4 企业微信平台网关库全量落地：`WeComApiClient` 出站（gettoken 双用途缓存键 P1-2 + getuserinfo/getuserinfo3rd 三路径双策略 P0-1 + getuserdetail 敏感信息即用即弃 P1-4）+ `WeComOptions`/`WeComChannelConfig`（凭证自持含 IsThirdParty/EnableSensitiveInfo + DefaultScope 兜底）+ `WeComUserInfo`/`WeComSensitiveInfo`（敏感标注 P2-3）+ `WeComOauthChannel`（wecom_oauth——external_uid 双策略 + AgentId fail-fast P1-5）+ `WeComEventChannel` + `WeComEventCrypto`（DecryptAndVerify ReceiveId 校验 P1-1 + VerifyAndDecryptEcho P1-6）+ `WeComAuthorizeUrlBuilder`（双流 URL 模板 P1-3/P2-4）+ `AddWeComFederationChannels()`；纯库（无 Initializer 无 `[TKWFExtension]`），csproj 引 Federation 扩展实现 ISsoChannel + `Microsoft.Extensions.Http`；测试 41 用例全绿（生产路径 + 三路径双策略 + 信任根正负 + receiveid 篡改拒 + echostr 握手）；slnx 接线 + 全量回归 39 测试项目全绿 |

<!-- EOF -->
