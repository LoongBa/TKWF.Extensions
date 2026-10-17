# TKWF.Federation.WeChat 微信公众平台连接器技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.4.2（2026-10-18——RawXml 逃逸口（P0-1 选 A）） | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律——纯内存态 access_token 缓存）/ **双向**（OAuth 身份获取 + 事件推送验签）/ **凭证自持**（`WeChatOptions`——Oracle P2-4，不复用 Authentication `IPlatformCredentialService`）/ **信任根一票否决**（事件推送 `msg_signature` + AES 验签——漏验 = 可伪造任意 openid = 体系崩溃）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层，库只提供验签逻辑类）

> ✅ **实施进度（2026-10-05）**：V0.1.0 **全部落地**——`WeChatApiClient` 出站客户端 + `WeChatOptions`/`WeChatChannelConfig` 配置 + `WeChatUserInfo` 响应裁剪（T3）+ **`WeChatOauthChannel`（wechat_oauth）/ `WeChatEventChannel`（wechat_event）+ `WeChatEventCrypto` / `AddWeChatFederationChannels()` 注册方法**（T4-T6）+ 测试 18 用例全绿（生产路径 + 信任根正负路径）。组件清单全部 ✅ 已实现。

> ✅ **实施进度（2026-10-17）**：V0.4.0 **全部落地**——`WechatEventReplyHandlerBase`（平台基类——**二级分发** + 图文工具 + **协议门控**）+ `WechatTemplateEventReplyHandler`（模板驱动，**消费方显式注册**）+ `WechatProbeEventReplyHandler`（探针，**开关门控默认关**）+ `WechatEventContext`/`WechatReplyPayload`/`WechatEventReplyOptions` 支撑类型；**`WeChatEventChannel` 编排放权**（`HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵——W3 绑定桶对微信可达）；`AddWeChatFederationChannels()` 增 `TKWF:Federation:WeChat:EventReply` 节（`ProbeEventReplyHandlerEnabled` 默认 false）+ Probe 注册；模板 JSON 键 `[JsonPropertyName]` 缺陷修复（ADR D8）；测试 WeChat.Tests 86 用例全绿（方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS——P0×2/P1×4/P2×7 全部吸收）。

> ✅ **实施进度（2026-10-18）**：V0.4.1 **全部落地**（方案 `docs/WeChatOperations/微信公众号运营扩展-开发方案.md` Oracle PASS WITH CONDITIONS——P0×1/P1×7 吸收）——**菜单出站 API 三件**（`WeChatApiClient.CreateMenuAsync`（`cgi-bin/menu/create` POST 全量覆盖——raw JSON 透传 + 合法 JSON 对象/button 非空校验）/ `GetCurrentSelfMenuInfoAsync`（`cgi-bin/get_current_selfmenu_info` 结构化 poco 返回 `WeChatSelfMenuInfoResult`——提交/返回命名区分）/ `DeleteMenuAsync`（`cgi-bin/menu/delete`）——自实现不引 Senparc）+ **`IWeChatCredentialResolver` 凭证解析可插拔契约**（`OptionsWeChatCredentialResolver` 缺省实现——**DI 注册 TryAddSingleton 非 ctor fallback**（Oracle P1）；凭证来源演进 Options→DB 加密→未来 KMS；两源并存 DB 权威 + Options 降级由扩展注册实现）+ **`WechatTemplateEventReplyHandler` 多图文升级**（`NewsReplyTemplateV2` `articles` 数组优先 1..8 篇 `Take(8)` 钳制 + 单图文兼容回退——存量零破坏）；测试 98 用例全绿（86 既有适配 ctor 变化 + 12 新增：菜单 API 6/凭证解析 4/template 多图文 2）。

> ✅ **实施进度（2026-10-18）**：V0.4.2 **全部落地**（Oracle P0-1 选 A——`WechatReplyPayload` 增可选 `RawXml` 字段（第三位置可选参，完整微信被动回复 XML）+ `WechatEventReplyHandlerBase.BuildReplyResult` **优先检查 RawXml 非空直接返回**（原始 XML 原样透传跳过 text/news 重组——防双重信封，SyncProxy 下游自组信封 XML 原样回复预备）；优先级 **RawXml > News > Content**，默认 null 存量零破坏）；测试 **100 用例全绿**（98 既有 + 2 新增：RawXml 原样透传优先 / 空回退——`BuildReplyResult` 正负路径）。

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
            "EncodingAESKey": "<43 字符消息加解密密钥>"    // T5 AES 解密（Base64Decode 派生 32 字节 AESKey）
          }
        ]
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:WeChat")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定（对齐 AuthCenterOptions/FederationOptions 先例）；亦可在消费方 ConfigureExtensions 编程覆盖。
- **凭证承载形态**（Oracle 评审点 4 P2）：开发——明文 `appsettings.json` 配置；生产——**AES-GCM 密文**或装配注入（K8s secret mount）；**AppSecret 永不明文进配置库**。channel 凭证归本库自持，与 Federation `SsoSecretKeyStore`/Authentication `PlatformCredentialService` 均独立。
- **`TKWF:Federation:WeChat:EventReply`** 节（V0.4.0——平台层 `WechatEventReplyOptions`，事件回复 handler 开关，方案 §3.5）：`ProbeEventReplyHandlerEnabled` **默认 false**（fail-safe——探针启用开关；开发期开 true 验证全分支 `PROBE:branch=...`，接入业务 handler 后保持 false 与业务 handler 互斥——Oracle P2-5）。`TestTicketEndpointBaseUrl`/`RoutePrefix` 继续归属主包 `FederationEventReplyOptions`（测试端点是 Federation 主包编排面能力，方案 §3.5）。

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
| `WeChatOptions` + `WeChatChannelConfig` | `TKWF:Federation:WeChat` 节——`Channels` 列表按公众号区分（ChannelId/AppId/AppSecret/Token/EncodingAESKey）；凭证自持 | 配置 | ✅ 已实现（T3） |
| `WeChatUserInfo` | `sns/userinfo` 响应裁剪 record（OpenId/Nickname/Avatar/UnionId，对齐 Authentication 既有语义） | DTO | ✅ 已实现（T3） |
| `WeChatOauthChannel` | `ISsoChannel`（`ChannelType="wechat_oauth"`）——authorize 构造 + 回调 code→openid，OAuth 身份获取 | 对外（上游 IdP 适配） | ✅ 已实现（T4） |
| `WeChatEventChannel` + `WeChatEventCrypto` | `ISsoChannel`（`ChannelType="wechat_event"`）——事件推送验签（`msg_signature` + AES 解密）+ `FromUserName`→openid | 对外（上游 IdP 适配） | ✅ 已实现（T5） |
| `AddWeChatFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel>`——channel 业务参数经 Options 注入，不占 ctor `IDomainUser` 槽，ADR92 兼容；V0.2.0 起增 `AddOptions<WechatEventReplyOptions>` + `TryAddEnumerableConstructible<IEventReplyHandler, WechatProbeEventReplyHandler>`——探针无条件注册 IOptions 门控） | 装配 | ✅ 已实现（T6 / 2026-10-17） |
| `WechatEventReplyHandlerBase` | 平台基类（`DomainServiceBase, IEventReplyHandler`，`ChannelType="wechat_event"`）——**二级分发**（MsgType/Event → 强类型虚方法 OnSubscribe/OnClick/OnText...，未覆盖 → `OnDefaultAsync` 兜底 null）/ 图文工具（`BuildTextReply`/`BuildNewsReply` 多图文 + `ReadNewsTemplate`/`ReplacePlaceholders`）/ **协议门控**（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 强制禁回复——副作用可做回复被吞）；**不继承** 主包 `EventReplyHandlerBase`（两基类并列——Oracle P1-1，方案 §3.1）；`__decrypted_xml` 字段类型化解析（`WechatEventContext`/`WechatReplyPayload`——子类零 XML 感知） | WeChat 事件回复 | ✅ 已实现（2026-10-17，方案 §3.2） |
| `WechatReplyPayload` | 业务回复载荷 record（方案 §3.2——`Content` 文本回复 / `News` 图文条目（`EventReplyNewsArticle` 多图文）；**V0.4.2 增可选 `RawXml` 原始 XML 透传逃逸口（Oracle P0-1 选 A）**——非空时 `BuildReplyResult` 直接返回该原始 XML 跳过 text/news 重组（防双重信封——SyncProxy 下游自组信封 XML 原样回复预备）；优先级 **RawXml > News > Content**；全空 → null（不占 first-non-null-wins 槽，其它 handler 继续） | WeChat 事件回复 | ✅ 已实现（2026-10-17 支撑类型 / V0.4.2 增 RawXml） |
| `WechatTemplateEventReplyHandler` | 模板驱动 handler（继承平台基类，覆盖 `OnDefaultAsync` 读 `Channel.Extra["news_reply"]` 模板——占位符 `{uid}/{openid}/{channel_id}/...`，纯图文零签票耦合）；**消费方显式注册**（平台库不默认注册——Oracle P0-1）；模板缺失/损坏/`Enabled=false` → **返回 null**（不回复不干预——Oracle P0-2） | WeChat 事件回复 | ✅ 已实现（2026-10-17，方案 §3.3） |
| `WechatProbeEventReplyHandler` | 诊断探针 handler（继承平台基类，覆盖全部分支）——**无条件注册** + `IOptions<WechatEventReplyOptions>` 惰性判断 `ProbeEventReplyHandlerEnabled`（默认 **false** fail-safe——Oracle P1-3）；开关开 → 可回复分支 `PROBE:branch=...` 可区分回复 / 门控分支基类强制 null + ILogger；与业务 handler **互斥**（Oracle P2-5） | WeChat 事件回复 | ✅ 已实现（2026-10-17，方案 §3.3/§3.5） |

## 四、双通道契约（T4/T5 已实现）

| 通道 | 场景 | 流程 |
|------|------|------|
| `wechat_oauth`（形态 A/认证服务号） | 网页静默 OAuth 身份获取 | `/{prefix}/{platformId}/login` 按 channel 路由 → 302 微信 `authorize?appid&redirect_uri&scope=snsapi_base&state`（state 绑定会话防 CSRF，回调域名 = SSO 域名）→ 回调 code → `WeChatApiClient.GetOpenIdAsync(appId, code)` → `(channel_id, openid) → uid` → Federation 签 token2 |
| `wechat_event`（形态 B/未认证号） | 公众号事件推送入站 | 微信推送事件 → 接收端点（**验 `msg_signature` + AES 解密**；`FromUserName` = openid）→ **`WeChatEventChannel.HandleEventAsync`（V0.2.0 编排放权）**：验签解密成功 → 富化 `Parameters` + 存 **`__decrypted_xml`** 明文 XML（Oracle P1-4——平台基类 Parse 数据源）→ **返回 null**（"无平台回复"——编排器 `EventOrchestrator` 双桶迭代：W3 绑定桶优先 → ChannelType 桶回退，微信可达 G4/G5）→ handler（`WechatEventReplyHandlerBase` 派生）**组装被动回复 XML 信封**（text/news 多图文——G6 信封上移平台基类）；无回复 → 端点 200 空回复兜底（5 秒超时）；异常（解密失败/格式非法）→ 返回空哨兵（与"无平台回复"区分，不触发 handler 双桶——Oracle P2-3） |

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
| V0.4.2（2026-10-18） | **RawXml 逃逸口（Oracle P0-1 选 A——SyncProxy 下游 XML 原样透传预备）**：`WechatReplyPayload` 增可选 `RawXml` 字段（第三位置可选参——完整微信被动回复 XML，下游自组信封）；`WechatEventReplyHandlerBase.BuildReplyResult` **优先检查 RawXml 非空直接返回**（原始 XML 原样透传跳过 text/news 重组——防双重信封）；优先级 **RawXml > News > Content**，默认 null 存量零破坏；测试 100 用例全绿（98 既有 + 2 新增：RawXml 原样透传优先/空回退——`BuildReplyResult` 正负路径）+ slnx 0 错误 |
| V0.4.0（2026-10-17） | **事件回复 Handler 三件套（方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md` Oracle PASS WITH CONDITIONS——P0×2/P1×4/P2×7 全部吸收 + ADR `docs/Federation/ADR/ADR-Federation-平台特定事件回复Handler基类与二级分发.md` 已实施）**：`WechatEventReplyHandlerBase`（平台基类——二级分发虚方法集（8 事件 + 7 消息 + OnDefaultAsync 兜底 null）/ 图文工具（多图文 text/news 信封）/ 协议门控（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 强制 null）/ `__decrypted_xml` 类型化解析）+ `WechatTemplateEventReplyHandler`（模板驱动，消费方显式注册 Oracle P0-1）+ `WechatProbeEventReplyHandler`（探针，无条件注册 + `ProbeEventReplyHandlerEnabled` 默认 false 门控 Oracle P1-3）+ `WechatEventContext`/`WechatReplyPayload`/`WechatEventReplyOptions`；**`WeChatEventChannel` 编排放权**（`HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵 Oracle P2-3——W3 绑定桶对微信可达）；`AddWeChatFederationChannels()` 增 EventReply Options + Probe 注册；**主包 `TestEventReplyHandler` 迁移删除**（能力迁 Template/Probe——ADR D7，承接 `NewsReplyTemplateKey` 常量 Oracle P2-2）；模板 JSON 键 `[JsonPropertyName]` 显式映射 snake_case 缺陷修复（ADR D8）；测试 WeChat.Tests 86 + Federation.Tests 79 全绿 + slnx 0 错误 |
| V0.1.0（2026-10-05） | T3 平台网关库骨架（commit 968715a）：`WeChatApiClient` 出站客户端（access_token L1 缓存 + 并发锁 + 提前 5min 刷新 + code→openid + sns/userinfo 降级）+ `WeChatOptions`/`WeChatChannelConfig`（`TKWF:Federation:WeChat` 节，凭证自持 Oracle P2-4）+ `WeChatUserInfo` record；纯库（无 Initializer 无 `[TKWFExtension]`），csproj 引 Federation 扩展实现 ISsoChannel（Oracle P1-1 单向依赖）+ `Microsoft.Extensions.Http`（不引 AspNetCore，Oracle 评审点 5）；**T4 `WeChatOauthChannel`（wechat_oauth）/ T5 `WeChatEventChannel` + `WeChatEventCrypto`（wechat_event 信任根一票否决）/ T6 `AddWeChatFederationChannels()` 注册方法 全部落地**；测试 18 用例全绿（生产路径 + 探针门面 + 信任根正负路径）；slnx 已接线 |

<!-- EOF -->
