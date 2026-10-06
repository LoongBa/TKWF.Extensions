# TKWF.Ext.AuthCenter 认证中心扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.8.0（2026-10-06 认证 API 补全——内建 3 端点 `/verify`（已认证内省）`/grants`（应用授权状态）`/sms/verify`（验证码独立校验）+ **B5 票据绑定缺口闭环**（`OAuthTicketIssueRequest.UserId` 签发即绑 + `BindTicketAsync` 已认证帧补绑 + CAS 条件更新）；**`AuthGrantEntity` 应用授权数据底座**（唯一索引 `UX_AuthGrant_User_App_Source(UserId,AppId,Source)`——`IAuthGrantQueryService`/`IAuthGrantCommandService` 双门面，exchange 成功经写入门面落登录授权）+ 配置分层（3 端点开关进 `AuthCenterEndpointOptions` `TKWF:AuthCenter:Web`）；Oracle 评审 PASS WITH CONDITIONS 修订闭环；前版 V0.7.0 E4 密钥管理抽象、V0.6.0 归层迭代） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain + FreeSql + Microsoft.Extensions.Caching.Memory + FrameworkReference Microsoft.AspNetCore.App（路径 B 中间件 + 内建端点）

**核心约束**: 手写 RS256 JWT（零第三方 JWT 库）/ 密钥持久化 PEM + kid 轮换 / 黑名单落库 + IMemoryCache 短 TTL / Refresh rotation + TokenVersion 闭环 / Provider 认证矩阵（fail-closed）/ 数据访问红线合规（全走 SG1 DataService）/ 身份适配层 AuthorityFilter 零改动

---

## 一、定位

认证中心——从 DMP-Lite AuthCenter 抽取的 TKWF.Extension 通用认证组件，多业务线（生活服务电商 + 教育工具系列 + 并行业务系统）独立装配部署复用；**完成后 DMP-Lite 改用本扩展**（用户裁定 2026-09-30）。

| 能力 | 说明 |
|------|------|
| 令牌体系 | `TokenService`（手写 RSA RS256 + kid 轮换 + 持久化密钥 fail-fast + 黑名单落库 + Refresh rotation + TokenVersion 闭环） |
| 认证矩阵 | `IAuthenticationProvider` Provider 框架（`EnabledAuthTypes` 配置化启用，fail-closed）+ **短信验证码**内置（`SmsVerificationService` + `ISmsSender` 抽象）+ **微信 OAuth 双形态**内置（网页授权 snsapi_base / 扫码 snsapi_login） |
| 登录保护 | `AuthLoginAttemptEntity` 限流/审计 + 策略配置（短信 60s/小时/天/IP + OAuth 10 次/分钟/IP + 口令兑换 5 次/小时） |
| 票据换令牌 | `OAuthTicketEntity` TTL 5min 单次 + **PKCE** + app_id/redirect_uri 白名单 + state 防重放 |
| 身份适配层 | `JwtDomainUserParser`（Parse 内部强制 Verify）/ `ITokenVerifier` / `IAuthorizationMapper<TUserInfo>` / `AuthenticationUserHelperBase<TUserInfo>` / `JwtAuthenticationMiddleware`（路径 B Bearer JWT 恢复）——**AuthorityFilter 零改动** |
| 跨系统映射 | `PlatformAccountMapEntity`（平台内部 id ↔ 业务 app + 业务本地 id + UnionId——统一 DMP 双机制） |
| 平台凭证 | `PlatformCredentialEntity`（AppSecret **AES-GCM 加密**在服务层 `PlatformCredentialService`——经 keyed `ISymmetricKeyProvider`；DataService 纯持久化）+ `WeChatApiClient`（access_token 缓存 + 并发锁） |
| 平台账号 | `AuthAccountEntity`（手机号主键 + 微信绑定 + 认证声明 teacher_verified/auth_level，**不含业务角色**） |
| 账号查询契约（V0.2.0） | `IAuthAccountQueryService`——对外只读查询（4 方法），委托 DataService（红线合规） |
| **UserCenter 终态承接（V0.2.0）** | 实现 `IUserProfileSource`（`AuthAccountUserProfileSource`）——数据属主扩展实现他扩展读取契约，装配实例零桥接 |

**用户中心（档案面）独立立项**（用户裁定 2026-09-30）——`TKWF.Ext.UserCenter` 另行立项（档案项目独立）；认证中心 **v0.2.0 实现其 `IUserProfileSource` 读取契约（终态承接）**。

## 二、令牌契约（冻结）

```json
{ "iss": "<auth-instance-id>", "sub": "user:<平台内部id>", "userId": "u_xxxx",
  "authType": "sms | wechat | password | redeem", "auth_level": 1,
  "teacher_verified": false, "exp": 1722243600, "iat": 1722240000,
  "jti": "unique-token-id", "kid": "rsa-key-2026-07" }
```

- **不含业务角色**——令牌只回答「你是谁」；业务角色由各业务系统 `IAuthorizationMapper.MapRoles(sub, claims)` 本地映射。
- **签名**：RS256（RSA PKCS#1 v1.5 SHA-256）；`kid` 标识密钥版本（JWK RFC 7517 语义），支持轮换。
- **生命周期**：Access 2h / Long-lived 7d（壳端低敏）/ Refresh 30d rotation（SHA256 落库，新旧不可复用）/ 一次性票据 5min 单次。
- **🔒 验签安全加固（Oracle C1）**：alg 强制 RS256（拒 none/HS256）/ `CryptographicOperations.FixedTimeEquals` / RSA≥2048 fail-fast / exp·iat 校验 / kid 白名单（防注入）/ iss 校验（多实例隔离）/ Base64Url 边界。

## 三、安装与接线

### 1. 消费方引用 + 白名单启用（V4.9.85 必需）

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\..\_Framework\Authentication\TKWF.Ext.AuthCenter.csproj" />
<!-- ⚠️ V0.6.0 契约包拆分（SSO 消费面）：使用 Federation/SSO 联邦契约（ISsoAccountQueryService /
     ISsoChannelMapService / ISsoAccountLinkService + 不可变 DTO）的消费方须**同时**引用独立契约包
     TKWF.Ext.AuthCenter.Abstractions——SSO 契约已迁出主 DLL，只引主 DLL 会致接口继承解析断裂误报
     （DI005，EduPlatform 实证 2026-10-05）；仅内部认证（不消费 SSO 契约）可不引。
     契约包零实体零 SG1，命名空间保持 TKWF.Ext.AuthCenter -->
<ProjectReference Include="..\..\_Framework\AuthCenter.Abstractions\TKWF.Ext.AuthCenter.Abstractions.csproj" />
```

```csharp
using TKWF.Ext.AuthCenter;

[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // 自动注册（V4.10.53 领域自治根治，ADR90——正确路线三态；V4.10.55 ADR92/T3 闭环增强）：
    //   门面（AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory，消费方 User.Use<接口>() 解析）：
    //       ITokenService / IAuthLoginAttemptService / IOAuthTicketService / ISmsVerificationService /
    //       IPlatformCredentialService / IPlatformAccountMapService / IWeChatApiClient / ITokenVerifier /
    //       IAuthAccountQueryService / IAuthAccountService
    //       + 应用授权双门面（V0.8.0）：IAuthGrantQueryService / IAuthGrantCommandService
    //       + 登录编排门面（V0.5.0）：ISmsLoginService / IWechatLoginService——控制器经此编排，禁 [FromServices] 集合
    //   接线型（TryAddScoped 普通 DI）：IUserProfileSource（UserCenter 档案源——非 IDomainService 契约，被门面 GetService 解析）
    //   多 Provider（TryAddEnumerableConstructible——V0.5.0 集合版守卫工厂）：IAuthenticationProvider（短信 + 微信）
    //   + 8 实体 DataService（ADR61 消费方聚合自动注册——Initializer 零手动注册）
}
```

> **⚠️ V0.5.2 消费约束（fcbffd1 受理终态，Oracle 裁决——Bearer 受保护端点 500 修复）**：`ITokenVerifier` 为**守卫工厂**（`AddConstructibleService`——帧内 `CurrentAopUser` 供给 ctor `IDomainUser`，帧外解析抛守卫）。**两条无 AOP 帧消费路径已修复**：
> - `JwtAuthenticationMiddleware`（路径 B Bearer 恢复）——中间件内改经**游客帧**解析：`context.Items["DomainUser"]` 的游客 `DomainUser` → `guest.Use<ITokenVerifier>()`（验签 = 匿名请求者证明身份，与 LoginAs 同属 Guest 行为，非 System 操作）。原裸 `GetService` 帧外抛守卫 → 带 Bearer 受保护端点 500。**⚠️ 装配约束：`AuthCenterWebExtension` 须与 `UseWebSession` 同装配**（ContextExtraction 阶段 2 才写入游客 DomainUser；未装配则无游客 → 降级裸 GetService 抛守卫 = 正确 fail，非静默降级）。
> - `AuthenticationUserHelperBase.RestoreFromTokenAsync`（登录票据换令牌）——改 `user.Use<ITokenVerifier>()`（AOP 帧内，设 CurrentAopUser=user）。原 `DomainUser.GetService` 直通不设帧 → 同样 500。
> **消费方（EduPlatform 等）注意**：控制器**禁止** `[FromServices] ITokenVerifier` 预绑定（模型绑定阶段无帧 → 帧外抛守卫）——验签归中间件/登录门面链路，表现层无需直取。
>
> **⚠️ V0.5.1（过渡形态，未发布）**：曾以 System 作用域（`BeginSystemScopeAsync` + `System.Use`）实现——语义错置（验签是 Guest 行为非 System 操作），经 Oracle 评审否决，V0.5.2 定稿游客帧。
>
> **⚠️ V0.5.0 消费约束（EduPlatform 3 端点修复，ADR92/T3 闭环）**：控制器**禁止**
> `[FromServices] IEnumerable<IAuthenticationProvider>` 预绑定（帧外枚举抛守卫）——短信/微信登录改经登录编排门面：
> `User.Use<ISmsLoginService>().LoginAsync(phone, code)` / `User.Use<IWechatLoginService>().LoginAsync(code, scope)`
> （门面 ctor 帧内枚举 Provider 集合，守卫工厂经 CurrentAopUser 供给）——见使用指南 §登录。

### 2. 登录衔接（路径 A——业务系统自己触发登录）

消费方 UserHelper 继承 `AuthenticationUserHelperBase<TUserInfo>`，仅实现两个工厂方法：

```csharp
public class MyUserHelper : AuthenticationUserHelperBase<MyUserInfo>
{
    // 短信登录：AuthAccountEntity + 本地角色 → MyUserInfo（Account.UId / Account.Phone）
    protected override MyUserInfo CreateUserInfoFromAccount(AuthAccountEntity account, IReadOnlyList<string> roles)
        => new MyUserInfo(account.UId, account.Phone ?? account.UId) { Roles = roles.ToList() };

    // 微信/OAuth 恢复：令牌载荷 + 本地角色 → MyUserInfo
    protected override MyUserInfo CreateUserInfoFromToken(TokenValidationResult token, IReadOnlyList<string> roles)
        => new MyUserInfo(token.UserId, token.UserId) { Roles = roles.ToList() };
}
```

### 3. Bearer JWT API 消费（路径 B——主框架缺口补齐）

> **v4.10.45 收敛迁移（破坏性变更）**：旧静态方法 `AddJwtAuthentication` + `UseTkfwJwtAuthentication` **已删除**——
> Web 装配收敛为 `AuthCenterWebExtension<TUserInfo>`（Web 装配钩子 ADR87/D22/G18，锚点默认
> `BeforeAuthentication`——ContextExtraction 之后 / HttpAuthentication 之前，验签恢复 DomainUser 供框架认证判定）。

```csharp
// Program.cs——Web 装配钩子一次声明（UseWebExtensions；RestoreUser 委托直传，不经 Options）
builder.ConfigWebAppDomain<MyUserInfo, MyDomainInitializer, DomainWebOptions>(...)
    .UseWebSession()
    .UseWebExtensions(e => e.Add<AuthCenterWebExtension<MyUserInfo>>(x =>
    {
        x.RestoreUser = (httpContext, tokenResult) =>
            Task.FromResult(/* 消费方 UserHelper 内经 CreateUserInstance() 构建已认证 DomainUser<MyUserInfo> */);
    }))
    .BeforeRouting(...)
    .AfterRouting(...)
    .Build(...);
// ITokenVerifier 等业务服务由 Domain 钩子 AuthCenterExtensionInitializer 注册（领域自治，非 Web 钩子职责）
```

> **⚠️ 短路语义修正（v4.10.45 随收敛迁移）**：`JwtAuthenticationMiddleware` 短路判定由"Items 已有
> `DomainUser<TUserInfo>` 即跳过"修正为"**已认证**（`IsAuthenticated`）才跳过"——ContextExtraction 阶段 2
> 恒写匿名游客 DomainUser，原判定在 UseWebSession 全链下恒真 → JWT 验签永不执行（API 场景失效）；修正后
> 认证会话先到短路（浏览器场景），游客 + Bearer 走 JWT 验签（API 场景）。由 `JwtAuthenticationWebHookIntegrationTests`
> 黑盒哨兵冒烟锁定（顺序串 ContextExtraction → JwtAuth 验签 → HttpAuthentication）。

### 4. 配置选项（`TKWF:AuthCenter` 节）

```jsonc
{
  "TKWF": {
    "AuthCenter": {
      "Issuer": "auth-instance-1",                 // 必填（生产 fail-fast）
      "SigningKeyPath": "/keys/rsa-private.pem",   // 必填（生产 fail-fast；开发自动生成临时密钥 + Warning）
      "CurrentKid": "rsa-key-1",
      "SigningKeys": [ { "Kid": "rsa-key-1", "PrivateKeyPath": "/keys/rsa-private.pem" } ],
      "EnabledAuthTypes": ["sms", "wechat"],        // fail-closed：集合外 Provider 不接线
      "RedirectUriWhitelist": ["https://app.example.com/callback"],
      "SecretEncryptionKeyPath": "/keys/credential-aes.key",  // 平台凭证 AES-GCM（前 32 字节；生产必填）
      "IsProduction": true,                          // fail-fast 门
      "LoginProtection": { "SmsResendIntervalSeconds": 60, "OAuthPerMinutePerIp": 10 }
    }
  }
}
```

## 四、核心组件清单

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`ITokenService`** | 签发/验证/刷新/撤销（手写 RS256 + kid + 黑名单 + rotation） | `TokenService`（internal sealed，本扩展） |
| **`ITokenVerifier`** | 令牌验证（本地公钥验签；VerifyMode.RemoteIntrospection 装配层替换）——**守卫工厂注册**（帧内供给）；**消费须在 AOP 帧内**（中间件经游客帧 `guest.Use` / UserHelper 经 `user.Use`，V0.5.2 终态） | `LocalJwtTokenVerifier`（本扩展） |
| **`IAuthenticationProvider`** | 认证矩阵 Provider（EnabledAuthTypes fail-closed；V0.5.0 改 TryAddEnumerableConstructible 集合版守卫工厂——帧内经 CurrentAopUser 供给，禁帧外枚举） | `SmsAuthenticationProvider` + `WeChatAuthenticationProvider`（本扩展） |
| **`ISmsLoginService`（V0.5.0）** | 短信登录编排门面（表现层零编排终态——验证码校验→查/建账号→ProviderAuthenticateResult；控制器 `User.Use<>()` 帧内编排，替代集合直注） | `SmsLoginService`（internal sealed，本扩展） |
| **`IWechatLoginService`（V0.5.0）** | 微信登录编排门面（code→openid→查/建账号→ProviderAuthenticateResult；snsapi_base/snsapi_login scope 透传） | `WechatLoginService`（internal sealed，本扩展） |
| **`ISmsVerificationService`** | 短信验证码发送/校验（频控 + 单次消费 + SHA256 落库） | `SmsVerificationService`（本扩展） |
| **`ISmsSender`** | 短信发送渠道抽象（**消费方实现**——腾讯云等） | 无默认（TryAdd 语义） |
| **`IAuthorizationMapper<TUserInfo>`** | 业务角色本地映射（sub+claims → 角色；**消费方实现**） | 无默认（TryAdd 语义） |
| **`IOAuthTicketService`** | 一次性票据签发/消费（PKCE + 白名单 + 防重放） | `OAuthTicketService`（本扩展） |
| **`IAuthLoginAttemptService`** | 登录尝试记录 + 限流窗口 | `AuthLoginAttemptService`（本扩展） |
| **`IPlatformAccountMapService`** | 跨系统映射（Link upsert / 双向 / UnionId） | `PlatformAccountMapService`（本扩展） |
| **`IPlatformCredentialService`** | 平台凭证管理（AES-GCM 加密在**服务层**——V0.7.0 自 DataService 边界上移，密钥经 keyed `ISymmetricKeyProvider`） | `PlatformCredentialService`（本扩展） |
| **`IWeChatApiClient`** | 微信 API（access_token 缓存 + 并发锁 + 凭证解析） | `WeChatApiClient`（本扩展） |
| **9 实体 + DataService** | AuthAccount/AuthLoginAttempt/SmsRecord/AuthRefreshToken/AuthTokenBlacklist/OAuthTicket/PlatformAccountMap/PlatformCredential/**AuthGrant（V0.8.0 应用授权）** | SG1 + xCodeGen（.g.cs 入库） |
| **`IAuthAccountQueryService`（V0.2.0）** | 对外只读查询契约（ByUId/ByPhone/ByWechatMpOpenId/ByWechatWebOpenId——返回完整 `AuthAccountEntity`） | `AuthAccountQueryService`（internal sealed，本扩展，委托 `AuthAccountEntityDataService`） |
| **`IAuthAccountService`（V0.2.0）** | 对外写契约（Create/Update/IncrementTokenVersion/GetByUId——DMP 渐进替换影子账号 upsert，ADR-Authentication-账号写契约） | `AuthAccountService`（internal sealed，本扩展，委托 `AuthAccountEntityDataService`） |
| **`IAuthGrantQueryService`（V0.8.0）** | 应用授权**查询**门面（只读——`/grants` 端点数据底座：`GetGrantsAsync(userId, appId?)` 按用户/应用查询有效授权，Status=Active 按 CreateTime 倒序） | `AuthGrantQueryService`（internal sealed，本扩展，委托 `AuthGrantEntityDataService`） |
| **`IAuthGrantCommandService`（V0.8.0）** | 应用授权**写入**门面（`RecordLoginGrantAsync` upsert 幂等——`OAuthTicketService.ExchangeAsync` 成功经 `User.Use` 落登录授权；唯一约束 `UX_AuthGrant_User_App_Source` 兜底 TOCTOU；[AllowAnonymousFlag] 匿名面） | `AuthGrantCommandService`（internal sealed，本扩展，委托 `AuthGrantEntityDataService`） |
| **`IUserProfileSource`（V0.2.0）** | UserCenter 档案读取契约（AuthAccount 属主终态承接——`GetProfileAsync` 返回 `UserProfileDto`） | `AuthAccountUserProfileSource`（public sealed，本扩展） |

## 四之二、V0.2.0 查询契约与 UserCenter 承接

**`IAuthAccountQueryService`（查询契约）**——对外只读查询 4 方法（`GetByUIdAsync`/`GetByPhoneAsync`/`GetByWechatMpOpenIdAsync`/`GetByWechatWebOpenIdAsync`），返回完整 `AuthAccountEntity`；实现 `AuthAccountQueryService`（internal sealed）委托 `AuthAccountEntityDataService`——**红线合规**（零 IFreeSql/IEntityDAC 直注入）。

**`IAuthAccountService`（写契约）**——对外写契约（`CreateAsync`/`UpdateAsync`/`IncrementTokenVersionAsync`/`GetByUIdAsync`——upsert 流单注入便利），实现 `AuthAccountService`（internal sealed）委托 `AuthAccountEntityDataService`；**DMP 渐进替换路径**（ADR-Authentication-账号写契约）：平台管理员影子 AuthAccount（`UId=PlatformAdmin.UId`，`IsEnabled=true`，Phone 可空）由消费端创建/更新/失效——扩展 `TokenService.RefreshTokenAsync`（L196-200）强依赖 AuthAccount 记录。**`AdminDeleteAsync` 不暴露**（破坏性，管理 API 迭代）。

**`AuthAccountUserProfileSource`（UserCenter 终态承接）**——public sealed，实现 `IUserProfileSource`：
- **映射矩阵**：UserId←UId / Phone←Phone（**原始值**——UserCenter 门面强制脱敏，实现方不得自行 Mask）/ Nickname / AvatarUrl←Avatar / IsTeacherVerified←TeacherVerified / AuthLevel←AuthLevel
- **微信绑定推导**：`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` **任一非空 = 已绑定**（AuthAccount 无显式 IsWechatBound 字段）
- **注册**：`AuthCenterExtensionInitializer.ConfigureServices`——`IAuthAccountQueryService` 经 **AddConstructibleService**（门面，`User.Use<接口>()` 解析）+ `IUserProfileSource` 经 **TryAddScoped**（接线型，UserCenter 门面 GetService 解析）——消费方白名单声明认证中心 + UserCenter 后 `IUserCenterQueryService.GetProfileAsync` 自动获得真实档案；新装配实例**无需写桥接类**
- **依赖**：引 `TKWF.Ext.UserCenter.Abstractions`（**契约包非主包**——L2 门控合规）
- **边界**：两契约**不可合并**——查询服务返回完整 `AuthAccountEntity`（含 TokenVersion/IsEnabled，供 TokenService/装配桥接），档案源返回 `UserProfileDto`（公共档案子集，无敏感字段，Phone 门面脱敏）

## 五、实体表结构（9 张）

| 表 | 关键列/约束 |
|----|-----------|
| `AuthAccount` | UId(32 唯一)/Phone(20 **可空**唯一——微信便捷账号无手机号)/PasswordHash?/WechatMpOpenId(唯一)/WechatWebOpenId(唯一)/UnionId?/TeacherVerified/AuthLevel/TokenVersion/IsEnabled/**FederationAnchorOpenId?**（V0.6.0 联盟锚点——联邦 SSO 账号锚定列，**唯一**；保留 UnionId 微信开放平台原语义，见 Federation 联盟锚点数据模型） |
| `AuthLoginAttempt` | UserIdentity(100)+AuthType(20)+IsSuccess+IpAddress?+FailReason?+AttemptTime；索引 (UserIdentity,AuthType,AttemptTime) |
| `SmsRecord` | Phone+Scene+CodeHash(SHA256 不存明文)+IsVerified+ExpireAt；索引 (Phone,Scene,CreateTime)/(IpAddress,CreateTime) |
| `AuthRefreshToken` | Jti/UserId+TokenHash(SHA256 唯一)/TokenVersion/ExpiresAt/IsRevoked/RevokedAt?；索引 (UserId,TokenVersion) |
| `AuthTokenBlacklist` | Jti(唯一)/UserId/ExpiresAt/RevokedAt/Reason——条目 TTL=token 自然过期 |
| `OAuthTicket` | Ticket(唯一高熵)/TicketType(login/bind)/AppId/RedirectUri/State?/CodeVerifierHash?/UserId?/ExpiresAt/IsConsumed |
| `AuthGrant`（**V0.8.0**） | UserId(50)/AppId(100)/Scopes(500 逗号分隔)/**ValidUntil?（应用授权有效期——null=持续至吊销，非会话有效期）**/Source(20：login/redeem)/Status(int 0 Active/1 Revoked)/CreateTime/UpdateTime；**唯一索引 `UX_AuthGrant_User_App_Source(UserId,AppId,Source)`**（防并发 exchange 重复 grant 行——写入门面 upsert 以此复合键冲突判定） |
| `PlatformAccountMap` | PlatformAccountId+BusinessAppId+BusinessLocalId(唯一)/UnionId?/**ChannelId?+ExternalUserId?**（V0.6.0 联邦通道映射——IdP 通道 + 外部用户 Id；新索引 `UX_PlatformAccountMap_Channel`，既有 UX 保留） |
| `PlatformCredential` | Platform+AppType+AppId(唯一)/AppSecretEncrypted(AES-GCM 密文——服务层 `PlatformCredentialService` 经 keyed `ISymmetricKeyProvider` 加解密，DtoFieldIgnore 不外泄)/IsEnabled |

> ⚠️ **V0.6.0 升级迁移提示（EduPlatform 实证 2026-10-05）**：`AuthAccount` 新增 `FederationAnchorOpenId` 列、`PlatformAccountMap` 新增 `ChannelId`/`ExternalUserId` 列（+ `UX_PlatformAccountMap_Channel` 索引）——**旧表升级需迁移**（SyncStructure 开发环境自动；生产走迁移脚本/DBA），否则 Federation 联邦流 sms/login 相关路径 500（列缺失）。

## 六、安全边界

- **数据访问红线合规**：全部实体 SG1 + `*EntityDataService` 委托（非泛型 `DomainDataServiceBase`——ADR61）；扩展零 IFreeSql/IEntityDAC 直注入（Service 层只委托 DataService）。
- **密钥安全**：PEM 私钥（chmod 600）/ AES-GCM 密钥文件（前 32 字节）不进代码库；生产缺密钥 fail-fast 拒绝启动；kid 轮换支持紧急换钥。**对称密钥经框架（v4.10.61）`ISymmetricKeyProvider`/`FileSymmetricKeyProvider`（Domain.KeyManagement）keyed 注册（`SymmetricKeyProviderKeys.AuthCenter`）持有 + `DevKeyCache<T>`（Utility.Caching）缓存开发临时密钥**——开发临时密钥进程内共享（重启即变），生产缺密钥 fail-fast。
- **只增语义**：AuthLoginAttempt/SmsRecord/AuthRefreshToken/AuthTokenBlacklist 无 Update/Delete 公开业务方法（对齐 SecurityLog 先例）。
- **密码修改处置（Oracle C2）**：`TokenVersion++` 后存量 2h access 靠短 TTL 自然失效（不做 access 级主动撤销）；高安全场景 v0.2.0 加"按 UserId 批量黑名单撤销"。
- **微信便捷账号 Phone 可空**（方案偏离登记 2026-09-30）：微信便捷登录无手机号账号 Phone=null（唯一索引对 NULL 放行）；短信路径必填由 SmsAuthenticationProvider 保证；短信绑定补齐后回填。

## 七、与既有扩展划界

| 扩展 | 边界 |
|------|------|
| `TKWF.Ext.Identity`（V0.3.3） | 用户/角色管理 + PasswordHasher——认证中心**不复用**其 UserEntity/RoleEntity（AuthAccount 手机号主键模型根本不同）；两者可共存 |
| `TKWF.Ext.Account`（V0.3.1） | 账户锁定/密码重置——认证中心登录保护 = AuthLoginAttempt 独立实现，语义互补 |
| `TKWF.Ext.SecurityLog` | 安全事件日志——AuthLoginAttempt 是认证中心实例自身库的登录尝试计数，互补不重叠 |
| `TKWF.Ext.RateLimiting` | Web 层限流中间件——认证中心登录保护是 Domain 层窗口计数，双层互补 |

## 八、架构演进路线

- **V0.1.0（已实施）**：令牌体系 / 认证矩阵（短信 + 微信）/ 登录保护 / 票据换令牌（PKCE）/ 身份适配层 / 跨系统映射 / 平台凭证——通用内核 8 组件 + 41 测试全绿。
- **V0.2.0（已实施：查询契约 + UserCenter 承接）**：`IAuthAccountQueryService` 查询契约（只读 4 方法委托 DataService）+ `AuthAccountUserProfileSource` 实现 `IUserProfileSource`（UserCenter 终态落地，装配实例零桥接）；N1-N5 用例全绿。**其余规划项待后续迭代**：管理端 API（账号/凭证管理端点）；按 UserId 批量黑名单撤销；黑名单过期清理任务（对齐 BackgroundJobs 清理范式）；高流量 Redis 分布式黑名单缓存。
- **V0.4.0（V4.10.53 领域自治根治，ADR90——正确路线）**：12 个门面实现继承 `DomainServiceBase`（经基类 `User` 获取用户上下文——**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——v0.3.3 同根缺陷）+ `[DiContractIgnore]` 豁免 DI001；DataService/服务链仍经 `User.Use<T>()` 懒加载（DI004 零豁免）。10 门面注册改 `AddConstructibleService`（接口可构造守卫工厂 + 实现类 throw-factory，消费方统一 `User.Use<接口>()` 解析）；`AuthAccountUserProfileSource` 改**接线型**（ctor `IServiceProvider` + C1 延迟解析 `IAuthAccountQueryService`，修复 UserCenter 门面 GetService 构造失败——真实生产故障；注册保持 TryAddScoped）；两 Provider 保持 TryAddEnumerable（多实现集合）；Initializer 补 `AddOptions<AuthCenterOptions>` + `TryAddSingleton<IMemoryCache>` 兜底（守卫工厂经 ActivatorUtilities 解析剩余参数需可解析）。78 用例全绿（禁止 slnx 构建，仅 Authentication 项目 + 测试项目）。
- **V0.5.0（V4.10.55 多实现集合守卫工厂，ADR92/T3 闭环）**：
  - **两 Provider 注册改 `TryAddEnumerableConstructible`**（集合版守卫工厂）——集合内继承 `DomainServiceBase` 的实现 ctor 注入 `IDomainUser` 由帧内 `CurrentAopUser` 供给（`User.Use` 调用链）；**帧外枚举（如控制器 `[FromServices] IEnumerable<IAuthenticationProvider>` 预绑定）抛守卫**（禁止形态）。修复 EduPlatform 实证 3 认证端点运行时 500（`CallSiteFactory.TryCreateEnumerable`——普通 `GetServices` 裸枚举无法供给 `IDomainUser`，D01 永不注册）。
  - **新增登录编排门面** `ISmsLoginService` / `IWechatLoginService`（`AddConstructibleService` 注册，`[AllowAnonymousFlag]` 匿名面）——**表现层零编排终态**：控制器改 `User.Use<ISmsLoginService>().LoginAsync(phone, code)` / `User.Use<IWechatLoginService>().LoginAsync(code, scope)`，门面 ctor 注入 `IEnumerable<IAuthenticationProvider>`（帧内枚举，守卫工厂供给），选区后委托 `Provider.AuthenticateAsync`，失败语义透传 `ProviderAuthenticateResult`。**⚠️ 破坏性消费约束（评审 P1）**：控制器禁止 `[FromServices] IEnumerable<IAuthenticationProvider>`（帧外抛守卫）——原集合编排消费形态作废，改经登录门面。EduPlatform `sms/login` + `wechat mp/web callback` 3 端点修复路径。
  - 82 用例全绿（新增登录门面 3 用例：建账号成功/错码失败/帧外守卫 + Provider 守卫工厂形态断言）。
- **V0.5.1（过渡形态，未发布——Oracle 评审否决后并入 V0.5.2）**：曾以**系统作用域**修复（`BeginSystemScopeAsync` + `System.Use<ITokenVerifier>()`）——经 Oracle 评审**语义错置**（验签是 Guest 行为非 System 操作；System 作用域本意为系统级后台任务/启动预检，D22 L367"用户请求不可静默提权"）；且若黑名单 DataService 未来加审计，System 路径记 System 而 UserHelper 路径记 Guest，消费链语义分裂。否决并归并至 V0.5.2。
- **V0.5.2（V4.10.57 fcbffd1 受理终态，Oracle 裁决——ITokenVerifier 无帧消费 500 修复）**：
  - **缺陷**：`ITokenVerifier` 守卫工厂（`AddConstructibleService`——帧内 CurrentAopUser 供给）被**两条无 AOP 帧路径**消费——`JwtAuthenticationMiddleware`（HTTP 管线裸 `GetService`）与 `AuthenticationUserHelperBase.RestoreFromTokenAsync`（`DomainUser.GetService` 直通不设帧）→ 守卫工厂帧外抛守卫 → **带 Bearer 受保护端点 500**（真实生产缺陷；测试 Fake 替换掩盖——skill §4.8 心得 8 同型，测试桩掩盖真实生产故障）。
  - **修复（终态）**：① 中间件改经**游客帧**解析——ContextExtraction 阶段 2（UseWebSession 装配时）恒写游客 DomainUser 到 `HttpContext.Items["DomainUser"]`（`WebAppBuilder.InvokeSessionStepAsync`；`BeginSessionScopeAsync(context.RequestServices)` 传外部 SP 不建子作用域，游客 SP 即请求 SP 生命周期安全）→ `guest.Use<ITokenVerifier>()`（AOP 帧内设 CurrentAopUser=guest，守卫工厂供给——验签 = 匿名请求者证明身份，与 LoginAs 同属 Guest 行为）；**⚠️ AuthCenterWebExtension 须与 UseWebSession 同装配**（无游客 → 降级裸 GetService 抛守卫 = 正确 fail 非静默降级；隔离测试宿主 Fake 替换兼容）。② UserHelper 改 `user.Use<ITokenVerifier>()`（AOP 帧内设 CurrentAopUser=user——登录流 user 即游客，与中间件语义一致）。
  - 消费约束：控制器禁 `[FromServices] ITokenVerifier` 预绑定（帧外抛守卫）；验签归中间件/登录链路（表现层零直取）。
  - 82 用例全绿（WebHook FullChain 游客帧分支 + 中间件测试降级分支 + TokenServiceTests 黑名单链）。
- **V0.5.3（框架组转达 2026-10-05——开发模式临时 RSA 密钥跨实例不一致修复）**：
  - **缺陷**：`TokenService` 开发模式（`SigningKeyPath` 未配置 && `IsProduction=false`）每次 `LoadKeysCore` 都 `RSA.Create()` **新建临时密钥**且无进程内共享——每个 scoped 实例（每请求一个）各自 `Lazy<RsaKeySet>` → 签发实例与验签实例（`LocalJwtTokenVerifier` 经 `User.Use<ITokenService>()` 解析的另一实例）密钥集不同 → **INVALID_SIGNATURE**（EduPlatform 反馈，框架组转达 P0）。既有 `Dev_MissingSigningKey_AutoGeneratesTemporary` 只测**单实例**自带签发+验签——单实例密钥集自洽必通过，漏检跨实例不一致。
  - **修复**：新增 `DevRsaKeyCache`（internal static + 双重校验锁 `GetOrCreate(Func<RsaKeySet>)` + `ResetForTests()` 测试隔离钩子——镜像 `PlatformCredentialKeyStore` 既有模式并补其缺 Reset 缺陷）——`LoadKeysCore` dev 分支改从缓存取（工厂仅首次执行）；**生产分支（PEM 文件）天然跨实例一致，绝不走缓存**（缺 PEM 仍 fail-fast）。`RsaKeySet` private → internal（复用同一类型）；`ResetForTests` 先 Dispose 已缓存密钥再置 null（防托管资源泄漏）。
  - 84 用例全绿（新增 `DevRsaKeyCacheTests`：T1 两独立实例 A 签发/B 验签成功——缺陷消除唯一证明 + T2 Reset 后新实例拒旧 token——隔离有效；setup Reset 防跨类泄漏）。
  - **注（V0.7.0）**：`DevRsaKeyCache` 已被框架 `DevKeyCache<T>`（Utility.Caching）取代（E4 密钥管理抽象，见 V0.7.0）——本条为历史记录。
- **V0.5.4（框架组转达 2026-10-05——Development 模式启动崩溃修复）**：
  - **缺陷**：`AuthCenterExtensionInitializer.InitializeAsync` 经 `BeginSystemScopeAsync(sp)` 进入系统作用域——传参形态（DomainHost M1 所有权契约）**原样绑定传入 provider、不建子 scope**，而 `InitializeAsync(IServiceProvider sp)` 收到的是**根容器**（`ServiceProviderBuiltCallbackAsync` 保持根容器）→ `sysScope.System.Use<ITokenService>()` 从根解析 `AddScoped` 守卫工厂 → **Development（ValidateScopes=true）启动必崩**（`Cannot resolve scoped service ... from root provider`——EduPlatform 实证）。冒烟恒 Production（ValidateScopes 关）掩盖；（同为 V0.3.1 方案 A' 复制链：Federation/Tagging/Permissions/Identity 四处同 defect）。
  - **修复**：`host.BeginSystemScopeAsync()` **不传参**——框架内部 `_ServiceProvider.CreateScope()` 建子 scope 并自拥生命周期（DomainHost.cs L405-409；`SystemActorApiTests` "必须使用独立 IServiceScope 而非根容器" 既有实证）。
  - 回归护栏：`JwtAuthenticationWebHookIntegrationTests` 宿主改走 **Development** 环境（ValidateScopes=true）——启动通过 = 修复回归哨兵（旧形态在此环境启动必崩）。
- **V0.6.0（2026-10-06 归层迭代——Authentication → AuthCenter，无外部消费方零折中）**：
  - **全面归层**：目录/包名（`TKWF.Ext.Authentication` → `TKWF.Ext.AuthCenter`）/命名空间（102 .cs 全量）/MinVerTagPrefix（`AuthCenter/v`）/测试项目（`Extension.AuthCenter.Tests`）/xCodeGen 配置/`[TKWFExtension("AuthCenter")]`/docs 目录；**契约包统一归层** `AuthCenter.Abstractions`（`TKWF.Ext.AuthCenter` 命名空间——README"命名空间保持"旧说明废止）；Federation 消费方同步（ProjectReference/using/IVT——同为内部扩展零外部影响）；`JwtAuthenticationOptions` → `AuthCenterMiddlewareOptions`（中间件行为 POCO，不进 Web Options）。
  - **登录门面升级（Oracle P0-1 表现层零编排补全）**：`ISmsLoginService`/`IWechatLoginService` 返回 `LoginResult(Success, UserId, Token, FailReason)`——门面内编排 认证（Provider）→ 取账号（`IAuthAccountQueryService.GetByUIdAsync` 取 TeacherVerified/AuthLevel）→ 签发（`ITokenService.IssueTokenAsync`）——**端点只调一个门面，不串多门面**；新增依赖经基类 `User.Use<T>()` 懒加载（DI004）。
  - **内建标准对内端点**：`AuthCenterWebExtension<TUserInfo>`（原 JwtAuthenticationWebExtension 更名 + ConfigureEndpoints）内建 6 端点——`POST {prefix}/sms/send-code`（scene 白名单）/ `{prefix}/login/sms` / `{prefix}/login/wechat` / `{prefix}/refresh` / `{prefix}/logout`（已认证 Bearer，jti 经中间件 Items 取）/ `{prefix}/ticket/exchange`——minimal API 手写（Oracle P2-5 非 RESTful）+ **匿名端点游客帧**（`guest.Use<门面>()`，须 UseWebSession 同装配）+ `.AllowAnonymous()` 免认证 + `TokenResponse` DTO（access/refresh/token_type/expires_in）+ FailReason→HTTP 映射（400/429 频控码/503 无 ISmsSender）。
  - **配置分层（AGENTS §8）**：领域 `AuthCenterOptions`（`TKWF:AuthCenter` 不动——EnabledAuthTypes fail-closed/LoginProtection/生命周期）+ 表现层 `AuthCenterEndpointOptions`（`TKWF:AuthCenter:Web`——`RoutePrefix` 默认 `/api/auth` + 6 端点开关，POCO 不镜像领域配置）+ 中间件 `AuthCenterMiddlewareOptions`（RejectInvalidToken）。
  - 测试：102 用例全绿（归层回归 + 门面升级回归 + 端点黑盒冒烟模式 A 共享宿主 + 配置绑定/分层断言）；全 slnx 全绿（Federation 23 同步）。
- **V0.7.0（2026-10-06 E4 密钥管理抽象——响应框架组转达，CPM 4.10.61）**：
  - **删静态密钥类**：`PlatformCredentialKeyStore` / `DevRsaKeyCache` 删除（5 份复制链族系收敛为框架共享抽象——ADR-KeyStore-密钥管理抽象上提主框架）。
  - **keyed DI 首次引入**：`PlatformCredentialService` ctor 注入 `[FromKeyedServices(SymmetricKeyProviderKeys.AuthCenter)] ISymmetricKeyProvider`（`FileSymmetricKeyProvider`——构造即加载：生产缺密钥 fail-fast / 开发两分支；`AddKeyedSingleton` 惰性构造，首次解析门面时触发，启动语义与旧 `Initialize` 等价）；`TokenService` ctor 注入 `DevKeyCache<RsaKeySet>` DI 单例（dev 分支临时 RSA 密钥集非静态缓存——替代 V0.5.3 `DevRsaKeyCache` 进程静态；生产 PEM 分支恒不走缓存语义不变）。
  - **加密边界上移服务层（P1-6 方法面显式规约）**：`PlatformCredentialEntityDataService` 回归纯持久化——`GetSecretByAppAsync`（解密）拆为 `GetByAppIdAsync`（entity 返回不解密）/`CreateEncryptedAsync(cred, plainSecret)` → `CreateAsync(cred)`（已加密）/`UpdateEncryptedAsync` → `UpdateAsync(cred)`（已加密）/删 `GetSecretByAppAsync`；加密移 `PlatformCredentialService` 私有包装（`_keys.Encrypt/Decrypt`）；`WeChatApiClient` 等 DataService 直接消费方改经门面（T1 穷举）。收敛对齐 Federation/MFA 既有服务层加密拓扑（DataService 保持 ADR61 纯 DAC 语义）。
  - **格式统一单段**：AES-GCM 委托框架 `AeadEncryptionUtil` byte[] 重载（`base64(nonce[12]‖cipher‖tag[16])` 规范格式）——三段点分废弃（用户裁定不考虑过渡）；存量三段密文一次性迁移或重注册（三扩展新发大概率无生产存量——闭环确认）。
  - **测试**：`DevRsaKeyCacheTests` 改注入式（去静态 Reset）；`PlatformCredentialServiceTests` keyed provider 注入；**AssemblyInfo 撤销 `DisableTestParallelization`**（静态消失 → 并行安全回归哨兵）；102+ 用例全绿。
- **V0.8.0（2026-10-06 认证 API 补全——EduPlatform 转告 2026-10-06，Oracle 评审 PASS WITH CONDITIONS 修订闭环）**：
  - **内建 3 认证端点**（`AuthCenterWebExtension.ConfigureEndpoints` 扩编，minimal API + Options 开关）：`GET {prefix}/verify`（**已认证内省**——判别器 = 中间件验签产物 `Items[TokenValidationResult]`，零二次验签；无效 Bearer → **401 显式拒绝**（非 RFC 7662 `200{active:false}`，README/指南标注）；返回 `VerifyResponse(valid/userId/authType/auth_level/teacher_verified/exp/jti)`）+ `GET {prefix}/grants?app_id`（已认证——`user.Use<IAuthGrantQueryService>()` → `GrantsResponse(GrantView[]——Scopes 拆分归 Web 层)`）+ `POST {prefix}/sms/verify`（匿名游客帧——scene 白名单 Login/Register/Bind/Reset + `ISmsVerificationService.VerifyCodeAsync` 单次消费 → `200{verified:true}`；频控 429）。
  - **B5 票据绑定缺口闭环（转告 §三.3 + Oracle P0-1/P1-2）**：`OAuthTicketIssueRequest` 末位增 `string? UserId = null`（签发即绑——authorize 时用户已登录带 UId；末位默认参零破坏既有调用）；新增 `BindTicketAsync(ticket)`（**已认证帧**——userId 取 `User.UserId` 不经请求体，**无 `[AllowAnonymousFlag]`**；防票据泄漏后绑任意 userId 账号接管）+ DataService `BindUserAsync` **CAS 条件更新**（`WHERE Id=? AND UserId IS NULL` → false 抛 `TICKET_ALREADY_BOUND`）；`ExchangeAsync` 语义不动（保留 `TICKET_NOT_BOUND`——绑定后才可换取）。**测试收敛**：`OAuthTicketServiceTests` 删 5 处手动 `entity.UserId=` 回填改 `IssueAsync(UserId:)` 签发即绑。
  - **`AuthGrantEntity` 应用授权数据底座（Oracle P0-2/P1-1/P1-3/P2-1/P2-4）**：OAuth2 authorization grant 语义（UserId/AppId/Scopes/ValidUntil/Source/Status + **唯一索引 `UX_AuthGrant_User_App_Source(UserId,AppId,Source)`**）；`IAuthGrantQueryService`（只读）+ `IAuthGrantCommandService`（写入——`RecordLoginGrantAsync` upsert 幂等，唯一冲突 catch→重查转 update）双门面 `AddConstructibleService` 注册；**写入点**：`ExchangeAsync` 成功（签发 JWT 对后）经 `User.Use<IAuthGrantCommandService>()` 落登录授权（**best-effort**——写入失败不阻断 JWT 签发，grant 仅服务 `/grants` 查询，可接受降级标注）；`Source="redeem"` 预留 B-口令兑换产品线。**⚠️ `ValidUntil` 语义**：应用授权有效期（null=持续至吊销，跨会话），**非会话有效期**（2h access/30d refresh 与 grant 正交）。
  - **配置分层（AGENTS §8）**：`AuthCenterEndpointOptions`（`TKWF:AuthCenter:Web`）增 `TokenVerifyEndpointEnabled`/`GrantsEndpointEnabled`/`SmsVerifyEndpointEnabled`（默认 true，纯暴露面；领域侧零新增——verify 验签模式已由 `AuthCenterOptions.VerifyMode` 承载、grant 无开关、sms 频控由 `LoginProtection` 承载）；`SmsScenes.Bind` 复用（转告"缺绑定场景"偏差——已天然满足）。
  - 测试：**124 用例全绿**（103→124：+3 端点冒烟×6 + grants 查询 Fake + sms/verify 2 + verify 2 + 配置断言 2 + `AuthGrantCommandServiceTests` 4（创建/幂等/并发唯一/多 app）+ `OAuthTicketServiceTests` BindTicketAsync 全矩阵 6（成功/无帧/消费/过期/已绑/CAS 并发））；9 实体 DataService 红线断言同步。
- **用户中心（档案面）**：**独立立项 `TKWF.Ext.UserCenter`**（用户裁定 2026-09-30）——公共 Profile API/兑换历史/我的应用/页面另行立项。**注：认证中心 v0.2.0 已实现 `IUserProfileSource`（终态落地）**——`AuthAccountUserProfileSource` 经 TryAddScoped 注册（接线型），装配实例零桥接。
- **DMP-Lite 迁移**：本扩展完成后 DMP 改用本扩展（密钥交接不可行 → 存量 access 失效需公告重登；PlatformAdmin 本地映射；GlobalUserMap → PlatformAccountMap 外键拆除；TokenVersion 初始化对齐——见开发方案 §九）。

<!-- EOF -->
