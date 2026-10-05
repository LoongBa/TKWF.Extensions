# TKWF.Ext.Federation 认证中心联邦层技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain + TKWF.Ext.Authentication.Abstractions 契约包 + FreeSql + Microsoft.AspNetCore.App（JWKS/profile 端点装配）

**核心约束**: 手写 ES256 JWT（零第三方 JWT 库）/ **独立密钥域**（ECDSA P-256 PEM + kid 轮换 + JWKS，与 Authentication RS256 完全独立）/ 授权码 accesscode（120s 单次原子 CAS + SHA256 存储 + PKCE 可选）/ 应用注册（origin 白名单防开放重定向 + scope + client credential AES-GCM + per-channel HMAC）/ 数据访问红线合规（全走 SG1 DataService）/ 经 Authentication.Abstractions 契约消费认证内核（组合式，L2 门控）

---

## 一、定位

认证中心（联邦枢纽）的**跨应用联邦层**（Federated SSO）——认证中心实例的对外面：目标 H5 应用对认证渠道（微信/QQ/Google/Apple/微软）零感知，统一消费 token2（ES256 自包含 JWT）。

| 能力 | 说明 |
|------|------|
| 应用注册 | `SsoClientEntity`（app_id + origin 白名单 + scope + client credential + per-channel HMAC 密钥，AES-GCM 加密落库） |
| 授权码 accesscode | `SsoAccessCodeEntity`——CSPRNG 32B base64url / 120s TTL / **单次原子 CAS**（ADR89 条件 UPDATE）/ 只存 SHA256(code) / **PKCE 可选**（defense in depth） |
| token2 签发/验证 | `Token2Service`——手写 **ES256**（BCL ECDsa，P-256 曲线强制）+ kid 轮换 + 独立密钥域（生产 PEM fail-fast / 开发临时密钥 DevEcKeyCache）+ JWKS 分发 |
| profile API | `SsoProfileService`——server-to-server（scope 强制 + ILogger 审计 + 永不返回 openid/channel_id/phone） |
| channel 契约 | `ISsoChannel`——IdP 适配器接口（可扩展键值上下文，供 `TKWF.Federation.{平台}` 平台适配扩展实现） |
| 契约包 | `TKWF.Ext.Authentication.Abstractions`——Federation 消费面契约（`ISsoAccountQueryService`/`ISsoChannelMapService` + 不可变 DTO，零实体零 SG1） |

**组合矩阵**（消费方按需装配）：
- 只引 `TKWF.Ext.Authentication` = 内部认证（单应用登录）
- **Authentication + Federation = 认证中心实例**（内部认证 + 多应用联邦 SSO）
- Federation + 平台适配扩展 = 纯外部联邦登录（BYO IdP——V5 国外客户主力形态，经 Abstractions 引契约零全量传递）

**不包含**：具体平台适配扩展（微信/QQ/Google/Apple/微软 channel 实现，独立立项 `TKWF.Federation.{平台}`）；`/sso/*` 端点映射（装配层职责——Federation 提供 Service 层）；我方多实例互通（默认不做，iss 域隔离）。

## 二、安装与接线

### 1. 消费方引用 + 白名单启用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Federation\TKWF.Ext.Federation.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Authentication\TKWF.Ext.Authentication.csproj" />  <!-- 组合式（可选——纯外部联邦场景可只引 Federation + Abstractions） -->
```

```csharp
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]   // 组合式：认证中心实例
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

### 2. 接入平台网关库（`TKWF.Federation.{平台}`）——外交部经 `ISsoChannel` 集合窄适配编排

Federation 扩展**拥有 `ISsoChannel` 契约**（Oracle P1-1），但**不实现任何通道**——通道由平台网关库（`TKWF.Federation.WeChat`/`.QQ`/`.Google`/...）实现并经其 DI 扩展方法注册进集合（开发方案 §5.4 Oracle P1-4/评审点 7）。消费方装配链路：

```csharp
// ① 消费方（认证中心实例）领域初始化器——白名单声明 Federation 扩展（三钩子接线）
using TKWF.Ext.Federation;

[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]   // 组合式：认证中心实例
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// ② Program.cs（Web 装配层）——装配 TKWF.Federation.WeChat 平台网关库
//    库侧 AddWeChatFederationChannels() 内部 TryAddEnumerableConstructible<ISsoChannel, X>（ADR92 集合版守卫工厂）
builder.Services.AddWeChatFederationChannels();
//    通道配置经 TKWF:Federation:WeChat 配置节绑定（SG1 [Options] 自动），或编程覆盖：
builder.Services.Configure<WeChatOptions>(o => { /* 按 AppId 选区 Channels */ });

// ③ 编排——/sso/login 等装配层端点经 User.Use<门面>() 帧内枚举 ISsoChannel 集合
//    （门面 ctor 注入 IEnumerable<ISsoChannel>，ADR92 守卫工厂于帧内经 CurrentAopUser 供给通道）：
await user.Use<ISsoLogin>().LoginAsync(channelType, context, ct);
//    ⛔ 禁止 [FromServices] IEnumerable<ISsoChannel> 预绑定（帧外枚举抛守卫）
```

- **依赖方向**：`TKWF.Federation.{平台}` 库 → `TKWF.Ext.Federation` 扩展 → `Authentication.Abstractions` + 主框架（Oracle P1-1，单向无循环）。
- **未装配库 = 空集合**：Federation 初始器零 `ISsoChannel` 元素注册——未引用任何平台网关库时 `IEnumerable<ISsoChannel>` 帧内解析为空数组，"未注册通道自然跳过"（验收 F5）；装配库后元素自动进集合。
- **注册责任归属**：库无 Initializer（纯库判据）——由库侧 `AddWeChatFederationChannels()` 扩展方法注册（`Microsoft.Extensions.DependencyInjection` 命名空间）；Federation 扩展不强制引库保持核心轻量。
- ☝️ `AddWeChatFederationChannels()` 为 `TKWF.Federation.WeChat` 库提供（V0.1.0，库侧 T6）；`ISsoLogin` 登录编排门面归装配层/后续迭代，本 README 示例为其形态示意。

### 3. 配置 `TKWF:Federation`

```jsonc
{
  "TKWF": {
    "Federation": {
      "Issuer": "https://api.lexue.loongba.cn",       // 必填（生产 fail-fast）
      "SigningKeyPath": "C:\\keys\\sso-ec-private.pem", // EC P-256 私钥 PKCS#8（生产 fail-fast）
      "CurrentKid": "sso-key-1",
      "SigningKeys": [{ "Kid": "sso-key-1", "PrivateKeyPath": "C:\\keys\\sso-ec-private.pem" }],
      "SecretEncryptionKeyPath": "C:\\keys\\sso-aes.key",  // client credential/HMAC AES-GCM（前 32 字节，生产必填）
      "IsProduction": true
    }
  }
}
```

**生成 EC 私钥 PEM**（生产，PKCS#8）：
```bash
openssl ecparam -name prime256v1 -genkey -noout -out sso-ec-private.pem   # PKCS#8 EC 私钥
openssl ec -in sso-ec-private.pem -pubout -out sso-ec-public.pem
```

> ⚠️ **生产 fail-fast**：`IsProduction=true` 时缺 `Issuer` / `SigningKeyPath` / `SecretEncryptionKeyPath` → 启动拒绝（InitializeAsync 预检经系统作用域 `Use<IToken2Service>()`）。开发模式自动生成临时 EC 密钥（DevEcKeyCache 进程内缓存——重启即变，仅限开发联调）。

### 4. 消费（应用端起）

```csharp
// 门面帧内解析（AddConstructibleService 守卫工厂——禁止 [FromServices] 预绑定）
public class AppSessionService(DomainUser<MyUserInfo> user)
{
    // token2 验签（应用端离线验证：ES256 + JWKS 公钥；aud 须 == 自身 app_id）
    public async Task<Token2ValidationResult> VerifyAsync(string token2)
        => await user.Use<IToken2Service>().ValidateToken2Async(token2);

    // profile 拉取（server-to-server：Bearer app client credential + X-App-Id）
    public async Task<SsoProfileDto?> GetProfileAsync(string appId, string uid, string[] scopes)
        => await user.Use<ISsoProfileService>().GetProfileAsync(appId, uid, scopes, null);
}
```

## 三、令牌契约（token2，ES256，独立密钥域）

```json
{ "alg": "ES256", "kid": "sso-key-2026-10", "typ": "JWT",
  "iss": "https://api.lexue.loongba.cn", "sub": "<uid>", "aud": "<target_app_id>",
  "iat": 1722240000, "exp": 1722240300, "jti": "uuid", "scope": "profile:basic" }
```

- **明确不放**：openid / phone / channel_id（隐私最小化 + 解耦应用与渠道，设计文档 §6.2）
- **签名**：手写 ES256（BCL `ECDsa`，曲线强制 P-256）；验签加固——alg 强制 / FixedTimeEquals / kid 白名单 / iss 校验 / exp·iat leeway（默认 30s）/ Base64Url 边界
- **独立密钥域**：与 Authentication RS256 完全独立（算法/iss/消费方不同，不复用既有 TokenService）；kid 轮换（SigningKeys 遍历验证，旧密钥可验到过期）
- **JWKS**：`GetJwksJson()` 序列化（RFC 7517：kid + x/y 坐标）；`/.well-known/jwks.json` 端点装配层映射
- **吊销**：短 TTL 为主（默认 300s）；用户封禁/解绑 → jti 吊销列表（profile API 在线校验），app session 周期性 re-check

## 四、accesscode 契约（授权码，通道 B）

| 字段 | 规格 |
|---|---|
| 生成 | CSPRNG 32 字节 `base64url` |
| TTL | 120 秒（`FederationOptions.AccessCodeExpirationSeconds`） |
| 单次 | **原子 CAS**（`EntityUpdateWhereAsync(Id && used=false, set used=true)`——ADR89 引擎级条件 UPDATE），重放即拒 + Warning |
| 存储 | 只存 `SHA256(code)` 索引，不存原文（防库泄露后 code 盗用） |
| PKCE | 可选（签发时存 code_verifier SHA256 hash；消费必传比对，恒定时间——defense in depth，非强制） |
| 关联字段 | `channel_id, uid, target_app_id, scope, exp, used, ip, code_verifier_hash` |

**错误码**：`ACCESS_CODE_NOT_FOUND` / `TICKET_EXPIRED` / `TICKET_CONSUMED`（重放）/ `TICKET_STATE_MISMATCH`（PKCE）。

**与既有 `IOAuthTicketService` 边界**（并列不迁移）：既有 = 单应用票据（login/bind + PKCE + TTL 5min，服务认证中心内部"票据换令牌"）；Federation accesscode = 联邦授权码（channel_id + target_app_id + scope，服务跨应用联邦流）——语义不同，各自独立（ADR-SSO 3.3 并存路径）。

## 五、安全边界

- **数据访问红线合规**：全部实体 SG1 + `*EntityDataService` 委托（零 IFreeSql / 零 IEntityDAC 直注入）；Federation 经 Abstractions 契约消费认证内核（`ISsoAccountQueryService`/`ISsoChannelMapService` 返回不可变 DTO，敏感字段不出契约包）。
- **密钥安全**：EC 私钥 PEM（PKCS#8，chmod 600）/ AES-GCM 密钥文件（前 32 字节）不进代码库；生产缺密钥 fail-fast；kid 轮换支持紧急换钥。
- **防开放重定向**：`target_app_id → 注册精确 origin（scheme+host）` 白名单（`SsoClientService.IsOriginAllowedAsync`，Ordinal 精确匹配）；不接受自由 `redirect_uri` 参数。
- **client credential 校验**：AES-GCM 解密 + `CryptographicOperations.FixedTimeEquals`（防时序攻击）。
- **accesscode 只增语义**：SsoAccessCode 无 Update/Delete 公开业务方法（对齐 SecurityLog 先例）。
- **profile 审计**：ILogger 结构化日志（app_id/uid/scopes/ip/ts）——SsoProfileAuditEntity 归后续迭代（复用 SecurityLog 或自建表另议）。

## 六、架构决策记录

- 模块立项 ADR：`docs/SSO/ADR/ADR-SSO-模块立项与契约归属.md`（Oracle PASS WITH CONDITIONS）
- 开发方案：`docs/SSO/v0.1.0-SSO-认证中心联邦层-开发方案.md`（Oracle PASS WITH CONDITIONS）
- 需求设计文档：`_TCloud/docs/协作/记录/20261005-01-认证中心设计方案.md`（v4，将归档）
- 既有 ADR：认证中心命名与边界 / 令牌契约与密钥管理 / UserCenter 契约承接

## 七、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-05） | Federation 联邦层内核（归层后命名，原 SSO）：应用注册（origin 白名单 + scope + AES-GCM credential + per-channel HMAC）/ accesscode（120s 原子 CAS + SHA256 + PKCE）/ token2（手写 ES256 独立密钥域 + kid 轮换 + JWKS）/ profile API（scope 强制 + 审计）/ ISsoChannel 契约；前置 `Authentication.Abstractions` 契约包拆出（Federation 消费面：ISsoAccountQueryService/ISsoChannelMapService + DTO，零实体零 SG1）；联盟锚点数据模型（AuthAccount.FederationAnchorOpenId 列 + PlatformAccountMap 扩展）；19 测试全绿 + 全量回归通过 |

<!-- EOF -->