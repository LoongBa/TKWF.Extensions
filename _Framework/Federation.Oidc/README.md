# TKWF.Federation.Oidc 通用 OIDC 通道基座技术规范

**状态**: 平台网关基座库 (Platform Gateway Base Library——纯库无装配无持久化) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Federation`（实现其 `ISsoChannel` 契约——Oracle P1-1 单向依赖）+ `TKWF.Domain` + `Microsoft.Extensions.Http`（`IHttpClientFactory`）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**）/ **零持久化零 Store**（tkwf-extension 铁律）/ **最小职责 = channel 适配 + 配置驱动**（OIDC 原语若 `Utility.OAuthClient` 引擎可用则委托、否则内联标注待迁移——国外规划 §五 Oracle P1-3）/ **不引 AspNetCore**（入站端点归装配层）/ 凭证自持（Oracle P2-4）

---

## 一、定位

**国外 OIDC 平台（Google/Microsoft/LinkedIn/Slack…）共享的 OIDC 通道基座**——5 平台共享 >80% 代码（国外规划 §五）：Discovery 配置驱动（LinkedIn 非标路径 / Apple 双端点 iss 容错）/ JWKS 管理（kid 精确匹配 + L1 缓存 + 轮换重取）/ id_token 验签（RS256 + iss/aud/azp/exp/nbf/sub）/ PKCE S256 / sub 语义策略（不透明透传 + pairwise 复合编码）。

| 能力 | 说明 |
|------|------|
| `OidcChannelBase` | 抽象通道基座（`DomainServiceBase, ISsoChannel`）——`AuthenticateAsync` 编排 code→token→验签→sub；`BuildChannelId` 复合编码（pairwise/public）；`Defaults()` 派生填平台端点 |
| `OidcAuthFlow` | 出站协议流——Discovery 可选解析 / authorize URL 构造（装配层消费）/ code→token（client_secret post / private_key_jwt + redirect_uri 一致性 RFC 6749 §4.1.3 + PKCE）/ userinfo 拉取 |
| `JwksManager` | JWKS 管理——kid→RSA 公钥 L1 缓存 + SemaphoreSlim 并发锁 + 过期刷新；kid 匹配失败重取一次（平台轮换密钥恢复） |
| `OidcIdTokenValidator` | id_token 验签器——alg 强制 RS256 / kid 白名单 + JWKS 验签 / iss 白名单（通配/正则——Microsoft common tenant iss 含 GUID）/ aud 含 client_id（数组场景 azp）/ exp·iat leeway 30s + nbf / sub 必存 / Base64Url 边界 + FixedTimeEquals |
| `OidcConfiguredChannel` | 自托管 IdP 直配通道（Keycloak/Okta/Auth0/Authentik——零专属代码） |
| `OidcPlatformConfig` | 平台配置（端点全可覆盖 + TokenIssuer 白名单 + PKCE 开关 + private_key_jwt 预留） |

**双边接线**：
- **派生平台库**（M2 Google/Microsoft）：`GoogleOidcChannel : OidcChannelBase`（Defaults 填端点 + BuildChannelId 复合编码）→ `AddOidcDerivedChannels<GoogleOidcChannel>()`（平台库自行注册其 Options——Oracle P1-6）；
- **自托管 IdP 直配**：`services.AddOidcFederationChannel("keycloak-corp", o => { ... })`——`OidcConfiguredChannel` 具体包装注册（Oracle P0-1：抽象基类不可 direct 注册）。

**组合矩阵**（消费方按需装配）：
- 只引 `TKWF.Ext.Federation` + `TKWF.Federation.Oidc` = 纯外部 OIDC 联邦登录（BYO IdP——V5 国外客户主力形态）
- **Federation + Oidc + 派生平台库（M2）** = Google/Microsoft/LinkedIn 等平台 OIDC 登录

**不包含**：具体平台适配（Google/Microsoft/Apple 独立平台库——M2/L3 立项）；Apple OIDC 变体（form_post/user 一次性——独立适配器非基座）；`TKWF.Utility.OAuthClient` 引擎（归主框架，转达框架组）；对外 OIDC 端点薄层（v0.2.0 下游 OP——独立轨道）。

## 二、安装与接线

### 1. 消费方引用 + 直配

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.Oidc\TKWF.Federation.Oidc.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- ISsoChannel 契约 -->
```

```csharp
// 自托管 IdP 直配（Keycloak/Okta/Auth0——零平台专属代码）
builder.Services.AddOidcFederationChannel("keycloak-corp", o =>
{
    o.DiscoveryUri = "https://idp.corp.com/realms/main/.well-known/openid-configuration";  // Discovery 驱动（端点可覆盖）
    o.ClientId = "tkwf-app";
    o.ClientSecret = "<AES-GCM 密文或装配注入>";   // 凭证自持（Oracle P2-4）
    o.Scopes = ["openid", "email"];
    o.TokenIssuers = ["https://idp.corp.com/realms/main"];
});
```

### 2. 派生平台库（M2 形态——Google/Microsoft 平台库内部）

```csharp
public sealed class GoogleOidcChannel : OidcChannelBase
{
    public GoogleOidcChannel(IDomainUser user, OidcAuthFlow flow, IOptions<GoogleOptions> options) : base(user, flow) { ... }
    public override string ChannelType => "google_oidc";
    protected override string BuildChannelId(OidcPlatformConfig config) => $"{ChannelType}:*";   // public 通配
    protected override OidcPlatformConfig EffectiveConfig => /* 平台 Options 选区 + Defaults 合并 */;
    protected override OidcPlatformConfig Defaults() => new() { AuthorizeUri = "...", TokenUri = "...", ... };
}
// 平台库注册扩展（自行注册 Options + AddOidcDerivedChannels<GoogleOidcChannel>）
```

### 3. 配置 `TKWF:Federation:Oidc`（直配场景）

```jsonc
{
  "TKWF": {
    "Federation": {
      "Oidc": {
        "Channels": [
          {
            "ChannelId": "keycloak-corp",          // channel 实例 id（ISsoChannel.ChannelId 选区）
            "Platform": "keycloak",                 // ChannelType = {platform}_oidc
            "ClientId": "tkwf-app",
            "ClientSecret": "<AES-GCM 密文>",       // 生产永不明文进配置库
            "DiscoveryUri": "https://idp.corp.com/realms/main/.well-known/openid-configuration",
            "Scopes": ["openid", "email"],
            "TokenIssuers": ["https://idp.corp.com/realms/main"]
          }
        ]
      }
    }
  }
}
```

### 4. 编排（Federation 白名单 + `/sso/login`——装配层）

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

装配层构造 authorize URL（`OidcAuthFlow.BuildAuthorizeUrl`——state/PKCE verifier 会话绑定）→ 回调 code → 帧内 `User.Use<ISsoLogin>().LoginAsync(channelType, context)`（context 带 code + redirect_uri + code_verifier）→ `OidcChannelBase.AuthenticateAsync` 验签归一 `(channel_id, sub)` → token2。**state 校验归装配层**（库 `AuthenticateAsync` 不感知 state——对齐 WeChat/N3 先例）。

## 三、安全边界

- **信任根 = id_token JWKS 验签**（出站-only——OIDC 平台无入站回调，P13）：alg 强制 RS256（拒 none/HS256）/ kid 精确匹配 + JWKS 重取 / iss 白名单（通配/正则——Microsoft common tenant iss 含实际租户 GUID）/ aud 含 client_id（数组场景 azp）/ exp·iat leeway 30s + nbf / sub 必存 / Base64Url 边界 + FixedTimeEquals。
- **JWKS 端点篡改防护**（Oracle P2-4）：HTTPS 强制 + kid 白名单（首次见到入白名单 + 配置预置兜底）+ JWKS 缓存 TTL 1h + 响应大小钳制（512KB）+ RSA <2048 拒。
- **凭证自持**（Oracle P2-4）：ClientSecret/private_key 从 `OidcPlatformConfig` 解析——生产 AES-GCM 密文或装配注入；**绝不落日志**（id_token 仅记 sub+iss+kid+exp——PII redaction）。
- **redirect_uri 一致性**（RFC 6749 §4.1.3）：code→token 时发回授权时同名 redirect_uri——防 code 窃取换 token。
- **PKCE S256**（OAuth 2.1）：`UsePkce` 默认启用——verifier 缺失/不匹配拒（Stub 服务端比对）。
- **sub 不透明字符串**（P12）：不解析结构（Apple 格式未契约化）——`(channel_id, sub)` 归一，pairwise 隔离经 channel_id 复合编码（N2 §3.3）。

## 四、架构决策记录

- 平台网关立项依据：`docs/Federation/平台网关立项依据-近中远.md`（M1 中期）
- 国外平台总规划：`docs/Federation/平台网关总体规划-国外平台-开发方案.md`（§五 OidcChannelBase 形态 + §八复核结论）
- M1 开发方案：`docs/Federation/M1-通用OIDC通道基座-开发方案.md`（Oracle PASS WITH CONDITIONS——P0×1 + P1×7 落实）
- 锚点定案：`docs/Federation/N2-联盟锚点映射策略-开发方案.md`（pairwise channel_id 复合编码 §3.3）
- 联邦互联归层：`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（§五.8 协议面扩展余地）

## 五、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | M1 通用 OIDC 通道基座：OidcChannelBase（DomainServiceBase, ISsoChannel——BuildChannelId 复合编码 + Defaults 派生）/ OidcAuthFlow（Discovery 配置驱动 + code→token（client_secret post / private_key_jwt + redirect_uri 一致性 + PKCE S256）+ userinfo）/ JwksManager（kid 精确匹配 + L1 缓存 + 轮换重取）/ OidcIdTokenValidator（RS256 + iss 白名单通配/正则 + aud/azp + exp/iat/nbf + sub）/ OidcConfiguredChannel（自托管 IdP 直配）+ AddOidcFederationChannel / AddOidcDerivedChannels 注册；18 测试全绿（验签正负/issuer 容错/pairwise 复合编码/PKCE/Discovery/multi-IdP 枚举）；slnx 已接线；Oracle 评审 P0×1（注册抽象基类→OidcConfiguredChannel）+ P1×7 落实 |

<!-- EOF -->