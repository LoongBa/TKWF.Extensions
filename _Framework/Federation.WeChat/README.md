# TKWF.Federation.WeChat 微信公众平台连接器技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.6.0（2026-10-20 Iteration 2——EventType 归一 + `wechat.` 前缀 Attributes + IFollowChannelQuery 反查 + Capabilities 声明 + `{ticket}` 免登；**待发布**） | **框架**: .NET 10 | **依赖**: `TKWF.Federation.Abstractions`（**I2-1 契约归属——实现其 `ISsoChannel`/`IFollowChannelQuery` 契约**，Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律——纯内存态 access_token 缓存）/ **双向**（OAuth 身份获取 + 事件推送验签）/ **凭证自持**（`WeChatOptions`——Oracle P2-4，不复用 Authentication `IPlatformCredentialService`）/ **信任根一票否决**（事件推送 `msg_signature` + AES 验签——漏验 = 可伪造任意 openid = 体系崩溃）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类）

> ✅ **实施进度（2026-10-05）**：V0.1.0 **全部落地**——`WeChatApiClient` 出站客户端 + `WeChatOptions`/`WeChatChannelConfig` 配置 + `WeChatUserInfo` 响应裁剪（T3）+ **`WeChatOauthChannel`（wechat_oauth）/ `WeChatEventChannel`（wechat_event）+ `WeChatEventCrypto` / `AddWeChatFederationChannels()` 注册方法**（T4-T6）+ 测试 18 用例全绿（生产路径 + 信任根正负路径）。组件清单全部 ✅ 已实现。

> ✅ **实施进度（2026-10-17）**：V0.4.0 **全部落地**——`WechatEventReplyHandlerBase`（平台基类——**二级分发** + 图文工具 + **协议门控**）+ `WechatTemplateEventReplyHandler`（模板驱动，**消费方显式注册**）+ `WechatProbeEventReplyHandler`（探针，**开关门控默认关**）+ `WechatEventContext`/`WechatReplyPayload`/`WechatEventReplyOptions` 支撑类型；**`WeChatEventChannel` 编排放权**（`HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵——W3 绑定桶对微信可达）；`AddWeChatFederationChannels()` 增 `TKWF:Federation:WeChat:EventReply` 节（`ProbeEventReplyHandlerEnabled` 默认 false）+ Probe 注册；模板 JSON 键 `[JsonPropertyName]` 缺陷修复（ADR D8）；测试 WeChat.Tests 86 用例全绿（方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS——P0×2/P1×4/P2×7 全部吸收）。

> ✅ **实施进度（2026-10-18）**：V0.4.1 **全部落地**（方案 `docs/WeChatOperations/微信公众号运营扩展-开发方案.md` Oracle PASS WITH CONDITIONS——P0×1/P1×7 吸收）——**菜单出站 API 三件**（`WeChatApiClient.CreateMenuAsync`（`cgi-bin/menu/create` POST 全量覆盖——raw JSON 透传 + 合法 JSON 对象/button 非空校验）/ `GetCurrentSelfMenuInfoAsync`（`cgi-bin/get_current_selfmenu_info` 结构化 poco 返回 `WeChatSelfMenuInfoResult`——提交/返回命名区分）/ `DeleteMenuAsync`（`cgi-bin/menu/delete`）——自实现不引 Senparc）+ **`IWeChatCredentialResolver` 凭证解析可插拔契约**（`OptionsWeChatCredentialResolver` 缺省实现——**DI 注册 TryAddSingleton 非 ctor fallback**（Oracle P1）；凭证来源演进 Options→DB 加密→未来 KMS；两源并存 DB 权威 + Options 降级由扩展注册实现）+ **`WechatTemplateEventReplyHandler` 多图文升级**（`NewsReplyTemplateV2` `articles` 数组优先 1..8 篇 `Take(8)` 钳制 + 单图文兼容回退——存量零破坏）；测试 98 用例全绿（86 既有适配 ctor 变化 + 12 新增：菜单 API 6/凭证解析 4/template 多图文 2）。

> ✅ **实施进度（2026-10-18）**：V0.4.2 **全部落地**（Oracle P0-1 选 A——`WechatReplyPayload` 增可选 `RawXml` 字段（第三位置可选参，完整微信被动回复 XML）+ `WechatEventReplyHandlerBase.BuildReplyResult` **优先检查 RawXml 非空直接返回**（原始 XML 原样透传跳过 text/news 重组——防双重信封，SyncProxy 下游自组信封 XML 原样回复预备）；优先级 **RawXml > News > Content**，默认 null 存量零破坏）；测试 **100 用例全绿**（98 既有 + 2 新增：RawXml 原样透传优先 / 空回退——`BuildReplyResult` 正负路径）。

> ✅ **实施进度（2026-10-20）**：V0.6.0 **Iteration 2 全部落地**（方案 `docs/Federation/I2-渠道关注关系域-开发方案.md`——I2-7/I2-8）——**① EventType 语义归一（I2-7）**：`WeChatEventChannel.HandleEventAsync` 产出 `ChannelEventOutcome.Parsed(ChannelEvent)`——`subscribe`→`follow`、`subscribe`+`qrscene_` 前缀→`scan_follow`、`unsubscribe`→`unfollow`、`SCAN`→`scan_only`、`CLICK`→`menu_click`、消息类→`message`；`OccurredAt`=CreateTime→UTC；**② `wechat.` 前缀 Attributes 键（I2-7）**——`wechat.ToUserName`/`wechat.FromUserName`/`wechat.MsgType`/`wechat.CreateTime`/`wechat.EncryptType`（键清单 ADR 固化）；**③ 关注状态反查（I2-7）**——`WeChatEventChannel : IFollowChannelQuery`（`GET /cgi-bin/user/info` 的 `subscribe` 字段→`FollowStateSnapshot`）；**④ 能力声明（I2-7）**——`WeChatChannelSource.Capabilities` = `Event|Follow|FollowQuery|Menu`（认证服务号另加 `SilentAuthorize`）；**⑤ 免登闭环（I2-8）**——`WechatEventReplyHandlerBase.ResolveTicketAsync`（实例侧经 `User.Use<ILoginEntryService>()` 签票——`ChannelType="wechat_event"` + `ctx.OpenId`）+ `ReplacePlaceholders` 5 参静态重载（`{ticket}` 替换，未配置 fail-closed 空串）+ `WechatEventContext.FromChannelEvent`（编排层重建上下文，14 个 `OnXxxAsync` 零改动）；测试 **WeChat.Tests 110 用例全绿**（100→110：+10 EventType 归一/Attributes/反查/Capabilities/免登）＋ Federation 114 / WeChatOperations 143+2 受影响面全绿。

> ✅ **实施进度（commit 38037ab + 3652261）**：**模板解析链 4 级 + 免登链接基址三级链（零 seed 冒烟可测）**——① `WechatTemplateEventReplyHandler.OnDefaultAsync` 模板来源扩展为 4 级链：`Channel.Extra["news_reply"]`（per-channel：静态投影 / DB 动态权威层）→ `WechatEventReplyOptions.DefaultNewsReplyTemplate`（全局默认）→ `EnableBuiltInTestTemplate` 内置"链路测试"单图文卡片（开发期，默认 false）→ null（纯文本模式）；旧"模板缺失 → null（Oracle P0-2）"归并为「①②③均空才 null」；② **免登链接基址三级链**（`ILoginEntryService.BuildTicketUrlAsync` 增 `requestBaseUrl` 参数）：`FederationLoginEntryOptions.ChannelBaseUrls[channelId]` → `FederationEventReplyOptions.TestTicketEndpointBaseUrl` → `requestBaseUrl`（装配层请求 Host 注入 `scheme://host`——HandleEvent 写 `parameters["base_url"]` → 通道 Attributes `wechat.base_url` → `WechatEventContext.BaseUrl` → `ResolveTicketAsync` 透传）——三级均空 → `CombineUrl` 相对路径兜底（不再是 fail-closed null）；③ **`WeChatChannelConfig.NewsReplyTemplate`**（per-channel 静态图文模板，投影进 `Channel.Extra["news_reply"]`——静态层/DB 层统一解析面）；④ **内置"链路测试"卡片 link 指向测试端点**（`ILoginEntryService.BuildTestTicketUrlAsync` 新增——`{baseUrl}{RoutePrefix}/wechat/test/{channelId}/{ticket}` 匿名兑票 → 200 JSON 展示解票信息；**不经 `LoginEntry.Enabled` 门控**——零配置冒烟可直达解票展示；对照免登 URL 走 `{TicketPath}?ticket=` 消费方 SPA 落地，Platform.Web 无前端散点击 404 死链，故内置模板分支专用测试端点 URL）。

---

## 一、定位

认证中心**对外（上游）接微信 IdP** 的平台网关库（大使馆）——把微信 **OAuth 网页授权**（`wechat_oauth`）与**公众号事件推送**（`wechat_event`）两种身份获取流程，翻译成 `(channel_id, openid) → uid`，归一认证中心 Federation **token2**（ES256 联邦流）。

| 方向术语 | 说明 |
|---------|------|
| **对外（上游）** | 本库方向对外接微信 IdP——认证中心作为 **RP / OAuth Client**（对微信）消费微信身份 |
| **对内（下游）** | **SSO 是认证中心对下游的能力（token2 统一断言），不是本库的协议**——本库只管从上游"进货"身份（OAuth/事件推送），不产 SSO 断言 |
| 一句话 | 微信扩展用 OAuth/"事件推送"从上游进货身份，认证中心用 SSO 对外出货——本库 = FedSSO **上侧接入器**（IdP Adapter），非 FedSSO 本身 |

**组合矩阵**（消费方按需装配，归层模型"不是所有 AuthCenter 都需要 Federation"）：
- 只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录，微信走内部 `WeChatAuthenticationProvider`）
- **Authentication + Federation = 认证中心实例**（内部认证 + 多应用联邦 SSO）
- **Federation + `TKWF.Federation.WeChat` = 纯外部联邦登录（BYO IdP——V5 国外客户主力形态）**：本库引 Federation 扩展实现 `ISsoChannel`，经 Federation 窄适配编排身份获取方向，消费方零内部 Provider 全量

**不包含**：`/{prefix}/*` 端点映射（OAuth 代理入口 / 微信回调 / 事件推送接收——归 Federation 扩展/消费方**装配层**，本库不引 AspNetCore，只提供验签/出站逻辑类）；其他平台网关（QQ/Google/支付宝——`TKWF.Federation.{平台}` 模式复制）；`TKWF.Utility.OAuthClient` 引擎（BCL 协议，归主框架 Utility，转达框架组）。

## 二、安装与接线

### 1. 消费方引用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.WeChat\TKWF.Federation.WeChat.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- 组合式：Federation 扩展（ISsoChannel 契约 + 窄适配编排） -->
```

### 2. 注册通道（T6 已实现——库提供 `AddWeChatFederationChannels()`）

库无 Initializer（纯库）——经库提供 `AddWeChatFederationChannels()` 扩展方法（`Microsoft.Extensions.DependencyInjection` 命名空间）注册：

```csharp
// 消费方 ConfigureServices（DomainHostConfigureServices）
services.AddWeChatFederationChannels();   // 内部 TryAddEnumerableConstructible<ISsoChannel, WeChatOauthChannel/WeChatEventChannel>
```

> ✅ **T6 已实现**：注册扩展方法已提交（见上）——消费方 `services.AddWeChatFederationChannels()` 即注册通道。

### 3. 配置 `TKWF:Federation:WeChat`

```jsonc
{
  "TKWF": {
    "Federation": {
      "WeChat": {
        "Channels": [
          {
            "ChannelId": "wechat-mp-main",        // channel 实例 id（公众号 id——ISsoChannel.ChannelId 选区依据）
            "AppId": "wx0000000000000000",        // 公众号 AppId（凭证解析键——按 AppId 精确匹配）
            "AppSecret": "<AES-GCM 密文或装配注入>", // 生产永不明文进配置库（Oracle P2-4：凭证自持）
            "Token": "<事件推送验签 msg_signature 令牌>", // T5 入站信任根
            "EncodingAESKey": "<43 字符消息加解密密钥>",   // T5 AES 解密（Base64Decode 派生 32 字节 AESKey）
            "NewsReplyTemplate": {                 // V0.7.0 per-channel 静态图文模板（可空 = 走全局 DefaultNewsReplyTemplate；投影进 Channel.Extra["news_reply"]）
              "enabled": true,
              "title": "欢迎图文", "description": "...", "link": "https://...", "pic_url": "https://...", "ttl_seconds": 120
            }
          }
        ],
        "EventReply": {                           // 平台层事件回复 Options（V0.4.0；V0.7.0 增全局默认模板 + 内置测试卡片）
          "ProbeEventReplyHandlerEnabled": false, // 探针开关（默认 false——fail-safe）
          "DefaultNewsReplyTemplate": {           // V0.7.0 全局默认图文模板（模板解析链第②级；NewsReplyTemplateV2 snake_case JSON）
            "enabled": true,
            "articles": [ { "title": "...", "description": "...", "link": "...", "pic_url": "..." } ],
            "ttl_seconds": 120
          },
          "EnableBuiltInTestTemplate": false      // V0.7.0 内置测试卡片开关（默认 false——生产防护；开发 appsettings.Development.json 开 true）
        }
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:WeChat")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定（对齐 AuthCenterOptions/FederationOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。
- **凭证承载形态**（Oracle 评审点 4 P2）：开发——明文 `appsettings.json` 配置；生产——**AES-GCM 密文**或装配注入（K8s secret mount）；**AppSecret 永不明文进配置库**。channel 凭证归本库自持，与 Federation `SsoSecretKeyStore`/Authentication `PlatformCredentialService` 均独立。
- **`TKWF:Federation:WeChat:EventReply`** 节（V0.4.0——平台层 `WechatEventReplyOptions`，事件回复 handler 开关，方案 §3.5）：`ProbeEventReplyHandlerEnabled` **默认 false**（fail-safe——探针启用开关；开发期开 true 验证全分支 `PROBE:branch=...`，接入业务 handler 后保持 false 与业务 handler 互斥——Oracle P2-5）。**V0.7.0 增两成员**：`DefaultNewsReplyTemplate`（**全局默认图文模板**——模板解析链第②级：per-channel `Channel.Extra["news_reply"]` 未配时兜底；appsettings 节 `TKWF:Federation:WeChat:EventReply:DefaultNewsReplyTemplate`，NewsReplyTemplateV2 JSON 结构 snake_case：enabled/articles/title/description/link/pic_url/ttl_seconds）+ `EnableBuiltInTestTemplate`（**内置"链路测试"单图文卡片开关**，默认 **false**——模板链①②均空时开 true 回退内置卡片（标题"链路测试"、link = **测试端点** `{baseUrl}{RoutePrefix}/wechat/test/{channelId}/{ticket}`——经 `ILoginEntryService.BuildTestTicketUrlAsync` **不经 LoginEntry.Enabled 门控**签票组装；pic_url 空 = 图片不存在）——开发期**零 seed 冒烟**验证图文 + ticket 闭环，点卡片直达测试端点 → **200 JSON 展示解票信息**；**生产防护：默认 false 绝不发测试卡片给真实用户，开发配置 appsettings.Development.json**）。`TestTicketEndpointBaseUrl`/`RoutePrefix` 继续归属主包 `FederationEventReplyOptions`（测试端点是 Federation 主包编排面能力，方案 §3.5）。

### 4. 编排（Federation 白名单 + `/{prefix}/{platformId}/login` 路由——已实现通道 + 装配层编排）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]   // Federation 扩展白名单（本库自身无 Initializer）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

Federation 扩展按 channel 类型路由：`wechat_oauth` → 302 微信 authorize；`wechat_event` → 引导页（归 Federation/装配层）。

## 三、组件清单

| 组件 | 形态 | 方向 | 状态 |
|------|------|------|------|
| `WeChatApiClient` | 出站 API 客户端——`GetAccessTokenAsync`（`cgi-bin/token` **L1 缓存 + SemaphoreSlim 并发锁 + 提前 5 分钟过期刷新**）/ `GetOpenIdAsync`（`sns/oauth2/access_token` **code→openid**，一次性 code 不缓存）/ `GetUserInfoAsync`（`sns/userinfo` 响应裁剪，snsapi_base 场景降级 null） | 出站（对微信 API） | ✅ 已实现（T3） |
| `WeChatOptions` + `WeChatChannelConfig` | `TKWF:Federation:WeChat` 节——`Channels` 列表按公众号区分（ChannelId/AppId/AppSecret/Token/EncodingAESKey）；**V0.7.0 增 `NewsReplyTemplate`**（per-channel 静态图文模板 `NewsReplyTemplateV2`，可空 = 走全局 `DefaultNewsReplyTemplate`——经 `WeChatChannelSource` 投影进 `Channel.Extra["news_reply"]`，与 DB 动态权威层同一解析面）；凭证自持 | 配置 | ✅ 已实现（T3 / V0.7.0） |
| `WeChatUserInfo` | `sns/userinfo` 响应裁剪 record（OpenId/Nickname/Avatar/UnionId，对齐 Authentication 既有语义） | DTO | ✅ 已实现（T3） |
| `WeChatOauthChannel` | `ISsoChannel`（`ChannelType="wechat_oauth"`）——authorize 构造 + 回调 code→openid，OAuth 身份获取 | 对外（上游 IdP 适配） | ✅ 已实现（T4） |
| `WeChatEventChannel` + `WeChatEventCrypto` | `ISsoChannel`（`ChannelType="wechat_event"`）+ **`IFollowChannelQuery`（I2-7——`GET /cgi-bin/user/info` 反查 `subscribe` 字段）**——事件推送验签（`msg_signature` + AES 解密）+ `FromUserName`→openid + **产 `ChannelEvent`（I2-7：EventType 归一 + `wechat.` 前缀 Attributes + `OccurredAt`=CreateTime→UTC + `RawPayload` 承载明文 XML）** | 对外（上游 IdP 适配） | ✅ 已实现（T5 / I2-7） |
| `WeChatChannelSource` | `IChannelSource` 投影（`WeChatOptions.Channels` → `ChannelConfig`）+ **`Capabilities` 声明（I2-7——`Event|Follow|FollowQuery|Menu`，认证服务号另加 `SilentAuthorize`；未声明则关注域 fail-closed 静默不记录）** | 装配 | ✅ 已实现（I2-7） |
| `AddWeChatFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel>`——channel 业务参数经 Options 注入，不占 ctor `IDomainUser` 槽，ADR92 兼容；V0.2.0 起增 `AddOptions<WechatEventReplyOptions>` + `TryAddEnumerableConstructible<IEventReplyHandler, WechatProbeEventReplyHandler>`——探针无条件注册 IOptions 门控） | 装配 | ✅ 已实现（T6 / 2026-10-17） |
| `WechatEventReplyHandlerBase` | 平台基类（`DomainServiceBase, IEventReplyHandler`，`ChannelType="wechat_event"`）——**二级分发**（MsgType/Event → 强类型虚方法 OnSubscribe/OnClick/OnText...，未覆盖 → `OnDefaultAsync` 兜底 null）/ 图文工具（`BuildTextReply`/`BuildNewsReply` 多图文 + `ReadNewsTemplate`/`ReplacePlaceholders`）/ **协议门控**（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 强制禁回复——副作用可做回复被吞）；**不继承** 主包 `EventReplyHandlerBase`（两基类并列——Oracle P1-1，方案 §3.1）；`__decrypted_xml` 字段类型化解析（`WechatEventContext`/`WechatReplyPayload`——子类零 XML 感知） | WeChat 事件回复 | ✅ 已实现（2026-10-17，方案 §3.2） |
| `WechatReplyPayload` | 业务回复载荷 record（方案 §3.2——`Content` 文本回复 / `News` 图文条目（`EventReplyNewsArticle` 多图文）；**V0.4.2 增可选 `RawXml` 原始 XML 透传逃逸口（Oracle P0-1 选 A）**——非空时 `BuildReplyResult` 直接返回该原始 XML 跳过 text/news 重组（防双重信封——SyncProxy 下游自组信封 XML 原样回复预备）；优先级 **RawXml > News > Content**；全空 → null（不占 first-non-null-wins 槽，其它 handler 继续） | WeChat 事件回复 | ✅ 已实现（2026-10-17 支撑类型 / V0.4.2 增 RawXml） |
| `WechatTemplateEventReplyHandler` | 模板驱动 handler（继承平台基类，覆盖 `OnDefaultAsync`——**V0.7.0 模板解析链 4 级**：① `Channel.Extra["news_reply"]`（per-channel：静态投影 `NewsReplyTemplate` / DB 动态权威层）→ ② `WechatEventReplyOptions.DefaultNewsReplyTemplate`（全局默认）→ ③ `EnableBuiltInTestTemplate` 内置"链路测试"卡片（开发期零 seed 冒烟）→ ④ null（纯文本模式）；①②③ 均空才 null——旧"模板缺失 → null（Oracle P0-2）"归并 4 级链语义）；占位符 `{uid}/{openid}/{channel_id}/{channel_type}` + `{ticket}` + `{msg_type}/{event}/{type}`（**v0.16.0 类型占位符**——`{msg_type}`/`{event}` 独立 + 合并占位符 `{type}`（MsgType 分流：event 消息显示 Event（CLICK/VIEW/subscribe），普通消息显示 MsgType（text/image）——内置"链路测试"卡片标题 `链路测试·{type}` 体现触发源，测试反馈可区分；**I2-8 签票替换双分支**：业务模板 → 免登链接 `BuildTicketUrlAsync`（`{TicketPath}?ticket=`——基址三级链 `ChannelBaseUrls` → `TestTicketEndpointBaseUrl` → 请求 Host 推导 `ctx.BaseUrl`）；**内置测试模板 → 测试端点 URL `BuildTestTicketUrlAsync`**（`{baseUrl}{RoutePrefix}/wechat/test/{channelId}/{ticket}`——不经 `LoginEntry.Enabled` 门控，点击直达 HandleTestTicket 200 HTML 解票展示页；2026-10-26 双修复——① 免登 URL 走 SPA 落地，Platform.Web 无前端点击 404 死链 → 内置卡片改走测试端点；② 测试端点裸 JSON 微信内置浏览器白屏 → 改 HTML 展示页）；**消费方显式注册**（平台库不默认注册——Oracle P0-1） | WeChat 事件回复 | ✅ 已实现（2026-10-17 方案 §3.3 / V0.7.0 4 级链） |
| `WechatProbeEventReplyHandler` | 诊断探针 handler（继承平台基类，覆盖全部分支）——**无条件注册** + `IOptions<WechatEventReplyOptions>` 惰性判断 `ProbeEventReplyHandlerEnabled`（默认 **false** fail-safe——Oracle P1-3）；开关开 → 可回复分支 `PROBE:branch=...` 可区分回复 / 门控分支基类强制 null + ILogger；与业务 handler **互斥**（Oracle P2-5） | WeChat 事件回复 | ✅ 已实现（2026-10-17，方案 §3.3/§3.5） |

## 四、双通道契约（T4/T5 已实现）

| 通道 | 场景 | 流程 |
|------|------|------|
| `wechat_oauth`（形态 A/认证服务号） | 网页静默 OAuth 身份获取 | `/{prefix}/{platformId}/login` 按 channel 路由 → 302 微信 `authorize?appid&redirect_uri&scope=snsapi_base&state`（state 绑定会话防 CSRF，回调域名 = SSO 域名）→ 回调 code → `WeChatApiClient.GetOpenIdAsync(appId, code)` → `(channel_id, openid) → uid` → Federation 签 token2 |
| `wechat_event`（形态 B/未认证号） | 公众号事件推送入站 | 微信推送事件 → 接收端点（**验 `msg_signature` + AES 解密**；`FromUserName` = openid）→ **`WeChatEventChannel.HandleEventAsync`（V0.2.0 编排放权；V0.6.0 I2-7 产 `ChannelEventOutcome.Parsed(ChannelEvent)`——EventType 归一 + `RawPayload` 承载明文 XML，`WechatEventContext.FromChannelEvent` 重建上下文）**：验签解密成功 → 产出 `ChannelEvent`（"平台只产事实"——编排器 `EventOrchestrator` 双桶迭代：W3 绑定桶优先 → ChannelType 桶回退，微信可达 G4/G5；**记录在后 R3**）→ handler（`WechatEventReplyHandlerBase` 派生）**组装被动回复 XML 信封**（text/news 多图文——G6 信封上移平台基类）；无回复 → 端点 200 空回复兜底（5 秒超时）；异常（解密失败/格式非法）→ 返回空哨兵 `Rejected`（与"无平台回复"区分，编排短路不记录不回复——Oracle P2-3） |

- **联盟锚点配合**（T5）：`snsapi_base` 联盟服务号场景 → `FederationAnchorOpenId`（经 Abstractions `ISsoAccountLinkService.SetFederationAnchorAsync`）；商户 openid2 → `ISsoChannelMapService.LinkAsync` → `PlatformAccountMap`；反查直认经既有契约。
- **`WeChatAuthenticationProvider` 不迁移**：既有 = 认证中心**内部认证方式**（短信/微信 → token1 签发）；本库 channel = **联邦 IdP 适配**（外部身份源 → token2 联邦流）——两会话永不交叉（见 §七）。

## 五、信任根（事件推送验签——一票否决）

`msg_signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))` + AES 解密（EncodingAESKey 43 字符 `Base64Decode` 派生 32 字节 AESKey）——**漏验 = 可伪造任意 openid = 体系崩溃**（设计文档 §8.1）：

- **验签失败即拒，无降级无旁路**（一票否决）；
- 正向：合法 `msg_signature` + AES 密文通过；负向：篡改样本拒（信任根自动化测试正负路径——T5 验收 W3）；
- 各号后端为信任边界——独立密钥部署，须独立安全加固（书面声明）。

## 六、安全边界

- **数据访问红线合规**：零持久化零 Store（tkwf-extension 铁律）——纯内存态 `ConcurrentDictionary` access_token 缓存；channel 业务参数经 Options 注入不占 ctor `IDomainUser` 槽；`WeChatApiClient` 直构（非 `IDomainService`），HttpClient 经 `IHttpClientFactory`（DI 生命周期托管，避免 HttpClient 悬挂 socket）。
- **凭证自持**（Oracle P2-4）：AppSecret/Token/EncodingAESKey 从 `WeChatOptions.Channels` 按 AppId 精确匹配解析——**不经** Authentication `IPlatformCredentialService`；生产 AES-GCM 密文或装配注入，永不明文进配置库。
- **信任根一票否决**：事件推送验签（`msg_signature` + AES 解密）漏验 = 伪造任意 openid（见 §五）。
- **不引 AspNetCore**（Oracle 评审点 5）：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`）——入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类（验签逻辑收 `byte[]`/`Stream`，端点映射在外）。
- **访问令牌时效**：access_token L1 缓存提前 5 分钟过期刷新（对齐 Authentication 语义），防 stampede（SemaphoreSlim 并发锁 + 双检）。

## 七、与既有微信资产边界（三问注记）

| 资产 | 归属 | 定位 |
|------|------|------|
| `WeChatAuthenticationProvider` / `IWeChatApiClient` | Authentication **主包** | 认证中心**内部认证方式**（短信/微信 → token1 签发）——供单应用登录 |
| `WeChatOauthChannel` / `WeChatEventChannel` + 轻量 `WeChatApiClient` | **本库（`TKWF.Federation.WeChat`）** | **联邦 IdP 适配**（外部身份源 → token2 联邦流） |

- **为什么是库不是扩展**：平台网关无持久化、纯协议逻辑——按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库"判据应为**库**（AGENTS §8：`TKWF.*`=库）。每次平台适配一个扩展 = 包泛滥；`TKWF.Federation.{平台}` 单库多命名空间收敛。
- **两会话永不交叉**：装配实例按需选装（认证中心实例用联邦流；单应用业务系统用内部 Provider）——凭证各自持有、流程各自走、产物不同（token1 vs token2）；微信场景双轨并存是**边界声明**，非重复建设。
- **命名区分**：内部 Provider（`WeChatAuthenticationProvider`）vs 联邦 Channel（`WeChatOauthChannel`/`WeChatEventChannel`）——Provider 在认证内核，Channel 在联邦库。

## 八、架构决策记录

- 归层模型 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（Oracle PASS WITH CONDITIONS——四层归层模型，`TKWF.Federation.{平台}` = 大使馆/平台网关库）
- 归层实施开发方案：`docs/AuthCenter/v0.1.0-AuthCenter归层实施-开发方案.md`（Oracle PASS WITH CONDITIONS——T3-T7 拆解；Oracle P1-1 库引扩展单向依赖 / 评审点 5 不引 AspNetCore）
- 微信适配历史方案：`docs/SSO/v0.1.0-SSO.WeChat-微信公众平台适配-开发方案.md`（Oracle PASS WITH CONDITIONS——扩展形态已被归层修订为库形态，正文保留历史原貌 + 归层修订注记）
- 需求设计文档：`_TCloud/docs/协作/记录/20261005-01-认证中心设计方案.md`（v4，将归档）
- 既有 ADR（已废弃）：`ADR-SSO-模块立项与契约归属`（选项 A 独立模块——标注已废弃 + 引用归层 ADR）

## 九、版本记录

| 版本 | 内容 |
|------|------|
| （commit 38037ab，未打 tag） | **模板解析链 4 级 + 免登链接基址三级链（零 seed 冒烟可测）**：`WechatTemplateEventReplyHandler.OnDefaultAsync` 模板来源扩展为 4 级链——① `Channel.Extra["news_reply"]`（per-channel：静态投影 / DB 动态权威层）→ ② `WechatEventReplyOptions.DefaultNewsReplyTemplate`（全局默认，appsettings 节 `TKWF:Federation:WeChat:EventReply:DefaultNewsReplyTemplate`）→ ③ `EnableBuiltInTestTemplate` 内置"链路测试"单图文卡片（默认 false——开发 appsettings.Development.json 开 true，生产防护绝不发测试卡片给真实用户）→ ④ null（纯文本模式）；`ILoginEntryService.BuildTicketUrlAsync` 增 `requestBaseUrl` 参数——免登链接基址三级链（`FederationLoginEntryOptions.ChannelBaseUrls[channelId]` → `FederationEventReplyOptions.TestTicketEndpointBaseUrl` → 请求 Host 推导 `scheme://host`），三级均空 → `CombineUrl` 相对路径兜底（**不再是 fail-closed null**——微信图文 link 实际链路恒有请求 Host，完整 URL 恒可能）；`WeChatChannelConfig.NewsReplyTemplate` per-channel 静态图文模板（投影进 `Channel.Extra["news_reply"]`——静态层/DB 层统一解析面） |
| V0.6.0（2026-10-20，**待发布**） | **Iteration 2：EventType 语义归一 + `wechat.` 前缀 Attributes + 关注状态反查 + 能力声明 + `{ticket}` 免登（方案 `docs/Federation/I2-渠道关注关系域-开发方案.md` I2-7/I2-8）**——`WeChatEventChannel` 产 `ChannelEvent`（`subscribe`→`follow`/`subscribe`+`qrscene_`→`scan_follow`/`unsubscribe`→`unfollow`/`SCAN`→`scan_only`/`CLICK`→`menu_click`/消息类→`message`；`OccurredAt`=CreateTime→UTC）+ `wechat.` 前缀 Attributes 键清单（ToUserName/FromUserName/MsgType/CreateTime/EncryptType）+ **`IFollowChannelQuery`**（`cgi-bin/user/info` 反查 `subscribe`）+ **`WeChatChannelSource.Capabilities`**（`Event|Follow|FollowQuery|Menu`，认证服务号加 `SilentAuthorize`）+ **`WechatEventReplyHandlerBase.ResolveTicketAsync`**（`{ticket}` 免登——实例侧签票 `User.Use<ILoginEntryService>()`，模板 `TtlSeconds` 优先 → Options 缺省；5 参静态重载替换；`WechatEventContext.FromChannelEvent` 编排层重建上下文，`OnXxxAsync` 签名零改动）；测试 **WeChat.Tests 110 用例全绿**（100→110：+10 归一/反查/Capabilities/免登）＋ Federation 114 / WeChatOperations 143+2 受影响面全绿 |
| V0.4.2（2026-10-18） | **RawXml 逃逸口（Oracle P0-1 选 A——SyncProxy 下游 XML 原样透传预备）**：`WechatReplyPayload`
| V0.4.1（2026-10-18） | **菜单出站 API + 凭证可插拔 + template 多图文**：`WeChatApiClient` 菜单 3 API（`CreateMenuAsync`/`GetCurrentSelfMenuInfoAsync` 结构化 poco/`DeleteMenuAsync`——自实现不引 Senparc）+ `IWeChatCredentialResolver` 凭证解析可插拔契约（缺省 Options 实现 DI 注册，扩展 DB 实现两源并存）+ template 多图文升级（`NewsReplyTemplateV2` articles 数组优先 + 单图文兼容回退）；测试 86→98 用例全绿 + ADR-菜单出站API与凭证解析可插拔 增可选 `RawXml` 字段（第三位置可选参——完整微信被动回复 XML，下游自组信封）；`WechatEventReplyHandlerBase.BuildReplyResult` **优先检查 RawXml 非空直接返回**（原始 XML 原样透传跳过 text/news 重组——防双重信封）；优先级 **RawXml > News > Content**，默认 null 存量零破坏；测试 100 用例全绿（98 既有 + 2 新增：RawXml 原样透传优先/空回退——`BuildReplyResult` 正负路径）+ slnx 0 错误 |
| V0.4.0（2026-10-17） | **事件回复 Handler 三件套（方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS——P0×2/P1×4/P2×7 全部吸收 + ADR `docs/Federation/ADR/ADR-Federation-平台特定事件回复Handler基类与二级分发.md` 已实施）**：`WechatEventReplyHandlerBase`（平台基类——二级分发虚方法集（8 事件 + 7 消息 + OnDefaultAsync 兜底 null）/ 图文工具（多图文 text/news 信封）/ 协议门控（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 强制 null）/ `__decrypted_xml` 类型化解析）+ `WechatTemplateEventReplyHandler`（模板驱动，消费方显式注册 Oracle P0-1）+ `WechatProbeEventReplyHandler`（探针，无条件注册 + `ProbeEventReplyHandlerEnabled` 默认 false 门控 Oracle P1-3）+ `WechatEventContext`/`WechatReplyPayload`/`WechatEventReplyOptions`；**`WeChatEventChannel` 编排放权**（`HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵 Oracle P2-3——W3 绑定桶对微信可达）；`AddWeChatFederationChannels()` 增 EventReply Options + Probe 注册；**主包 `TestEventReplyHandler` 迁移删除**（能力迁 Template/Probe——ADR D7，承接 `NewsReplyTemplateKey` 常量 Oracle P2-2）；模板 JSON 键 `[JsonPropertyName]` 显式映射 snake_case 缺陷修复（ADR D8）；测试 WeChat.Tests 86 + Federation.Tests 79 全绿 + slnx 0 错误 |
| V0.1.0（2026-10-05） | T3 平台网关库骨架（commit 968715a）：`WeChatApiClient` 出站客户端（access_token L1 缓存 + 并发锁 + 提前 5min 刷新 + code→openid + sns/userinfo 降级）+ `WeChatOptions`/`WeChatChannelConfig`（`TKWF:Federation:WeChat` 节，凭证自持 Oracle P2-4）+ `WeChatUserInfo` record；纯库（无 Initializer 无 `[TKWFExtension]`），csproj 引 Federation 扩展实现 ISsoChannel（Oracle P1-1 单向依赖）+ `Microsoft.Extensions.Http`（不引 AspNetCore，Oracle 评审点 5）；**T4 `WeChatOauthChannel`（wechat_oauth）/ T5 `WeChatEventChannel` + `WeChatEventCrypto`（wechat_event 信任根一票否决）/ T6 `AddWeChatFederationChannels()` 注册方法 全部落地**；测试 18 用例全绿（生产路径 + 探针门面 + 信任根正负路径）；slnx 已接线 |

<!-- EOF -->
