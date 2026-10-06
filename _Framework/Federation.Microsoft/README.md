# TKWF.Federation.Microsoft Microsoft Entra ID 平台网关技术规范

**状态**: 平台网关库 (Platform Gateway Library——纯库无装配无持久化) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: `TKWF.Federation.Oidc`（M1 通用 OIDC 基座——继承配置驱动）+ `TKWF.Domain`（CPM）

**核心约束**: 无 Initializer 无 `[TKWFExtension]`（**纯库判据**）/ **零持久化零 Store** / **不引 AspNetCore** / 凭证自持（Oracle P2-4）/ 单向依赖（`Microsoft → Oidc 基座 → Federation → AuthCenter.Abstractions`）

---

## 一、定位

**Microsoft Entra ID 平台网关**（M2 国外第一批——企业/学校账号场景）：继承 M1 基座 `OidcChannelBase`，**pairwise sub 复合编码 channel_id**（`microsoft_oidc:{client_id}`）+ **tenant 配置化**（common/consumers/organizations/{id}）。

| 能力 | 说明 |
|------|------|
| `MicrosoftOidcChannel` | `microsoft_oidc` 通道——**pairwise sub 跨 App 不同** → channel_id 复合编码 `microsoft_oidc:{client_id}`（Oracle P1-2：ChannelId 计算属性自动拼接，消费方只配 ClientId）；`Defaults()` 按 Tenant 模板化端点；凭证/覆盖经 `MicrosoftOptions.Channels` 选区合并 |
| `MicrosoftOptions`/`MicrosoftChannelConfig` | `TKWF:Federation:Microsoft` 节——ClientId/ClientSecret/**Tenant**（common/consumers/organizations/{id}）/TokenIssuers 白名单 |
| `AddMicrosoftFederationChannels()` | DI 注册扩展——`AddOptions().Configure` + `AddOidcDerivedChannels<MicrosoftOidcChannel>` |

**组合矩阵**：Federation + Oidc + Microsoft = Microsoft 企业/学校联邦登录。

**不包含**：`private_key_jwt` 证书认证（企业场景——config 预留 `ClientAssertionSigningKeyPath`，M2 先支持 client_secret，证书认证归后续迭代，Oracle P2-1）。

## 二、安装与接线

### 1. 消费方引用 + 注册

```xml
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation.Microsoft\TKWF.Federation.Microsoft.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />  <!-- ISsoChannel 契约 -->
```

```csharp
builder.Services.AddMicrosoftFederationChannels(o =>
{
    o.Channels = [new MicrosoftChannelConfig
    {
        ChannelId = "ms-orgs",            // 一 ChannelId = 一 tenant 实例（Oracle P1-7）
        ClientId = "<Entra App client_id>",
        ClientSecret = "<AES-GCM 密文或装配注入>",   // 凭证自持（Oracle P2-4）
        Tenant = "organizations",         // common/consumers/organizations/{租户 ID}
        TokenIssuers = ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],   // 正则——tenant iss 含实际 GUID
    }];
});
```

### 2. 配置 `TKWF:Federation:Microsoft`

```jsonc
{
  "TKWF": {
    "Federation": {
      "Microsoft": {
        "Channels": [
          {
            "ChannelId": "ms-orgs",
            "ClientId": "...",
            "ClientSecret": "<AES-GCM 密文>",
            "Tenant": "organizations",
            "TokenIssuers": ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"]
          }
        ]
      }
    }
  }
}
```

### 3. 编排（装配层——authorize 构造 + state 校验归装配层）

```csharp
// 装配层：构造 authorize URL（基座 OidcAuthFlow.BuildAuthorizeUrl——{tenant} 模板化 + scope=openid profile email + state + PKCE S256）
// → 回调 code → 帧内 User.Use<ISsoLogin>().LoginAsync("microsoft_oidc", context)
// → MicrosoftOidcChannel.AuthenticateAsync → id_token JWKS 验签 → (microsoft_oidc:{client_id}, sub) → token2
```

## 三、安全边界

- **信任根 = id_token JWKS 验签**（M1 基座核心——Microsoft JWKS RS256）：alg 强制 RS256 / kid 精确匹配 / iss 白名单（**通配/正则——common/consumers/organizations 授权后 iss 含实际租户 GUID，M2-P1-1**）/ aud=client_id / exp·iat leeway + nbf / sub 必存 / FixedTimeEquals。
- **pairwise sub 语义**（N2 §3.3）：`microsoft_oidc:{client_id}` 复合编码——同用户不同 App（不同 client_id）不同 channel_id → **不误并**（映射表不同行，绑定并合经用户验证 N2 §3.2）；**不加映射表列、不动既有 `UX_PlatformAccountMap_Channel` 索引**。
- **tenant 语义**（Oracle P1-7）：**tenant = 访问控制维度**（谁能登录：common=AAD+MSA / organizations=仅 AAD / consumers=仅 MSA）——**不影响 pairwise sub 派生**（按 client_id 派生）；channel_id 不含 tenant 正确（N2 §3.3 定案语义）。
- **凭证自持**：ClientSecret 生产 AES-GCM 密文或装配注入，永不明文进配置库。

## 四、架构决策记录

- M2 开发方案：`docs/Federation/M2-Google与Microsoft平台网关-开发方案.md`（Oracle PASS WITH CONDITIONS——P1×7 落实）
- 国外平台总规划：`docs/Federation/平台网关总体规划-国外平台-开发方案.md`（§三第一批 + §四矩阵 + §五基座）
- M1 基座：`docs/Federation/M1-通用OIDC通道基座-开发方案.md` + `_Framework/Federation.Oidc/README.md`
- 锚点定案：`docs/Federation/N2-联盟锚点映射策略-开发方案.md`（§3.3 pairwise channel_id 复合编码）

## 五、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-06） | M2 Microsoft 平台网关：MicrosoftOidcChannel（pairwise 复合编码 `microsoft_oidc:{client_id}` + tenant 配置化端点模板 + TokenIssuer 正则白名单）+ MicrosoftOptions/MicrosoftChannelConfig（Tenant 字段）+ AddMicrosoftFederationChannels（经 Oidc 基座）；11 测试全绿（pairwise 复合编码断言/tenant iss 含 GUID 正则匹配/OAuth 正负/evil iss 拒/注册枚举）；slnx 已接线 |

<!-- EOF -->