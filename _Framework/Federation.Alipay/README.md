# TKWF.Federation.Alipay 支付宝开放平台连接器技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.1.0（N4 协议实现落地） | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `TKWF.Utility`（RSA2 签名核心 `AlipaySignUtil`/`RsaUtil`——`TKW.Framework.Utility.Cryptography`）+ `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律）/ **出站-only 精简形态**（支付宝无入站回调——组件 1/2/5/6/7/8，信任根降级以出站换取链为准）/ **RSA2 双向签名**（商户私钥签请求 + 支付宝公钥验签响应——**信任根完备/强双向签名**，N4 §3.2）/ **凭证自持**（`AlipayOptions`——Oracle P2-4，私钥/公钥 PEM 文件路径配置，生产 fail-fast 缺钥拒）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层）

> ✅ **实施进度（2026-10-08，N4）**：V0.1.0 协议实现**全部落地**——`AlipayApiClient` 出站（gateway.do RSA2 签名调用：`alipay.system.oauth.token` auth_code→user_id + `alipay.user.info.share` auth_token 顶层 + refresh 续期封装 + BuildAuthorizeUrl 授权 URL）+ `AlipayOauthChannel`（alipay_oauth：回调 code→user_id，AuthLevel=2，缺 code/redirect_uri 拒，用户级 token 不缓存）+ `AlipaySignService`（RSA2 双向签名核心——请求签名委托框架 AlipaySignUtil + **同步响应自实现验签**（librarian §5：保留原始 body 防 JSON 顺序/空格差异）+ 通知验签 rsaCheckV1 等价）+ `AddAlipayFederationChannels()` 注册方法（TryAddEnumerableConstructible ADR92 + IChannelSource 多通道行）+ 测试全绿（生产路径 + 协议结构断言 + 验签信任根正负）。

---

## 一、定位

认证中心**对外（上游）接支付宝 IdP** 的平台网关库（大使馆）——把支付宝 **OAuth 授权**（`alipay_oauth`）身份获取流程，翻译成 `(channel_id, user_id) → uid`，归一认证中心 Federation **token2**（ES256 联邦流）。

| 方向术语 | 说明 |
|---------|------|
| **对外（上游）** | 本库方向对外接支付宝 IdP——认证中心作为 **RP / OAuth Client**（对支付宝）消费支付宝身份 |
| **对内（下游）** | **SSO 是认证中心对下游的能力（token2 统一断言），不是本库的协议**——本库只管从上游"进货"身份（OAuth），不产 SSO 断言 |
| 一句话 | 支付宝扩展用 OAuth 从上游进货身份，认证中心用 SSO 对外出货——本库 = FedSSO **上侧接入器**（IdP Adapter），非 FedSSO 本身 |

**组合矩阵**（消费方按需装配，归层模型"不是所有 AuthCenter 都需要 Federation"）：
- 只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录）
- **Authentication + Federation = 认证中心实例**（内部认证 + 多应用联邦 SSO）
- **Federation + `TKWF.Federation.Alipay` = 纯外部联邦登录（BYO IdP）**：本库引 Federation 扩展实现 `ISsoChannel`，经 Federation 窄适配编排身份获取方向，消费方零内部 Provider 全量

**不包含**：`/{prefix}/*` 端点映射（OAuth 代理入口 / 支付宝回调——归 Federation 扩展/消费方**装配层**，本库不引 AspNetCore）；**H5 端内 JSAPI**（`ap.getAuthCode` 纯前端 SDK 取 code——装配层/前端）；refresh_token 长续期（归 L7 非认证面 YAGNI，认证面一次性消费）。

---

## 二、安装与接线

### 1. 消费方引用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.Alipay\TKWF.Federation.Alipay.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- 组合式：Federation 扩展（ISsoChannel 契约 + 窄适配编排） -->
```

### 2. 注册通道（T4 已实现——库提供 `AddAlipayFederationChannels()`）

库无 Initializer（纯库）——经库提供 `AddAlipayFederationChannels()` 扩展方法（`Microsoft.Extensions.DependencyInjection` 命名空间）注册：

```csharp
// 消费方 ConfigureServices（DomainHostConfigureServices）
services.AddAlipayFederationChannels();   // 内部 TryAddEnumerableConstructible<ISsoChannel, AlipayOauthChannel> + IChannelSource
```

### 3. 配置 `TKWF:Federation:Alipay`

```jsonc
{
  "TKWF": {
    "Federation": {
      "Alipay": {
        "Channels": [
          {
            "ChannelId": "alipay-main",             // channel 实例 id（ISsoChannel.ChannelId 选区依据）
            "AppId": "2021000000000000",            // 支付宝开放平台 AppId（凭证解析键）
            "PrivateKeyPath": "C:\\keys\\alipay-app-private.pem",     // 商户私钥 PEM（PKCS8——签请求 RSA2，chmod 600）
            "AlipayPublicKeyPath": "C:\\keys\\alipay-public.pem",    // 支付宝公钥 PEM（验签响应/通知）
            "EnableMobile": false                   // 手机号可选增强（企业资质 + 申请 mobile 字段审核；默认 false）
          }
        ]
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:Alipay")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定；亦可在消费方 ConfigureExtensions 编程覆盖（`AddAlipayFederationChannels(x => x.Channels = ...)`）。
- **凭证承载形态**（Oracle P2-4）：支付宝无对称 AppSecret——**RSA 私钥/公钥 PEM 文件路径**配置（**私钥文件 chmod 600 不进代码库**；生产 fail-fast 缺钥拒——F7）。支持 KMS/装配注入路径（N4 §六风险）。
- **open_id 配置启用**（N4 §二）：接入前置须在支付宝控制台启用 openid 配置（灰度阶段）——本库统一读 `open_id` 字段（USER_ID→OPENID_AND_USERID→OPEN_ID 过渡兼容）。

### 4. 编排（Federation 白名单 + `/{prefix}/{platformId}/login` 路由——已实现通道 + 装配层编排）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]   // Federation 扩展白名单（本库自身无 Initializer）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

Federation 扩展按 channel 类型路由：`alipay_oauth` → 装配层构造支付宝 authorize URL（`AlipayApiClient.BuildAuthorizeUrl` 或装配层直拼 `openauth.alipay.com/oauth2/publicAppAuthorize.htm`——**authorize 构造归装配层**：state 生成 + 会话绑定 + 回调校验全部归装配层，通道不感知 state——Oracle P1-1）→ 回调 `/{prefix}/{platformId}/oauth/{channelId}/callback?auth_code=&state=`（v0.3.0 多通道带 channelId 段精确选区；无前缀降级默认通道）→ 装配层把 auth_code 映射为 context "code" 键 + redirect_uri 注入 → `AlipayOauthChannel.AuthenticateAsync`。

**redirect_uri 校验（N4 P1-2 防开放重定向）**：支付宝 code→token 换取**不经** redirect_uri（顶层参数无此字段——与 QQ 不同）；redirect_uri 须与支付宝控制台回调配置一致（防开放重定向）——装配层/消费方在授权步骤保证，通道仅校验 context 注入的 redirect_uri 存在。

---

## 三、组件清单

| 组件 | 形态 | 状态 |
|------|------|------|
| `AlipayApiClient` | 出站 API 客户端——`GetAccessTokenAsync`（`alipay.system.oauth.token` **auth_code→user_id/access_token**，**无 biz_content/code 顶层** + **RSA2 双向签名**（先验签后解析）+ 用户级 token **不缓存** P1-5）/ `GetUserInfoAsync`（`alipay.user.info.share` **auth_token 顶层公共参数**，snake_case 响应裁剪）/ `RefreshAccessTokenAsync`（grant_type=refresh_token 续期封装——保留）/ `BuildAuthorizeUrl`（openauth 授权 URL 构造——装配层拼接授权链） | ✅ 已实现（T2） |
| `AlipayOptions` + `AlipayChannelConfig` | `TKWF:Federation:Alipay` 节——`Channels` 列表（ChannelId/AppId/**PrivateKeyPath/AlipayPublicKeyPath**（PEM 文件路径）/EnableMobile）；凭证自持（Oracle P2-4） | ✅ 已实现（T1） |
| `AlipayUserInfo` + `AlipayTokenResult` | `alipay.user.info.share` 响应裁剪 record（UserId/OpenId/Nickname/Avatar/Province/City/Gender）+ token 换取结果（UserId/OpenId/AccessToken/ExpiresInSeconds/RefreshToken） | ✅ 已实现（T2） |
| `AlipaySignService` | **RSA2 双向签名核心**——`BuildRequestSignature`（请求签名委托框架 `AlipaySignUtil.BuildSignature`）/ `VerifyResponse`（**网关同步响应自实现验签**——保留原始 body 防 JSON 顺序/空格/引号差异，librarian §5）/ `VerifyNotification`（dict 扁平 = rsaCheckV1 等价，保留供未来） | ✅ 已实现（T2） |
| `AlipayOauthChannel` | `ISsoChannel`（`ChannelType="alipay_oauth"`）——回调 code→user_id（缺 code 拒 / 缺 redirect_uri 拒 / API 错误/验签失败负路径），AuthLevel=2，不感知 state（P1-1），external_uid 恒 = user_id（N4 §二） | ✅ 已实现（T3） |
| `AlipayChannelSource` | **多通道静态来源（组件 8.5）**——AlipayOptions.Channels 投影统一 `ChannelConfig`（AppId 公共列 + Extra{PrivateKeyPath, AlipayPublicKeyPath, EnableMobile}；**AppSecret=null**——支付宝无对称密钥） | ✅ 已实现（T3.5） |
| `AddAlipayFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel, AlipayOauthChannel>`——ADR92 集合版守卫工厂 + `TryAddEnumerable(IChannelSource)` 多通道行 + AddHttpClient + AlipaySignService 单例） | ✅ 已实现（T4） |

## 四、通道契约

| 通道 | 场景 | 流程 |
|------|------|------|
| `alipay_oauth`（出站-only） | 支付宝授权身份获取（跳转/扫码/H5 三形态统一收敛） | `/{prefix}/{platformId}/login` 按 channel 路由 → 装配层构造 authorize URL（**state 必填**，CSRF 防护，归装配层）→ 回调 auth_code（+state）→ `AlipayOauthChannel.AuthenticateAsync`：缺 code 拒 / 缺 redirect_uri 拒 → `AlipayApiClient.GetAccessTokenAsync`（RSA2 签名 → 验签 → user_id）→ `(channel_id, user_id) → uid` → Federation 签 token2 |

- **三种授权形态边界（Oracle P1-1 定案）**：跳转（装配层拼 `publicAppAuthorize.htm` 302）/ 扫码（二维码 = 授权页二维码；扫码页专用 `alipay.user.info.auth` 生成 `qr_code_url` 归装配层签名调用编排）/ H5 JSAPI（`ap.getAuthCode` 纯前端 SDK）——在 `AuthenticateAsync` 端点处**全部收敛到 code→user_id 同一处理路径**（channel 无分支）。
- **unionid = 应用分组（N4 §二）**：支付宝 unionid 为应用分组维度（须绑定分组）——跨应用对齐可选增强；user_id/open_id 为应用维度唯一（external_uid 稳定映射键）。双锚点写入（user_id 写 PlatformAccountMap + unionid 经 `ISsoAccountLinkService.SetFederationAnchorAsync` 写联盟锚点）归装配层/编排层（对齐 N2 §3.1 选项 B）。

## 五、信任根（强双向签名——出站换取链完备）

支付宝**无入站回调**（无事件推送）——P5 一票否决**不适用**（非违规）。trust root = **出站强双向签名换取链**（N4 §3.2 信任根完备）：

- **请求签名**：商户私钥 RSA2（SHA256WithRSA）签 gateway.do 请求（公共参数 + 顶层业务参数——剔 sign/空值 → key 首字符 ASCII 升序 → `key=value&` → SHA256WithRSA → Base64）；
- **响应验签**：支付宝公钥验网关同步响应（**自实现保留原始 body**——`AlipaySignService.VerifyResponse`，librarian §5）；验签失败即拒（FAIL_SIGNATURE——测试信任根负路径）；
- **auth_code 一次性**（3min~24h 动态，官方明确）；
- **state 强校验**（CSRF——归装配层生成 + 回调校验，N4 §3.2 强制要求）；
- **redirect_uri 一致性**（N4 P1-2 防开放重定向——与支付宝控制台回调配置一致，归装配层保证）。

无可伪造 user_id 的入站面——安全重心在"RSA2 双向签名 + code 一次性 + state"。

## 六、安全边界

- **数据访问红线合规**：零持久化零 Store（tkwf-extension 铁律）；`AlipayApiClient`/`AlipaySignService` 直构（非 `IDomainService`），HttpClient 经 `IHttpClientFactory`（DI 生命周期托管）；`AlipayOauthChannel` 继承 `DomainServiceBase` + `[DiContractIgnore]`（运行时手写注册豁免 DI001）+ `AddConstructibleService` 守卫工厂（集合版 `TryAddEnumerableConstructible` ADR92 帧内供给）。
- **凭证自持**（Oracle P2-4）：商户私钥/支付宝公钥 **PEM 文件路径**从 `AlipayOptions.Channels` 按 AppId 精确匹配解析——**不经** Authentication `IPlatformCredentialService`；私钥文件 chmod 600 不进代码库；生产 fail-fast 缺钥拒（F7）。
- **RSA2 双向签名**（N4 §3.2）：请求签名（商户私钥）+ 响应/通知验签（支付宝公钥）——RSA ≥ 2048 强度守卫由框架 `RsaUtil` 承担（fail-fast）；同步响应验签自实现（保留原始 body 防 JSON 序列化差异——librarian §5）；验签失败一票否决（FAIL_SIGNATURE 拒信任）。
- **用户级 token 不缓存不续期**（对齐 QQ N3 P1-5）：支付宝 token = 用户级 authorization_code grant——联邦认证面一次性消费；refresh_token 长续期归 L7 非认证面 YAGNI。
- **不引 AspNetCore**（Oracle 评审点 5）：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`）——入站端点归 Federation 扩展/消费方装配层。

## 七、与既有资产边界

| 资产 | 归属 | 定位 |
|------|------|------|
| `AlipayOauthChannel` / 其他平台库 | `TKWF.Federation.{平台}` | 各平台 IdP 适配（按 N1 模板复制）——支付宝为出站-only + RSA2 双向签名形态 |

- **为什么是库不是扩展**：平台网关无持久化、纯协议逻辑——按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库"判据应为**库**（AGENTS §8：`TKWF.*`=库）。
- **集合并列**：`alipay_oauth` 通道与其他平台通道并列进 `IEnumerable<ISsoChannel>`（Federation 拥有契约但零实现）——未装配库 = 空集合"未注册通道自然跳过"。

## 八、架构决策记录

- 归层模型 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（Oracle PASS WITH CONDITIONS——四层归层模型，`TKWF.Federation.{平台}` = 大使馆/平台网关库）
- N4 开发方案：`docs/Federation/N4-支付宝平台网关-开发方案.md`（v1.1.0——Oracle PASS WITH CONDITIONS：P1 五项 + P2 三项落实；同步多通道联邦 v0.3.0 模板：ChannelConfig 并 Options 同文件 + AlipayChannelSource 组件 8.5 + ctor 收 ChannelConfig? + 回调路由 /sso/oauth/{channelId}/callback + T3.5）
- N1 平台网关通用模板：`docs/Federation/平台网关模板-开发指南.md`（权威固化——8 组件 + 出站-only 精简形态 + 复制清单）
- 协议事实核实：librarian 官方文档（gateway.do RSA2 双向签名 / auth_code 时效 / auth_token 顶层字段 / 同步响应自实现验签 / sub_code 优先）

## 九、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-08） | N4 协议实现落地：`AlipayApiClient`（gateway.do RSA2 签名调用——oauth.token 无 biz_content/auth_code 顶层 + user.info.share auth_token 顶层 + refresh 续期封装 + BuildAuthorizeUrl）/ `AlipaySignService`（双向签名——请求签名委托框架 AlipaySignUtil + **同步响应自实现验签** + 通知验签 rsaCheckV1 等价）/ `AlipayOauthChannel`（alipay_oauth：code→user_id + 缺 code/redirect_uri 拒 + AuthLevel=2 + 用户级 token 不缓存）/ `AlipayChannelSource`（组件 8.5 多通道投影）/ `AlipayUserInfo`/`AlipayTokenResult` 响应裁剪 / `AddAlipayFederationChannels()` 注册 / 测试全绿（生产路径通道正负 + 协议结构断言 + 验签信任根正负） |

<!-- EOF -->
