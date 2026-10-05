# M1 通用 OIDC 通道基座 开发方案（TKWF.Federation.Oidc）

> **立项来源**: 平台网关立项依据（近中远）M1（P1 中期——**国外 OIDC 平台全部前置**）｜ **类型**: L0 基座
> **版本**: v0.1.0 ｜ **日期**: 2026-10-06 ｜ **状态**: 📋 提议（待 Oracle 评审）
> **关联**: 国外总规划 §五（OidcChannelBase 定形态——Oracle P1-2/P1-3/P1-7）｜ 国外规划 §八复核结论（Discovery 配置驱动/sub 不透明/JWKS kid 匹配）｜ N2 锚点策略（pairwise channel_id 复合编码）｜ WeChat 先例（`_Framework/Federation.WeChat/`——平台网关模板）
> **关键约束**: 平台网关 = **纯库**（无 Initializer 无 `[TKWFExtension]` 无持久化零 Store）；OidcChannelBase **最小职责 = channel 适配 + 配置驱动**——OIDC 原语（Discovery/JWKS/id_token 验签）若 `TKWF.Utility.OAuthClient` 引擎可用则委托、否则内联并标注"待引擎落地后迁移"（国外规划 §五 Oracle P1-3）；不引 AspNetCore

---

## 一、背景与目标

### 问题

国外 OIDC 平台（Google/Microsoft/LinkedIn/Slack…）共享标准协议（Discovery + JWKS + /userinfo），**5 平台共享 >80% 代码**（国外规划 §五）——若每平台独立从零实现 JWKS 管理/id_token 验签/Discovery 解析，重复劳动 + 验签实现漂移（安全一致性命脉）。国内平台走 WeChat 式模板（N1），国外 OIDC 平台须走**独立 OIDC 基座**（协议阵营不同，模板与基座并行——立项依据 §三排期要点 2）。

### 目标

1. **`TKWF.Federation.Oidc` 独立基座库**（Oracle P1-7 定案）——OidcChannelBase 承载 channel 适配 + 配置驱动；
2. **OIDC 协议原语最小实现**——Discovery（可选，配置驱动 URL）/ JWKS 管理（kid 精确匹配 + 缓存 + 并发）/ id_token 验签（RS256 + iss/aud/exp + kid 白名单）/ userinfo 拉取 / PKCE S256；
3. **sub 语义策略**——不透明字符串透传（P12 强化），pairwise 隔离经 channel_id 复合编码（N2 §3.3 定案）；
4. **双边接线**——平台库（Google/Microsoft 等 M2）**继承基座**填配置；自托管 IdP（Keycloak/Okta/Auth0——国外规划 §三 Oracle P2-3）**直接装配基座**免平台库；
5. 产出可测（id_token JWKS 验签正负 + issuer 容错——验收 F2），为 M2 提供已验证基座。

### 不包含

| # | 内容 | 去向 |
|---|------|------|
| 1 | Apple OIDC **变体**（form_post/user 一次性/无 userinfo） | L3 远期独立适配器（不继承基座——非完整 OIDC，国外规划 §二 P14） |
| 2 | 私有 OAuth2（GitHub/Facebook/Amazon/X） | WeChat 式专属适配器（N1 模板——非 OIDC 基座） |
| 3 | `TKWF.Utility.OAuthClient` 引擎（BCL，归主框架） | 转达框架组（国外规划 §七 3）——基座内联原语标注"待引擎落地后迁移"，不自行提炼 |
| 4 | 对外协议面 OIDC 端点薄层（v0.2.0，下游 OP） | ADR §五.8 独立轨道——RP 验签 ≠ OP 签发，不预设上下行共享（国外规划 §五 P1-2） |

---

## 二、基座设计（国外规划 §五落地形态）

### 2.1 目录结构

```
_Framework/Federation.Oidc/
├── TKWF.Federation.Oidc.csproj            # ProjectReference ..\Federation\TKWF.Ext.Federation.csproj（ISsoChannel）+ TKWF.Domain（条件）+ Microsoft.Extensions.Http；不引 AspNetCore
├── OidcOptions.cs                          # [Options("TKWF:Federation:Oidc")]——Channels 列表（OidcPlatformConfig 集合）
├── OidcPlatformConfig.cs                   # 平台配置：ChannelId + PlatformName + 端点（authorize/token/userinfo/jwks/discovery 均可覆盖）+ scope[] + client_id/凭证 + PKCE 开关 + 是否 Discovery 优先 + TokenIssuer 白名单
├── OidcAuthFlow.cs                         # 出站协议流：Discovery 解析（可选）→ authorize URL 构造（装配层消费）→ code→token（client_secret post / private_key_jwt 断言）→ id_token 解析
├── JwksManager.cs                          # JWKS 管理：kid→RSA 公钥映射 + L1 缓存 + SemaphoreSlim 并发锁 + 过期刷新；kid 精确匹配（RFC 7515 §4.1.4——禁取第一把）
├── OidcIdTokenValidator.cs                 # id_token 验签：alg 强制 RS256 / 签名（JwksManager）/ iss（TokenIssuer 白名单——支持通配/正则，Microsoft common tenant 场景 iss 含实际租户 GUID）/ aud（含 client_id——**数组场景且多方时 azp 必校验 = client_id**，Oracle 评审 P1-3）/ exp·iat leeway 30s + **nbf**（≤ now + leeway，P2-2）/ sub 必存在 / 不解析 sub 结构
├── OidcChannelBase.cs                      # 抽象基类：**DomainServiceBase, ISsoChannel**（Oracle 评审 P1-1：继承 DomainServiceBase 提供 User 上下文——tkwf-extension §4.3 铁律，IDomainUser 永不注册 DI 经基类取）；ChannelId/ValidIssuers/Scope 由派生 or 配置注入；AuthenticateAsync 编排 code→token→id_token 验签→(可选 userinfo) → SsoChannelAuthResult(ExternalUserId=sub, AuthLevel=2)；**BuildChannelId(config) 虚方法构造 channel_id 复合编码**（Oracle 评审 P1-5：默认返回 config.ChannelId，pairwise 派生类覆写 `$"{ChannelType}:{config.ClientId}"`、public 派生类覆写 `$"{ChannelType}:*"`——构造责任在 channel 内部，不依赖消费方手动拼接）
├── OidcFederationServiceCollectionExtensions.cs  # AddOidcFederationChannel(name, Action<OidcPlatformConfig>?)（直配自托管 IdP）/ AddOidcDerivedChannels<TChannel>()（派生平台库用——对齐 N1 模板 P1-1 注册形态）——TryAddEnumerableConstructible<ISsoChannel>
└── (无 Initializer 无 [TKWFExtension]——纯库判据)
```

### 2.2 依赖接线（对齐 WeChat 先例 + N1 模板 §2.3）

```xml
<!-- TKWF.Federation.Oidc.csproj -->
<ProjectReference Include="..\Federation\TKWF.Ext.Federation.csproj" />   <!-- 实现 ISsoChannel（Oracle P1-1 单向依赖） -->
<PackageReference Include="TKWF.Domain" Condition="'$(UseLocalFw)' != 'true'" />
<PackageReference Include="TKWF.CodeGeneration.Abstractions" Condition="'$(UseLocalFw)' != 'true'" />  <!-- DiContractIgnore（N1 模板 P1-2） -->
<Reference Include="TKWF.CodeGeneration.Abstractions" Condition="'$(UseLocalFw)' == 'true'" />
<PackageReference Include="Microsoft.Extensions.Http" />                  <!-- typed client（HttpClientFactory 生命周期） -->
<!-- ⚠️ 不引 AspNetCore（P6）——入站端点归装配层；OidcAuthFlow 纯逻辑收 HttpMessageHandler/HttpClient -->
```

### 2.3 OidcChannelBase 认证流

```
装配层构造 authorize URL（OidcAuthFlow.BuildAuthorizeUrl：client_id+redirect_uri+scope+state+code_challenge(S256)）
→ 302 平台授权页（纯跳转，国外无扫码）→ 回调 ?code=&state=
→ 验 state（**归装配层**——对齐 WeChat/N3 先例，基座 AuthenticateAsync 不感知 state）→ OidcAuthFlow.ExchangeCodeAsync：
    POST token 端点（grant_type=authorization_code + code + redirect_uri（一致性校验，RFC 6749 §4.1.3）+ code_verifier + client_secret/private_key_jwt）
→ 解析 id_token（不信任裸 JWT——JwksManager 按 kid 取公钥 + OidcIdTokenValidator RS256 验签 + iss/aud/exp/sub）
→ 可选 userinfo 拉取（标准端点，补 profile/email——默认懒加载，AuthLevel 基础够）
→ SsoChannelAuthResult(Success, ExternalUserId=sub, AuthLevel=2) <!-- AuthLevel=2 对齐微信 OAuth 便捷档（Oracle 评审 P2-1）——Federation 编排层按 AuthLevel 决定后续步骤 -->
→ (channel_id, sub) → uid（Federation 编排层，N2 锚点策略——channel_id 复合编码含 pairwise client 维度）
```

### 2.4 sub 语义策略（P12 强化 + N2 定案）

| 维度 | 处理 |
|------|------|
| **sub 不透明** | **不解析 sub 结构**（Apple `UUID+teamIdentifier` 从未官方契约化——国外规划 §八复核 2）——sub 当不透明字符串整体持久化；Google `public`/Microsoft `pairwise` 均按 `(channel_id, sub)` 归一，无特判 |
| **pairwise 隔离** | channel_id **复合编码** `"{platform}_oidc:{client_id}"`（N2 §3.3 定案）——Microsoft/LinkedIn 同用户跨 App sub 不同 → 不同 channel_id 天然隔离，**不加映射表列不动索引**；public 平台 client_id 通配 `*` |
| **issuer 容错** | `TokenIssuer` 白名单可配（默认 = config.Issuer 若显式 / Discovery 的 issuer 先存疑）——**校验 token 的 `iss`**；白名单须支持**通配/正则**（Microsoft common/consumers/organizations tenant 授权后 `iss` 含实际租户 GUID 如 `https://login.microsoftonline.com/{guid}/v2.0`，非字面 tenant 值——M2-P1-1）；不匹配不静默失败（fail 明确）。**Apple 事实注记（Oracle 评审 P1-4）**：Apple id_token `iss` 与 Discovery issuer **实际一致**（`https://appleid.apple.com`）——白名单容错针对多区域 IdP（如 Keycloak 多 realm iss 含 realm 名）/IdP 迁移场景，**非 Apple 必需**——Apple 示例不准确会致白名单过宽 |
| **复合键通知** | Slack `(sub, team_id)` / 企业场景复合标识——OidcChannelBase 提供可覆写 `BuildChannelIdentity(config, idToken)` 钩子（默认 sub），派生平台按需覆写（L6 第三批）——YAGNI：M1 不实现 Slack，仅留钩子 |

### 2.5 双边接线形态

```csharp
// ① 派生平台库（M2 Google/Microsoft——继承基座，仅填配置）
public sealed class GoogleOidcChannel : OidcChannelBase
{
    public GoogleOidcChannel(IDomainUser user, OidcAuthFlow flow, IOptions<GoogleOptions> options)
        : base(user, flow) { _options = options; }           // Oracle 评审 P1-1：IDomainUser 透传基类（对齐 WeChat 先例 : base(user)）

    protected override string ChannelType => "google_oidc";
    protected override string BuildChannelId(OidcPlatformConfig config) => $"{ChannelType}:*";   // Oracle 评审 P1-5：public 通配
    protected override OidcPlatformConfig Defaults() => new()
    {
        AuthorizeUri = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenUri     = "https://oauth2.googleapis.com/token",
        UserInfoUri  = "https://openidconnect.googleapis.com/v1/userinfo",
        JwksUri      = "https://www.googleapis.com/oauth2/v3/certs",
        Scopes       = ["openid", "email", "profile"],
    };
}

// ② 自托管 IdP 直配（Keycloak/Okta/Auth0——免平台库，国外规划 §三 P2-3）
services.AddOidcFederationChannel("keycloak-corp", o => {
    o.DiscoveryUri = "https://idp.corp.com/realms/main/.well-known/openid-configuration";
    o.ClientId = "..."; o.ClientSecret = "..."; o.Scopes = ["openid", "email"];
});
```

### 2.6 注册扩展形态（组件 7——对齐 N1 模板 §2.4 修正后形态）

```csharp
public static class OidcFederationServiceCollectionExtensions
{
    // 自托管 IdP 直配（免平台库）——OidcPlatformConfig 直接向导
    public static IServiceCollection AddOidcFederationChannel(
        this IServiceCollection services, string name, Action<OidcPlatformConfig>? configure = null)
    {
        services.AddOptions<OidcOptions>().Configure(o => { /* 追加 name 配置 */ });
        services.AddHttpClient<OidcAuthFlow>();
        services.TryAddEnumerableConstructible<ISsoChannel, OidcConfiguredChannel>(); // ✅ Oracle 评审 P0-1：注册具体包装通道——OidcChannelBase 是抽象类不可 direct 注册（DI 无法实例化运行时必炸）；OidcConfiguredChannel : OidcChannelBase 按 name 选区，基座库内部提供
        return services;
    }

    // 派生平台库（M2）注册——平台库调用，配置经其自身 Options 注入基座
    public static IServiceCollection AddOidcDerivedChannels<TChannel>(this IServiceCollection services)
        where TChannel : OidcChannelBase
    {
        services.AddHttpClient<OidcAuthFlow>();                             // 复用基座出站（typed client 生命周期）
        services.TryAddEnumerableConstructible<ISsoChannel, TChannel>();    // 派生通道入集合（DomainService => 守卫工厂）
        return services;
    }
}

// ⚠️ 派生平台库调用 AddOidcDerivedChannels<TChannel>() 后须自行注册其平台 Options（如 AddOptions<GoogleOptions>().Configure(...)）
//    ——基座不感知派生 Options 类型（Oracle 评审 P1-6）；channel 业务参数（凭证/ChannelId/Scope 等）经平台 Options 注入不占 ctor IDomainUser 槽（对齐 WeChat 先例）
```

> ⚠️ **base 注册注意**：OidcChannelBase 抽象类不可 direct 注册（非具体类型）——自托管直配场景注册**具体包装通道**（`OidcConfiguredChannel : OidcChannelBase` 按 `name` 选区，基座库内部提供）；派生平台库场景注册具体派生类型（`GoogleOidcChannel` 等）。**禁止尝试注册抽象基类**（DI 无法实例化——方案自检点）。

---

## 三、任务拆解

| 任务 | 描述 | 关联 | 工作量 |
|------|------|------|--------|
| T1 | csproj + OidcOptions/OidcPlatformConfig（端点全可覆盖 + TokenIssuer 白名单） | N1 模板接线 | 低 |
| T2 | JwksManager（kid 精确匹配 + L1 缓存 + SemaphoreSlim 并发锁 + 过期刷新——对齐 WeChatApiClient access_token 缓存模式） | 组件 A | 中 |
| T3 | OidcIdTokenValidator（alg 强制 RS256 / kid 白名单 / iss 白名单（支持通配/正则）/ **aud 含 client_id——数组场景 azp = client_id（Oracle 评审 P1-3）** / exp·iat leeway 30s + **nbf ≤ now+leeway（P2-2）** / sub 必存 / Base64Url 边界 + FixedTimeEquals） | 信任根核心 | 中 |
| T4 | OidcAuthFlow（Discovery 可选解析——配置驱动 URL（LinkedIn `/oauth/.well-known/` 场景）；code→token（client_secret post 或 private_key_jwt 断言 + redirect_uri 一致性 + PKCE code_verifier 传递）） | 出站协议流 | 中 |
| T5 | OidcChannelBase + OidcConfiguredChannel（自托管直配包装）+ BuildChannelIdentity 钩子 | 通道基座 | 中 |
| T6 | 注册扩展（AddOidcFederationChannel 直配 / AddOidcDerivedChannels<TChannel> 派生）— **禁注册抽象基类**（§2.6 自检点） | 组件 7 | 低 |
| T7 | 测试：OidcTestHost + id_token JWKS 验签正负（篡改/伪造/过期/kid 不匹配拒——F2）+ issuer 容错（Apple 双端点模拟）+ PKCE + sub 语义（pairwise 复合编码断言）+ Discovery 配置驱动 + 探针门面多 IdP 枚举 | 组件 8 | 中 |
| T8 | README（技术规范）+ 使用指南（国外规划 §五 确认引用）更新 | — | 低 |

---

## 四、验收标准（对齐国外总规划 F1-F8）

| 需求ID | 验收条件 | 验收方式 |
|--------|---------|---------|
| F1 | 基座零 Initializer 零 `[TKWFExtension]` 零持久化零 Store（纯库判定） | 架构检查 |
| F2 | **id_token JWKS 验签正确**（RS256 + kid 精确匹配 + iss 白名单 + aud 含 client_id/azp）；篡改/伪造/过期/kid 不匹配 → 拒（信任根负路径）；**issuer 容错**（token iss ≠ Discovery issuer 场景按白名单判定）；**JWKS kid 匹配失败后重取一次 JWKS 再试（密钥轮换场景恢复——Oracle 评审 P1-7）**，重取后仍不匹配 → 拒 | 自动化测试（正负） |
| F3 | sub 语义：不透明字符串透传不解析；pairwise 复合编码 `"{platform}_oidc:{client_id}"` 断言（N2 §3.3） | 自动化测试 + 语义断言 |
| F4 | Discovery 配置驱动：配置 URL 生效（LinkedIn 非标准路径 / Apple 双端点模拟）；无 Discovery 时纯端点配置可用 | 自动化测试 |
| F5 | 双边接线：派生平台库注册（GoogleOidcChannel 骨架）入集合 + 自托管直配（OidcConfiguredChannel）入集合；多通道同时装配枚举全部 | 自动化测试 + 探针门面 |
| F6 | PKCE S256：code_challenge 生成 + code_verifier 传递 + 缺失/不匹配负路径 | 自动化测试 |
| F7 | 红线合规：零 Store / 零构造注入 / 零裸 ORM / 不引 AspNetCore；凭证自持（ClientSecret/私钥绝不落日志——敏感字段 redaction；**id_token 仅记 sub+iss+kid+exp（PII redaction，Oracle 评审 P2-3）；JWKS 公钥不落日志**） | 代码审查 + skill 自检清单 |
| F8 | 全量回归不破坏（当前基线全绿） | slnx 测试 |

---

## 五、风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| OIDC 原语内联与未来 Utility.OAuthClient 引擎重叠 | 中 | 内联原语文件头标注"待引擎落地后迁移"（纯函数式/无状态门面便于搬迁）；不自行提炼 BCL 协议资产（归引擎） |
| JWKS 缓存过期/轮换致验签失败 | 中 | L1 缓存 + 过期刷新；验签失败重取 JWKS 再试一次（kid 变化场景）——防平台轮换密钥时瞬间失败 |
| id_token 信任依赖 JWKS 端点可用性 | 中 | JWKS 缓存 + 重试；网络失败 = 明确失败（不静默放行——无 JWKS 无验签 = 无信任根） |
| Discovery 与 token issuer 不一致（Apple 双端点） | 中 | TokenIssuer 白名单优先于 Discovery issuer；配置驱动（§2.4 issuer 容错）——白名单支持通配/正则（Microsoft common tenant 场景 iss 含实际租户 GUID） |
| **JWKS 端点被攻击/中间人篡改**（Oracle 评审 P2-4） | 高 | HTTPS 强制 + kid 白名单（首次见到的 kid 入白名单 + 配置预置兜底，异常 kid 拒并告警）+ JWKS 缓存 TTL 不宜过短——防攻击者注入伪造公钥验签通过 |
| 派生通道/base 注册混淆（DI 注册抽象类） | 高 | §2.6 base 注册注意显式自检点；测试覆盖集合解析（防运行时炸） |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-06 | v0.1.0 | 初始版本——M1 通用 OIDC 通道基座开发方案（TKWF.Federation.Oidc 独立基座库）：基座设计（目录结构 2.1/OidcAuthFlow 认证流 2.3/sub 语义策略 2.4（不透明透传 + pairwise 复合编码 N2 定案 + issuer 容错）/双边接线 2.5（派生平台库 M2 + 自托管 IdP 直配——国外规划 P2-3）/注册扩展 2.6（禁注册抽象基类自检点））+ 任务拆解 T1-T8 + 验收 F1-F8（id_token JWKS 验签正负 + issuer 容错 + PKCE + pairwise 断言 + 多通道枚举）+ 风险（引擎重叠/JWKS 轮换/issuer 一致性/base 注册混淆） |
| 2026-10-06 | v0.1.0 | **Oracle 评审（bg_2744eb77 oracle8）PASS WITH CONDITIONS——P0×1 + P1×7 落实**：① §2.6 **P0 修正**——代码示例注册抽象基类 `OidcChannelBase` 改为注册具体包装 `OidcConfiguredChannel`（抽象类 DI 无法实例化，代码与注意点自相矛盾——复制起点错误代码修正）（P0-1）；② §2.1/§2.5 `OidcChannelBase : DomainServiceBase, ISsoChannel`**明确继承**（User 上下文供给 tkwf-extension §4.3 铁律）+ GoogleOidcChannel 示例补 `IDomainUser user` 透传基类（P1-1）；③ §2.1 注册方法签名与 §2.6 对齐（`AddOidcFederationChannel(name,...)` 非泛型 + `AddOidcDerivedChannels<TChannel>`）（P1-2）；④ §2.3/T3 **aud 多受众场景补 azp 校验**（数组场景必含 client_id 且 azp = client_id，Microsoft common tenant 场景）（P1-3）；⑤ §2.4 **Apple issuer 事实修正**——id_token iss 与 Discovery issuer 实际一致（非 Apple 必需容错），白名单通配/正则支持（Microsoft common tenant iss 含实际租户 GUID——联动 M2-P1-1）（P1-4）；⑥ §2.5 **BuildChannelId(config) 虚方法**——channel_id 复合编码构造责任在 channel 内部（pairwise `$"{ChannelType}:{config.ClientId}"` / public `$"{ChannelType}:*"`，不依赖消费方手动拼接）（P1-5）；⑦ §2.6 **AddOidcDerivedChannels 派生 Options 注册责任明确**——派生平台库须自行注册其平台 Options（P1-6）；⑧ §四 F2 **JWKS kid 匹配失败重取一次再试验收**（P1-7）。同步落实 P2 快速项：AuthLevel=2 对齐注记 / T3 补 nbf 校验 / F7 PII redaction 细化 / 风险表补 JWKS 端点篡改风险 |

<!-- EOF -->