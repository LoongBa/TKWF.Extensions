# TKWF.Federation.Google Google 平台网关技术规范

**状态**: 平台网关库 (Platform Gateway Library——纯库无装配无持久化) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: `TKWF.Federation.Oidc`（M1 通用 OIDC 基座——继承配置驱动）+ `TKWF.Domain`（CPM）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**）/ **零持久化零 Store** / **不引 AspNetCore** / 凭证自持（Oracle P2-4）/ 单向依赖（`Google → Oidc 基座 → Federation → AuthCenter.Abstractions`）

---

## 一、定位

**Google OIDC 平台网关**（M2 国外第一批——OIDC 参考实现基线）：继承 M1 基座 `OidcChannelBase`，仅填 Google 固定端点 + **public sub 通配 channel_id**（`google_oidc:*`——N2 §3.3 定案）。

| 能力 | 说明 |
|------|------|
| `GoogleOidcChannel` | `google_oidc` 通道——public sub 跨 App 相同 → `google_oidc:*` 通配（同一用户经不同 Google App 登录映射同 uid）；`Defaults()` 填 Google 端点（authorize/token/userinfo/JWKS/Discovery）；凭证/覆盖经 `GoogleOptions.Channels` 选区合并 |
| `GoogleOptions`/`GoogleChannelConfig` | `TKWF:Federation:Google` 节——ClientId/ClientSecret（Google 仅两凭证——Oracle P1-1）/TokenIssuers 白名单 |
| `AddGoogleFederationChannels()` | DI 注册扩展——`AddOptions().Configure` + `AddOidcDerivedChannels<GoogleOidcChannel>`（基座注册 OidcAuthFlow/JwksManager/Validator + 通道入集合） |

**组合矩阵**：Federation + Oidc + Google = Google 联邦登录（V5 国外客户消费形态）。

**不包含**：多 Google App 场景（channel_id 加 client_id 区分但映射表按 sub 归一是 N2 §3.3 public 通配语义边界——本库不实现，一个消费方只配一个 Google channel 实例，Oracle P1-3）；Apple OIDC 变体（L3 独立适配器）。

## 二、安装与接线

### 1. 消费方引用 + 注册

```xml
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.Google\TKWF.Federation.Google.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- ISsoChannel 契约 -->
```

```csharp
builder.Services.AddGoogleFederationChannels(o =>
{
    o.Channels = [new GoogleChannelConfig
    {
        ChannelId = "google-main",
        ClientId = "<Google OAuth client_id>",
        ClientSecret = "<AES-GCM 密文或装配注入>",   // 凭证自持（Oracle P2-4）
        TokenIssuers = ["https://accounts.google.com"],
    }];
});
```

### 2. 配置 `TKWF:Federation:Google`

```jsonc
{
  "TKWF": {
    "Federation": {
      "Google": {
        "Channels": [
          {
            "ChannelId": "google-main",
            "ClientId": "...",
            "ClientSecret": "<AES-GCM 密文>",
            "TokenIssuers": ["https://accounts.google.com"]
          }
        ]
      }
    }
  }
}
```

### 3. 编排（装配层——authorize 构造 + state 校验归装配层）

```csharp
// 装配层：构造 authorize URL（基座 OidcChannelFlow.BuildAuthorizeUrl——scope=openid email profile + state/PKCE verifier 引擎生成，装配层持久化）
// → 回调 code → 帧内 User.Use<ISsoLogin>().LoginAsync("google_oidc", context)（context 带 code + redirect_uri + verifier）
// → GoogleOidcChannel.AuthenticateAsync → id_token JWKS 验签（RS256 + iss/aud/azp/exp/nbf/sub）→ (google_oidc:*, sub) → token2
```

## 三、安全边界

- **信任根 = id_token JWKS 验签**（M1 基座核心——Google JWKS RS256）：alg 强制 RS256 / kid 精确匹配 / iss 白名单（accounts.google.com）/ aud=client_id / exp·iat leeway + nbf / sub 必存 / FixedTimeEquals。
- **public sub 语义**（N2 §3.3）：`google_oidc:*` 通配——同一用户跨 App sub 相同可并（映射表 `(channel_id, sub)` 相同行）。
- **凭证自持**：ClientSecret 生产 AES-GCM 密文或装配注入，永不明文进配置库。
- **PKCE S256**（OAuth 2.1 对齐）：基座默认启用。

## 四、架构决策记录

- M2 开发方案：`docs/Federation/M2-Google与Microsoft平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS——P1×7 落实）
- 国外平台总规划：`docs/Federation/平台网关总体规划-国外平台-开发方案.md`（§三第一批 + §四矩阵 + §五基座）
- M1 基座：`docs/Federation/M1-通用OIDC通道基座-开发方案.md` + `_Framework/Federation.Oidc/README.md`
- 锚点定案：`docs/Federation/N2-联盟锚点映射策略-开发方案.md`（§3.3 pairwise/public channel_id 复合编码）

## 五、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | M2 Google 平台网关：GoogleOidcChannel（public sub 通配 `google_oidc:*` + Defaults 填 Google 端点 + MergeConfig 凭证合并）+ GoogleOptions/GoogleChannelConfig + AddGoogleFederationChannels（经 Oidc 基座）；9 测试全绿（OAuth 正负/id_token 篡改拒/public 通配断言/注册枚举）；slnx 已接线。**OIDC 归并配套（2026-10-06，基座 V0.2.0）**：ctor 类型同步 `OidcAuthFlow` → `OidcChannelFlow`（委托引擎薄层）——API 不变，行为委托 `Utility.OAuthClient` |

<!-- EOF -->