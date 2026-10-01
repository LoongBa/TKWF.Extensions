# TKWF.Ext.Authentication 认证中心扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (查询契约 + UserCenter 承接) | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain + FreeSql + Microsoft.Extensions.Caching.Memory + FrameworkReference Microsoft.AspNetCore.App（路径 B 中间件）

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
| 平台凭证 | `PlatformCredentialEntity`（AppSecret **AES-GCM 加密**在 DataService 边界）+ `WeChatApiClient`（access_token 缓存 + 并发锁） |
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
<ProjectReference Include="..\..\_Framework\Authentication\TKWF.Ext.Authentication.csproj" />
```

```csharp
using TKWF.Ext.Authentication;

[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // 自动注册：ITokenService / IAuthLoginAttemptService / IOAuthTicketService / ISmsVerificationService /
    //           IPlatformCredentialService / IPlatformAccountMapService / IWeChatApiClient / ITokenVerifier /
    //           IAuthenticationProvider（短信+微信） + 8 实体 DataService（ADR61 消费方聚合自动注册——Initializer 零手动注册）
}
```

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
> Web 装配收敛为 `JwtAuthenticationWebExtension<TUserInfo>`（Web 装配钩子 ADR87/D22/G18，锚点默认
> `BeforeAuthentication`——ContextExtraction 之后 / HttpAuthentication 之前，验签恢复 DomainUser 供框架认证判定）。

```csharp
// Program.cs——Web 装配钩子一次声明（UseWebExtensions；RestoreUser 委托直传，不经 Options）
builder.ConfigWebAppDomain<MyUserInfo, MyDomainInitializer, DomainWebOptions>(...)
    .UseWebSession()
    .UseWebExtensions(e => e.Add<JwtAuthenticationWebExtension<MyUserInfo>>(x =>
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
| **`ITokenVerifier`** | 令牌验证（本地公钥验签；VerifyMode.RemoteIntrospection 装配层替换） | `LocalJwtTokenVerifier`（本扩展） |
| **`IAuthenticationProvider`** | 认证矩阵 Provider（EnabledAuthTypes fail-closed） | `SmsAuthenticationProvider` + `WeChatAuthenticationProvider`（本扩展） |
| **`ISmsVerificationService`** | 短信验证码发送/校验（频控 + 单次消费 + SHA256 落库） | `SmsVerificationService`（本扩展） |
| **`ISmsSender`** | 短信发送渠道抽象（**消费方实现**——腾讯云等） | 无默认（TryAdd 语义） |
| **`IAuthorizationMapper<TUserInfo>`** | 业务角色本地映射（sub+claims → 角色；**消费方实现**） | 无默认（TryAdd 语义） |
| **`IOAuthTicketService`** | 一次性票据签发/消费（PKCE + 白名单 + 防重放） | `OAuthTicketService`（本扩展） |
| **`IAuthLoginAttemptService`** | 登录尝试记录 + 限流窗口 | `AuthLoginAttemptService`（本扩展） |
| **`IPlatformAccountMapService`** | 跨系统映射（Link upsert / 双向 / UnionId） | `PlatformAccountMapService`（本扩展） |
| **`IPlatformCredentialService`** | 平台凭证管理（AES-GCM 在 DataService 边界） | `PlatformCredentialService`（本扩展） |
| **`IWeChatApiClient`** | 微信 API（access_token 缓存 + 并发锁 + 凭证解析） | `WeChatApiClient`（本扩展） |
| **8 实体 + DataService** | AuthAccount/AuthLoginAttempt/SmsRecord/AuthRefreshToken/AuthTokenBlacklist/OAuthTicket/PlatformAccountMap/PlatformCredential | SG1 + xCodeGen（.g.cs 入库） |
| **`IAuthAccountQueryService`（V0.2.0）** | 对外只读查询契约（ByUId/ByPhone/ByWechatMpOpenId/ByWechatWebOpenId——返回完整 `AuthAccountEntity`） | `AuthAccountQueryService`（internal sealed，本扩展，委托 `AuthAccountEntityDataService`） |
| **`IAuthAccountService`（V0.2.0）** | 对外写契约（Create/Update/IncrementTokenVersion/GetByUId——DMP 渐进替换影子账号 upsert，ADR-Authentication-账号写契约） | `AuthAccountService`（internal sealed，本扩展，委托 `AuthAccountEntityDataService`） |
| **`IUserProfileSource`（V0.2.0）** | UserCenter 档案读取契约（AuthAccount 属主终态承接——`GetProfileAsync` 返回 `UserProfileDto`） | `AuthAccountUserProfileSource`（public sealed，本扩展） |

## 四之二、V0.2.0 查询契约与 UserCenter 承接

**`IAuthAccountQueryService`（查询契约）**——对外只读查询 4 方法（`GetByUIdAsync`/`GetByPhoneAsync`/`GetByWechatMpOpenIdAsync`/`GetByWechatWebOpenIdAsync`），返回完整 `AuthAccountEntity`；实现 `AuthAccountQueryService`（internal sealed）委托 `AuthAccountEntityDataService`——**红线合规**（零 IFreeSql/IEntityDAC 直注入）。

**`IAuthAccountService`（写契约）**——对外写契约（`CreateAsync`/`UpdateAsync`/`IncrementTokenVersionAsync`/`GetByUIdAsync`——upsert 流单注入便利），实现 `AuthAccountService`（internal sealed）委托 `AuthAccountEntityDataService`；**DMP 渐进替换路径**（ADR-Authentication-账号写契约）：平台管理员影子 AuthAccount（`UId=PlatformAdmin.UId`，`IsEnabled=true`，Phone 可空）由消费端创建/更新/失效——扩展 `TokenService.RefreshTokenAsync`（L196-200）强依赖 AuthAccount 记录。**`AdminDeleteAsync` 不暴露**（破坏性，管理 API 迭代）。

**`AuthAccountUserProfileSource`（UserCenter 终态承接）**——public sealed，实现 `IUserProfileSource`：
- **映射矩阵**：UserId←UId / Phone←Phone（**原始值**——UserCenter 门面强制脱敏，实现方不得自行 Mask）/ Nickname / AvatarUrl←Avatar / IsTeacherVerified←TeacherVerified / AuthLevel←AuthLevel
- **微信绑定推导**：`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` **任一非空 = 已绑定**（AuthAccount 无显式 IsWechatBound 字段）
- **注册**：`AuthCenterExtensionInitializer.ConfigureServices` **TryAddScoped 双注册**（`IAuthAccountQueryService` + `IUserProfileSource`）——消费方白名单声明认证中心 + UserCenter 后 `IUserCenterQueryService.GetProfileAsync` 自动获得真实档案；新装配实例**无需写桥接类**
- **依赖**：引 `TKWF.Ext.UserCenter.Abstractions`（**契约包非主包**——L2 门控合规）
- **边界**：两契约**不可合并**——查询服务返回完整 `AuthAccountEntity`（含 TokenVersion/IsEnabled，供 TokenService/装配桥接），档案源返回 `UserProfileDto`（公共档案子集，无敏感字段，Phone 门面脱敏）

## 五、实体表结构（8 张）

| 表 | 关键列/约束 |
|----|-----------|
| `AuthAccount` | UId(32 唯一)/Phone(20 **可空**唯一——微信便捷账号无手机号)/PasswordHash?/WechatMpOpenId(唯一)/WechatWebOpenId(唯一)/UnionId?/TeacherVerified/AuthLevel/TokenVersion/IsEnabled |
| `AuthLoginAttempt` | UserIdentity(100)+AuthType(20)+IsSuccess+IpAddress?+FailReason?+AttemptTime；索引 (UserIdentity,AuthType,AttemptTime) |
| `SmsRecord` | Phone+Scene+CodeHash(SHA256 不存明文)+IsVerified+ExpireAt；索引 (Phone,Scene,CreateTime)/(IpAddress,CreateTime) |
| `AuthRefreshToken` | Jti/UserId+TokenHash(SHA256 唯一)/TokenVersion/ExpiresAt/IsRevoked/RevokedAt?；索引 (UserId,TokenVersion) |
| `AuthTokenBlacklist` | Jti(唯一)/UserId/ExpiresAt/RevokedAt/Reason——条目 TTL=token 自然过期 |
| `OAuthTicket` | Ticket(唯一高熵)/TicketType(login/bind)/AppId/RedirectUri/State?/CodeVerifierHash?/UserId?/ExpiresAt/IsConsumed |
| `PlatformAccountMap` | PlatformAccountId+BusinessAppId+BusinessLocalId(唯一)/UnionId? |
| `PlatformCredential` | Platform+AppType+AppId(唯一)/AppSecretEncrypted(AES-GCM 密文，DtoFieldIgnore 不外泄)/IsEnabled |

## 六、安全边界

- **数据访问红线合规**：全部实体 SG1 + `*EntityDataService` 委托（非泛型 `DomainDataServiceBase`——ADR61）；扩展零 IFreeSql/IEntityDAC 直注入（Service 层只委托 DataService）。
- **密钥安全**：PEM 私钥（chmod 600）/ AES-GCM 密钥文件（前 32 字节）不进代码库；生产缺密钥 fail-fast 拒绝启动；kid 轮换支持紧急换钥。
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
- **用户中心（档案面）**：**独立立项 `TKWF.Ext.UserCenter`**（用户裁定 2026-09-30）——公共 Profile API/兑换历史/我的应用/页面另行立项。**注：认证中心 v0.2.0 已实现 `IUserProfileSource`（终态落地）**——`AuthAccountUserProfileSource` 经 TryAddScoped 注册，装配实例零桥接。
- **DMP-Lite 迁移**：本扩展完成后 DMP 改用本扩展（密钥交接不可行 → 存量 access 失效需公告重登；PlatformAdmin 本地映射；GlobalUserMap → PlatformAccountMap 外键拆除；TokenVersion 初始化对齐——见开发方案 §九）。

<!-- EOF -->
