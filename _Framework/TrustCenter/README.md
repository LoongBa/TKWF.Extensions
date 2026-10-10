# TKWF.Ext.TrustCenter 信任中心技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0（2026-10-09 自 Federation 剥离——**纯信任内核**：应用注册（`SsoClientEntity`）/ token2（ES256 独立密钥域 + JWKS）/ accesscode（授权码 + 安全数据投递）/ `ISsoChannel` 契约（Abstractions）；编排面（SsoLogin/ISsoChannelFactory）与多通道设施归 Federation） | **发布状态**: **待发布**（2026-10-10 已准备就绪——CPM 升级 4.10.70 + `SymmetricKeyProviderKeys.TrustCenter` 主框架常量切换完成，全量 1913 用例 0 失败；统一打 tag 发布待用户同意） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain + FreeSql + Microsoft.Extensions.Caching.Memory + Microsoft.AspNetCore.App（JWKS 端点装配保留）+ 信任契约包 `TKWF.Ext.TrustCenter.Abstractions`（ProjectReference）

**中文名**: **信任中心**（2026-10-09 三层架构裁定——认证体系三层化：`AuthCenter` 身份认证（内网）＋ `TrustCenter` 应用间信任（内网）＋ `Federation` 对外连接层（外网统一出口）；TrustCenter = 纯信任内核，内网中枢零外部协议依赖）

**核心约束**: 手写 ES256 JWT（零第三方 JWT 库）/ **独立密钥域**（ECDSA P-256 PEM + kid 轮换 + JWKS，与 AuthCenter RS256 完全独立）/ accesscode（单次原子 CAS + SHA256 存储 + PKCE 可选 + **安全数据投递增强**（Payload AES-GCM 密文 + ExpectedClaimant 原子核销））/ 应用注册（origin 白名单防开放重定向 + scope + client credential AES-GCM + per-channel HMAC）/ 数据访问红线合规（全走 SG1 DataService）/ **信任内核纯签发零编排**（SsoLogin/ISsoChannelFactory 归 Federation——方案 §5.2/§5.3，TrustCenter 不编排、不连接）

---

## 一、定位

信任中心——认证体系三层中的**应用间信任层**（纯信任内核）。**内网中枢**：签发/验证跨应用统一断言（token2），管理下游 SP 应用注册，提供 accesscode 联邦授权 + 安全数据投递；**不对外**——零平台协议实现，`ISsoChannel` 契约只定义"外部如何接入信任网络"，具体平台连接经 Federation 连接层完成。

| 能力 | 说明 |
|------|------|
| 应用注册 | `SsoClientEntity`（app_id + origin 白名单 + scope + client credential + per-channel HMAC 密钥，AES-GCM 加密落库） |
| token2 签发/验证 | `Token2Service`——手写 **ES256**（BCL ECDsa，P-256 曲线强制）+ kid 轮换 + 独立密钥域（生产 PEM fail-fast / 开发临时密钥 `DevKeyCache<Token2Service.EcKeySet>`——框架 Utility.Caching）+ JWKS 分发 |
| accesscode（联邦流） | `AccessCodeEntity`——CSPRNG 32B base64url / 120s TTL / **单次原子 CAS**（ADR89 条件 UPDATE）/ 只存 SHA256(code) / **PKCE 可选**（defense in depth） |
| accesscode（安全数据投递） | **Payload + ExpectedClaimant**（方案 §5.5）——附带信息 AES-GCM 密文落库（明文不落库，容量上限 ~4068 字节）+ 可核销人进原子 CAS 条件（无 TOCTOU）+ `IssueAsync(payload,ttl,claimant)`/`PeekAsync`/`RedeemAsync` |
| 通道契约 | `ISsoChannel`——IdP 适配器接口（**契约在 TrustCenter.Abstractions**，Oracle P1-1），供 `TKWF.Federation.{平台}` 平台网关库实现；TrustCenter **只定义契约不实现任何通道** |
| 清理任务 | `CleanupExpiredAsync`——过期 accesscode 行（含 Payload 密文）清理，`AccessCodeRetentionDays` 配置化（默认 7） |
| 契约包 | `TKWF.Ext.TrustCenter.Abstractions`——信任契约（`ISsoChannel`/`IToken2Service`/`IAccessCodeService` + 不可变 DTO，零实体零 SG1） |

**信任内核纯签发（zero-orchestration）**：编排面（`ISsoChannelFactory`/`SsoLogin`）**不迁入** TrustCenter——归 Federation（方案 §5.2/§5.3 修正，Oracle 复评条件 1）。TrustCenter 消费方（含 Federation）经 `IToken2Service`/`IAccessCodeService` 契约调签发，属合法 L2 间接层。

**平台库引目标改 TrustCenter.Abstractions**（方案 §6.1 #1 定案）：8 平台库（`TKWF.Federation.WeChat`/`.QQ`/`.DingTalk`/`.WeCom`/`.Oidc`/`.Google`/`.Microsoft`/`.Alipay`）保留包名与命名空间 `TKWF.Federation.*`（大使馆独立资产，不随主包改名），仅 csproj 引目标由 `TKWF.Ext.Federation` 改 `TrustCenter.Abstractions`（实现 `ISsoChannel`，L2 门控）——避免 7-8 库 tag/nuget 全改（评审建议）。

**不包含**：具体平台适配扩展（微信/QQ/Google/Apple/微软 channel 实现，独立立项 `TKWF.Federation.{平台}`）；`/sso/*` 端点映射（装配层职责——Federation `FederationWebExtension` 提供）；AuthCenter 身份认证（token1——独立扩展）。**编排/连接/多通道选区全归 Federation 连接层**。

## 二、安装与接线

### 1. 消费方引用 + 白名单启用

```xml
<!-- 源码模式 -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\TrustCenter\TKWF.Ext.TrustCenter.csproj" />
<ProjectReference Include="..\_TKWF.Extensions\_Framework\TrustCenter.Abstractions\TKWF.Ext.TrustCenter.Abstractions.csproj" />  <!-- 契约包（消费契约必需） -->
<!-- 组合式：信任中枢实例 = TrustCenter + AuthCenter（身份）——Federation 连接层按需加装（见使用指南 4 档装配梯度） -->
```

```csharp
using TKWF.Ext.TrustCenter;

[TKWFEnabledExtension(typeof(TrustCenterExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]   // 组合式：认证中心实例（信任 + 身份）
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

> ⚠️ **发现 ≠ 启用**：TrustCenter 三钩子经白名单声明才执行——消费方显式 `[TKWFEnabledExtension(typeof(TrustCenterExtensionInitializer<>))]` 声明。

### 2. 接入平台网关库（可选——需要外部 IdP 时，经 Federation 连接层）

TrustCenter **不直接对接平台库**——`ISsoChannel` 集合由平台网关库（`TKWF.Federation.{平台}`）实现并经其 DI 扩展方法（`AddWeChatFederationChannels()` 等）注册进 `ISsoChannel` 集合，**编排归 Federation `ISsoChannelFactory`/`SsoLogin`**（帧内经 `CurrentAopUser` 供给通道）。未装配任何平台库时 `IEnumerable<ISsoChannel>` 帧内解析为空数组（"未注册通道自然跳过" F5）。详见 [`docs/TrustCenter/信任中心-使用指南.md`](../../docs/TrustCenter/信任中心-使用指南.md) 与 Federation 使用指南。

### 3. 配置 `TKWF:TrustCenter`

```jsonc
{
  "TKWF": {
    "TrustCenter": {
      "Issuer": "https://api.lexue.loongba.cn",       // 必填（生产 fail-fast）
      "SigningKeyPath": "C:\\keys\\sso-ec-private.pem", // EC P-256 私钥 PKCS#8（生产 fail-fast）
      "CurrentKid": "sso-key-1",
      "SigningKeys": [{ "Kid": "sso-key-1", "PrivateKeyPath": "C:\\keys\\sso-ec-private.pem" }],
      "Token2ExpirationSeconds": 300,                  // token2 有效期（秒，默认 300）
      "ClockSkewSeconds": 30,                          // 验签 leeway（秒，默认 30）
      "AccessCodeExpirationSeconds": 120,              // accesscode TTL（秒，默认 120）
      "AccessCodeRetentionDays": 7,                    // 过期 accesscode 清理保留天数（默认 7，≤0 跳过清理）
      "SecretEncryptionKeyPath": "C:\\keys\\trustcenter-aes.key",  // client credential/HMAC/Payload AES-GCM（前 32 字节，生产必填）
      "IsProduction": true
    }
  }
}
```

> ⚠️ **配置节变更（TrustCenter 剥离）**：原 `TKWF:Federation` 节的 token2/accesscode/签发密钥相关配置**迁移至 `TKWF:TrustCenter` 节**（Issuer/SigningKeyPath/CurrentKid/SigningKeys/AccessCode*/SecretEncryptionKeyPath/IsProduction）——消费方须改绑定节。Federation 连接层**仅保留** `TKWF:Federation:ChannelRegistry`/`TKWF:Federation:Channels`/`TKWF:Federation:Web`（通道注册表/静态通道/对外端点）——见 Federation README 破坏性变更段。

**生成 EC 私钥 PEM**（生产，PKCS#8）：
```bash
openssl ecparam -name prime256v1 -genkey -noout -out sso-ec-private.pem   # PKCS#8 EC 私钥
openssl ec -in sso-ec-private.pem -pubout -out sso-ec-public.pem
```

> ⚠️ **生产 fail-fast**：`IsProduction=true` 时缺 `Issuer` / `SigningKeyPath` / `SecretEncryptionKeyPath` → 启动拒绝（`InitializeAsync` 预检经系统作用域 `Use<IToken2Service>()` 触发密钥懒加载 `EnsureKeysLoaded`）。开发模式自动生成临时 EC 密钥 + Warning（`DevKeyCache<Token2Service.EcKeySet>` 进程内缓存——框架 Utility.Caching；重启即变，仅限开发联调）。

### 4. 消费（应用端 / Federation 编排面）

```csharp
// 门面帧内解析（AddConstructibleService 守卫工厂——禁止 [FromServices] 预绑定）
public class AppSessionService(DomainUser<MyUserInfo> user)
{
    // token2 验签（应用端离线验证：ES256 + JWKS 公钥；aud 须 == 自身 app_id）
    public async Task<Token2ValidationResult> VerifyAsync(string token2)
        => await user.Use<IToken2Service>().ValidateToken2Async(token2);

    // token2 签发（Federation SsoLogin 编排链末端——经契约调签发，合法 L2 间接层）
    public async Task<Token2IssueResult> IssueAsync(string uid, string appId, string? scope)
        => await user.Use<IToken2Service>().IssueToken2Async(new Token2IssueRequest(uid, appId, scope));
}
```

### 5. 表结构（2 实体——`TKWF_SsoClient`/`TKWF_AccessCode`）

| 表 | 关键列/约束 |
|----|-----------|
| `TKWF_SsoClient` | AppId(64 **唯一**——token2 `aud`)/OriginWhitelist(512 JSON——防开放重定向，scheme+host 精确集合)/Scopes(256 JSON，默认 `["profile:basic"]`)/ClientSecretEncrypted(512 AES-GCM)/HmacSecretEncrypted(512 AES-GCM——per-channel + per-app HMAC，`/sso/issue` 验签来源 Oracle P1-4)/IsEnabled |
| `TKWF_AccessCode` | CodeHash(64 **唯一**——只存 SHA256(code))/ChannelId(64)/TargetAppId(64)/UId(32)/Scope(128?)/ExpiresAt/Used/CodeVerifierHash(64? PKCE 可选)/**PayloadEncrypted(4096——AES-GCM 密文列，方案 §5.5）**/**ExpectedClaimant(64?)**/CreateTime/UpdateTime |

## 三、令牌契约（token2，ES256，独立密钥域）

```json
{ "alg": "ES256", "kid": "sso-key-2026-10", "typ": "JWT",
  "iss": "https://api.lexue.loongba.cn", "sub": "<uid>", "aud": "<target_app_id>",
  "iat": 1722240000, "exp": 1722240300, "jti": "<32B base64url>", "scope": "profile:basic" }
```

- **明确不放**：openid / phone / channel_id（隐私最小化 + 解耦应用与渠道，设计文档 §6.2）
- **签名**：手写 ES256（BCL `ECDsa`，曲线强制 P-256）；验签加固——alg 强制 / FixedTimeEquals / kid 白名单 / iss 校验 / exp·iat leeway（默认 30s）/ Base64Url 边界
- **独立密钥域**：与 AuthCenter RS256 完全独立（算法/iss/消费方不同，不复用既有 TokenService）；kid 轮换（SigningKeys 遍历验证，旧密钥可验到过期）
- **JWKS**：`GetJwksJson()` 序列化（RFC 7517：kid + x/y 坐标）；`/.well-known/jwks.json` 端点装配层映射（Federation `FederationWebExtension` `GET {prefix}/jwks`）
- **吊销**：短 TTL 为主（默认 300s）；用户封禁/解绑 → jti 吊销列表（profile API 在线校验），app session 周期性 re-check

## 四、accesscode 契约（联邦授权码 + 安全数据投递）

### 通路 A——联邦授权码（SSO 联邦流，`AccessCodeIssueRequest` 重载）

| 字段 | 规格 |
|---|---|
| 生成 | CSPRNG 32 字节 `base64url` |
| TTL | 120 秒（`TrustCenterOptions.AccessCodeExpirationSeconds`） |
| 单次 | **原子 CAS**（`EntityUpdateWhereAsync(Id && used=false, set used=true)`——ADR89 引擎级条件 UPDATE），重放即拒 + Warning |
| 存储 | 只存 `SHA256(code)` 索引，不存原文（防库泄露后 code 盗用） |
| PKCE | 可选（签发时存 code_verifier SHA256 hash；消费必传比对，恒定时间——defense in depth，非强制） |
| 关联字段 | `channel_id, uid, target_app_id, scope, exp, used, ip, code_verifier_hash` |

### 通路 B——安全数据投递（业务层选用，方案 §5.5）

| 方法 | 语义 |
|------|------|
| `IssueAsync(string? payloadJson, TimeSpan ttl, string? expectedClaimant, [...] )` | 签发带附带信息的 accesscode——payloadJson 非空时 **UTF8 明文 ≤ 4068 字节**（4096 - 12 nonce - 16 tag，AES-GCM 密文列 `MaxLength(4096)`；超限抛 `PAYLOAD_TOO_LARGE`）；经 keyed `ISymmetricKeyProvider`（`TrustCenterKeyProviderKeys.TrustCenter` 过渡常量——主框架 `SymmetricKeyProviderKeys.TrustCenter` 转正后切换）AES-GCM 加密落 `PayloadEncrypted`（**明文不落库**）；`expectedClaimant` 可空 = 可转让（任意核销人可消费）；`ttl ≤ 0` → 钳制为配置默认（120） |
| `PeekAsync<T>(code)` | **只读快照非锁定**——查行 → 过期拒 → 解密 payload + 反序列化 `<T>`；**不消费不锁定**——业务层不得基于 Peek 结果做不可逆决策（不可逆动作一律经 `RedeemAsync` 原子 CAS 恰一成功） |
| `RedeemAsync<T>(code, claimant)` | **核销取回 + 销毁（单次原子 CAS）**——`EntityUpdateWhereAsync(WHERE CodeHash=? AND used=false AND (ExpectedClaimant IS NULL OR ExpectedClaimant=claimant), set Used=true)`：**核销人 + 并发核销联合判定无 TOCTOU**，恰一成功；影响 0 → 重查判因（`ACCESS_CODE_NOT_FOUND`/`TICKET_CONSUMED`/`CLAIMANT_MISMATCH`/`TICKET_EXPIRED`）；成功 → 解密 payload 取回 + 销毁（Used=true） |
| `CleanupExpiredAsync(retentionDays?)` | 清理过期行**含 Payload 密文**（防业务数据残留）——`ExpiresAt < UtcNow - retentionDays`（默认 `AccessCodeRetentionDays`=7；≤ 0 跳过清理）；**消费方经 `IRecurringBackgroundJobManager` 周期调度**（对齐 Metrics 定时重算范式） |

**错误码**：`ACCESS_CODE_NOT_FOUND` / `TICKET_EXPIRED` / `TICKET_CONSUMED`（重放）/ `TICKET_STATE_MISMATCH`（PKCE）/ `CLAIMANT_MISMATCH`（核销人不符）/ `PAYLOAD_TOO_LARGE`（明文超 ~4068 字节）。

**用途由业务层决定**（人工核验/自动核验——凭证兑换/链接投递等），TrustCenter 只提供能力不规定用途（开发方案 §二 不包含 #1 落档转告记录）。

**与 AuthCenter `IOAuthTicketService` 边界**（并列不迁移）：既有 = 单应用票据（login/bind + PKCE + TTL 5min，服务认证中心内部"票据换令牌"）；TrustCenter accesscode = 跨应用联邦授权码 + 安全数据投递（channel_id + target_app_id + scope + Payload）——语义不同，各自独立。

## 五、安全边界

- **数据访问红线合规**：全部实体 SG1 + `*EntityDataService` 委托（零 IFreeSql / 零 IEntityDAC 直注入）；TrustCenter 纯签发——不排列、不连接，零外部协议库引用（grep 断言零 `Federation.` 平台库引用，验收 F1）。
- **密钥安全**：EC 私钥 PEM（PKCS#8，chmod 600）/ AES-GCM 密钥文件（前 32 字节）不进代码库；生产缺密钥 fail-fast；kid 轮换支持紧急换钥。**对称密钥经 keyed `ISymmetricKeyProvider`**——✅ **主框架常量 `SymmetricKeyProviderKeys.TrustCenter`**（v4.10.70 C11 第 5 扩展边界兑现——`TrustCenterKeyProviderKeys` 过渡常量已删，单一事实源防 typo 静默解析错 key，1913 用例 0 失败验证切换零行为差异）。
- **防开放重定向**：`target_app_id → 注册精确 origin（scheme+host）` 白名单（`SsoClientService.IsOriginAllowedAsync`，Ordinal 精确匹配）；不接受自由 `redirect_uri` 参数。
- **client credential 校验**：AES-GCM 解密 + `CryptographicOperations.FixedTimeEquals`（防时序攻击）。
- **accesscode 只增语义**：`AccessCodeEntity` 无 Update/Delete 公开业务方法（对齐 SecurityLog 先例）。
- **Payload 密文**：附带信息 AES-GCM 密文落库（明文不落库）；`Peek`/`Redeem` 解密失败（密文被篡改/密钥不匹配）→ `CryptographicException`/`FormatException`——**fail-closed 拒信任，不静默降级**。

## 六、架构决策记录

- 剥离与三层架构开发方案：`docs/TrustCenter/TrustCenter剥离与三层架构-开发方案.md`（Oracle 初评 PASS WITH CONDITIONS + 批判性复评附条件通过——5 项条件全部吸收后可实施）
- 既有 ADR：`ADR-AuthCenter-归层与命名`（四层归层模型，本方案在三层化演进）；`ADR-SSO-模块立项与契约归属`（令牌契约与密钥管理基础）
- 转告：`docs/TrustCenter/转告-SymmetricKeyProviderKeys申请并入TrustCenter键.md`（C11 第 5 扩展，等框架组并入后统一发布）

## 七、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-10-09，**待发布**） | **TrustCenter 信任内核（自 Federation 剥离，Phase 1-4）**：项目/命名空间/MinVerTagPrefix（`TrustCenter/v`）确立；`SsoClientEntity`（应用注册——origin 白名单 + scope + AES-GCM credential + per-channel HMAC）/ `Token2Service`（手写 ES256 独立密钥域 + kid 轮换 + JWKS + `DevKeyCache<EcKeySet>`）/ `AccessCodeEntity` + `AccessCodeService`（联邦授权码 Issue/Consume + **安全数据投递增强** Payload + ExpectedClaimant + Issue/Peek/Redeem + CleanupExpiredAsync）/ `TrustCenterExtensionInitializer`（AddConstructibleService 三门面 + keyed `ISymmetricKeyProvider` + DevKeyCache + 生产 fail-fast 预检）；`TrustCenter.Abstractions` 契约包（`ISsoChannel`/`IToken2Service`/`IAccessCodeService` + 不可变 DTO，零实体零 SG1）；编排面（SsoLogin/ISsoChannelFactory）与多通道设施归 Federation；25 测试用例 + 全量回归 0 失败（Phase 1-4 累计 1913 用例）。**✅ v4.10.70 适配（2026-10-10）**：CPM 4.10.69→4.10.70 lockstep + 删 `TrustCenterKeyProviderKeys` 过渡类 + 3 处代码点/5 处注释切 `SymmetricKeyProviderKeys.TrustCenter`（C11 第 5 扩展边界兑现，零行为差异）——统一发布待用户同意 |

<!-- EOF -->