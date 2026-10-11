# TKWF.Federation.QQ QQ 互联连接器技术规范

**状态**: 平台网关库 (Platform Gateway Library——大使馆/纯库) | **版本**: V0.1.0（N3 T2-T6 落地；preview 0.1.1-preview.0.1 为 N1 骨架） | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**——AGENTS §8：`TKWF.Ext.*`=扩展（有装配/持久化）/ `TKWF.*`=库（纯逻辑））/ **零持久化零 Store**（tkwf-extension 铁律）/ **出站-only 精简形态**（QQ 无事件推送——组件 1/2/5/6/7/8，信任根降级以出站换取链为准）/ **凭证自持**（`QqOptions`——Oracle P2-4，复不复用 Authentication `IPlatformCredentialService`）/ **trust root 降级**（QQ 无回调验签——安全重心 = 出站 code 一次性 + state CSRF + client_secret 换取 + redirect_uri 一致性）/ **不引 AspNetCore**（Oracle 评审点 5——库纯逻辑 + HttpClient，入站端点归 Federation 扩展/消费方装配层）

> ✅ **实施进度（2026-10-06，N3）**：V0.1.0 协议实现**全部落地**——`QqApiClient` 出站（code→access_token→/me→openid 一次性链 + redirect_uri 一致性 + fmt=json + JSONP 剥壳兜底 + 用户级 token 不缓存 P1-5）+ `QqOauthChannel`（qq_oauth：回调 code→openid，AuthLevel=2，不感知 state P1-3）+ `AddQqFederationChannels()` 注册方法（TryAddEnumerableConstructible ADR92）+ 测试 **16 用例全绿**（生产路径 + redirect_uri 一致性正负 + unionid 开关 + 协议结构断言）。组件清单全部 ✅ 已实现。

---

## 一、定位

认证中心**对外（上游）接 QQ IdP** 的平台网关库（大使馆）——把 QQ **OAuth 网页授权**（`qq_oauth`）身份获取流程，翻译成 `(channel_id, openid) → uid`，归一认证中心 Federation **token2**（ES256 联邦流）。

| 方向术语 | 说明 |
|---------|------|
| **对外（上游）** | 本库方向对外接 QQ IdP——认证中心作为 **RP / OAuth Client**（对 QQ）消费 QQ 身份 |
| **对内（下游）** | **SSO 是认证中心对下游的能力（token2 统一断言），不是本库的协议**——本库只管从上游"进货"身份（OAuth），不产 SSO 断言 |
| 一句话 | QQ 扩展用 OAuth 从上游进货身份，认证中心用 SSO 对外出货——本库 = FedSSO **上侧接入器**（IdP Adapter），非 FedSSO 本身 |

**组合矩阵**（消费方按需装配，归层模型"不是所有 AuthCenter 都需要 Federation"）：
- 只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录）
- **Authentication + Federation = 认证中心实例**（内部认证 + 多应用联邦 SSO）
- **Federation + `TKWF.Federation.QQ` = 纯外部联邦登录（BYO IdP——V5 国外客户主力形态）**：本库引 Federation 扩展实现 `ISsoChannel`，经 Federation 窄适配编排身份获取方向，消费方零内部 Provider 全量

**不包含**：`/{prefix}/*` 端点映射（OAuth 代理入口 / QQ 回调——归 Federation 扩展/消费方**装配层**，本库不引 AspNetCore）；**QQ 扫码**（官方仅跳转授权，无扫码；APP 内嵌 H5 不支持）；refresh_token 续票（QQ refresh_token 一次有效续票 3 个月——归 L7 非认证面 YAGNI，认证面一次性消费）。

---

## 二、安装与接线

### 1. 消费方引用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.QQ\TKWF.Federation.QQ.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- 组合式：Federation 扩展（ISsoChannel 契约 + 窄适配编排） -->
```

### 2. 注册通道（T4 已实现——库提供 `AddQqFederationChannels()`）

库无 Initializer（纯库）——经库提供 `AddQqFederationChannels()` 扩展方法（`Microsoft.Extensions.DependencyInjection` 命名空间）注册：

```csharp
// 消费方 ConfigureServices（DomainHostConfigureServices）
services.AddQqFederationChannels();   // 内部 TryAddEnumerableConstructible<ISsoChannel, QqOauthChannel>
```

### 3. 配置 `TKWF:Federation:QQ`

```jsonc
{
  "TKWF": {
    "Federation": {
      "QQ": {
        "Channels": [
          {
            "ChannelId": "qq-main",                 // channel 实例 id（ISsoChannel.ChannelId 选区依据）
            "AppId": "101000000",                    // QQ 互联 AppId（client_id——凭证解析键）
            "AppSecret": "<AES-GCM 密文或装配注入>", // QQ 互联 AppSecret（AppKey/ClientSecret 同物——生产永不明文进配置库，Oracle P2-4）
            "EnableUnionId": false                   // unionid 可选增强开关（需官网自助申请，≤60 应用上限；默认 false 仅 openid）
          }
        ]
      }
    }
  }
}
```

- **`[Options("TKWF:Federation:QQ")]`**：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定；亦可在消费方 ConfigureExtensions 编程覆盖（`AddQqFederationChannels(x => x.Channels = ...)`）。
- **凭证承载形态**（Oracle 评审点 4 P2）：开发——明文配置；生产——**AES-GCM 密文**或装配注入（K8s secret mount）；**AppSecret 永不明文进配置库**。channel 凭证归本库自持（P1-1：QQ 仅 AppId+AppKey 两凭证，AppKey=ClientSecret 同物，命名对齐 WeChat 先例 AppId+AppSecret）。

### 4. 编排（Federation 白名单 + `/{prefix}/{platformId}/login` 路由——已实现通道 + 装配层编排）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]   // Federation 扩展白名单（本库自身无 Initializer）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

Federation 扩展按 channel 类型路由：`qq_oauth` → 302 QQ authorize（graph.qq.com/oauth2.0/authorize——**authorize 构造归装配层**：state 生成 + 会话绑定 + 回调校验全部归装配层，渠道不感知 state——N3 P1-3）。

**redirect_uri 一致性（N3 P1-4）**：授权时装配层以 `redirect_uri` 构造 authorize URL；回调时须将同一 `redirect_uri` 注入 `SsoChannelAuthContext.Parameters["redirect_uri"]`（本通道 code 换取时显式携带出站，QQ 服务端比对——OAuth RFC 6749 §4.1.3 防 code 窃取换 token）。

---

## 三、组件清单

| 组件 | 形态 | 状态 |
|------|------|------|
| `QqApiClient` | 出站 API 客户端——`GetAccessTokenAsync`（`oauth2.0/token` **code→access_token**，fmt=json 强制 + JSONP 剥壳兜底 + **redirect_uri 一致性出站校验** P1-4，用户级 token **不缓存** P1-5）/ `GetMeAsync`（`oauth2.0/me` openid+可选 unionid）/ `GetUserInfoAsync`（`user/get_user_info` 裁剪，ret/msg 错误模型，失败降级 null） | ✅ 已实现（T2） |
| `QqOptions` + `QqChannelConfig` | `TKWF:Federation:QQ` 节——`Channels` 列表（ChannelId/AppId/AppSecret/EnableUnionId）；凭证自持（P1-1） | ✅ 已实现（T1，N1 骨架） |
| `QqUserInfo` + `QqMeResult` | `get_user_info` 响应裁剪 record（OpenId/Nickname/Avatar/Gender/Province/City）+ `/me` 换取结果（OpenId/UnionId） | ✅ 已实现（T2） |
| `QqOauthChannel` | `ISsoChannel`（`ChannelType="qq_oauth"`）——回调 code→openid（code 缺拒 / redirect_uri 缺拒 / API 错误负路径），AuthLevel=2，不感知 state（P1-3），external_uid 恒 = openid（P1-2） | ✅ 已实现（T3） |
| `AddQqFederationChannels()` | DI 注册扩展方法（`TryAddEnumerableConstructible<ISsoChannel, QqOauthChannel>`——ADR92 集合版守卫工厂，channel 业务参数经 Options 注入不占 ctor `IDomainUser` 槽） | ✅ 已实现（T4，N1 骨架） |

## 四、通道契约

| 通道 | 场景 | 流程 |
|------|------|------|
| `qq_oauth`（出站-only） | 网页跳转授权身份获取 | `/{prefix}/{platformId}/login` 按 channel 路由 → 302 QQ `authorize?client_id&redirect_uri&state&scope&display`（**state 必填**，CSRF 防护，归装配层）→ 回调 code（+state，**恒传 redirect_uri 进 context 供换取一致性比对**）→ `QqOauthChannel.AuthenticateAsync`：code 缺拒 / redirect_uri 缺拒 → `QqApiClient.GetAccessTokenAsync`（client_secret + fmt=json + redirect_uri 出站比对）→ `GetMeAsync` → openid（+unionid 若 EnableUnionId）→ `(channel_id, openid) → uid` → Federation 签 token2 |

- **unionid 联盟锚点配合**（P1-2）：`EnableUnionId=true` 时 `GetMeAsync` 额外请求 `unionid=1` 并返回（未申请权限时 QQ 返 100048 companyid not set → 抛错）。**external_uid 始终 = openid**（应用维度稳定映射键）；unionid 仅为联盟锚点写入辅助（写 `FederationAnchorOpenId` 归装配层，对齐 N2 §3.1 选项 B——锚点列存平台无关自生成值）。
- **匿名关联**：QQ 无手机号无邮箱（官方明示），联邦只能匿名关联（openid/unionid）——账号绑定靠 N2 锚点策略（映射表），不依赖手机号。

## 五、信任根（降级——出站换取链）

QQ **无入站回调**（无事件推送）——P5 一票否决**不适用**（非违规）。trust root = **出站换取链**：

- **code 一次性**（10min 过期）；
- **state 强校验**（会话绑定防 CSRF——authorize 构造归装配层生成，回调校验归装配层；库通道不感知 state——P1-3）；
- **`client_secret` 换取**（对称密钥防冒充——服务端持有，永不明文）；
- **redirect_uri 一致性校验**（P1-4——code 换 token 时 redirect_uri 须与授权时一致，`QqApiClient` 出站显式携带，QQ 服务端比对；缺省拒发换取）。

无可伪造 openid 的入站面——安全重心在"我方向 QQ 发起的授权 + code 一次性"。

## 六、安全边界

- **数据访问红线合规**：零持久化零 Store（tkwf-extension 铁律）；`QqApiClient` 直构（非 `IDomainService`），HttpClient 经 `IHttpClientFactory`（DI 生命周期托管）；`QqOauthChannel` 继承 `DomainServiceBase` + `[DiContractIgnore]`（运行时手写注册豁免 DI001）+ `AddConstructibleService` 守卫工厂（集合版 `TryAddEnumerableConstructible` ADR92 帧内供给）。
- **凭证自持**（Oracle P2-4）：AppSecret 从 `QqOptions.Channels` 按 AppId 精确匹配解析——**不经** Authentication `IPlatformCredentialService`；生产 AES-GCM 密文或装配注入，永不明文进配置库。
- **用户级 token 不缓存不续期**（Oracle P1-5）：QQ token = 用户级 authorization_code grant（非微信应用级 client_credential）——联邦认证面一次性消费；refresh_token 一次有效续票 3 个月，归 L7 非认证面 YAGNI。
- **响应解析防御**（协议事实核实结论）：token/me 端点默认非 JSON（token=URL-encoded / me=JSONP `callback(...)`）——**恒传 `fmt=json`** + `StripJsonp` 剥壳兜底（官方 PHP 实证 token 出错也可能 JSONP）；error 字段**数字**（String/Number 双路兜底）；get_user_info 错误模型 `ret/msg`（分端点解析）。
- **不引 AspNetCore**（Oracle 评审点 5）：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`）——入站端点归 Federation 扩展/消费方装配层。

## 七、与既有资产边界

| 资产 | 归属 | 定位 |
|------|------|------|
| `WeChatOauthChannel` / 其他平台库 | `TKWF.Federation.{平台}` | 各平台 IdP 适配（按 N1 模板复制）——QQ 为出站-only 精简形态先例 |

- **为什么是库不是扩展**：平台网关无持久化、纯协议逻辑——按"有持久化或有服务需装配 → 扩展；纯逻辑/无状态 → 库"判据应为**库**（AGENTS §8：`TKWF.*`=库）。
- **集合并列**：`qq_oauth` 通道与其他平台通道并列进 `IEnumerable<ISsoChannel>`（Federation 拥有契约但零实现）——未装配库 = 空集合"未注册通道自然跳过"。

## 八、架构决策记录

- 归层模型 ADR：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（Oracle PASS WITH CONDITIONS——四层归层模型，`TKWF.Federation.{平台}` = 大使馆/平台网关库）
- N3 开发方案：`docs/Federation/N3-QQ平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS——P1 五项落实：凭证字段 P1-1 / unionid 语义 P1-2 / state 层次 P1-3 / redirect_uri 一致性 P1-4 / 用户级 token 不缓存 P1-5）
- N1 平台网关通用模板：`docs/Federation/平台网关模板-开发指南.md`（权威固化——8 组件 + 出站-only 精简形态 + 复制清单）

## 九、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | N3 协议实现落地：`QqApiClient`（code→token→/me→openid + fmt=json + JSONP 剥壳 + redirect_uri 一致性 P1-4 + 用户级 token 不缓存 P1-5 + GetMeAsync unionid + get_user_info ret/msg 裁剪）/ `QqOauthChannel`（qq_oauth：code→openid + 缺 code 拒 + 缺 redirect_uri 拒 + AuthLevel=2 + 不感知 state P1-3）/ `QqUserInfo`/`QqMeResult` 响应裁剪 / `AddQqFederationChannels()`（保留 T4）/ 测试 16 用例全绿（生产路径通道正负 + QqApiClient 直构单测 + 协议结构断言 + unionid 开关 + StripJsonp 剥壳） |
| 0.1.1-preview.0.1（2026-10-06） | N1 骨架（T1/T4 注册壳 + 通道骨骼）作为 preview 首发——`AuthenticateAsync` 抛 `NotImplementedException`（协议未实现），N3 落地后稳定版覆盖 |

<!-- EOF -->