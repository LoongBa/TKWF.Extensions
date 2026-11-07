# TKWF.Ext.TrustCenter.Abstractions 信任中心契约抽象

**状态**: 契约抽象（ADR48 L2 依赖倒置 / TrustCenter 剥离三层架构） | **框架**: .NET 10 | **依赖**: `TKWF.Domain`（`IDomainService`——`User.Use<T>()` 帧内解析准入）

## 定位

信任中心（TrustCenter）主包的**跨扩展消费契约**（L2 门控：扩展间只引 Abstractions，不引实现主包——`TKWF0022`）。承载应用间信任中枢的签发/校验/查询面——Federation 连接层与平台库经契约调签发 token2 / 授权码（合法 L2），消费方查询应用目录。零实体零 SG1 零 Initializer。

**namespace 保持 `TKWF.Ext.TrustCenter`**（跨程序集同 namespace——消费方零 using 迁移，仅 csproj 改引）。

## 内容

| 契约 | 说明 |
|------|------|
| `IToken2Service : IDomainService` | token2 签发校验——`IssueToken2Async(request)` → `Token2IssueResult(Token, ExpiresIn)` / `ValidateToken2Async(token)` → `Token2ValidationResult`（含 UId/AppId/Jti/ExpiresAtUtc/Claims）/ `GetJwksJson()`（ES256 独立密钥域 + kid 轮换 + JWKS） |
| `IAuthorizationCodeService : IDomainService` | OAuth 授权码面（面②）——`IssueAsync(request)`（ChannelId/UId/TargetAppId/Scope + PKCE CodeVerifier + IP 审计）→ `AuthorizationCodeIssueResult(Code, ExpiresIn)` / `ConsumeAsync(code, codeVerifier, ipAddress)` → `AuthorizationCodeConsumeResult` / `CleanupExpiredAsync`（CAS 原子消费防并发双兑） |
| `ISsoClientQueryService : IDomainService` | 应用目录查询——`GetInfoAsync(appId)` → `SsoClientInfo(AppId, OriginWhitelist, Scopes, IsEnabled)` / `ValidateClientCredentialAsync`（动态注册凭证校验）/ `IsOriginAllowedAsync`（origin 白名单判定） |
| DTO / record | `Token2IssueRequest`/`Token2ValidationResult`/`Token2IssueResult`、`AuthorizationCodeIssueRequest`/`IssueResult`/`ConsumeResult`、`SsoClientInfo`（不可变 wire 契约） |

> **契约归属注记（I2-1，2026-10-20）**：`ISsoChannel`（IdP 适配器契约）已在 Iteration 2 自本包迁入 `Federation.Abstractions`（namespace 改 `TKWF.Ext.Federation`）——通道契约归属纠正，本包依赖图单向（TrustCenter 内核 ⟂ Federation 连接层）；密封票据面① `ISealedTicketService<T>` 在主框架（`TKWF.Domain`，ADR107/ADR110），不在本包。

## 版本与发布

- MinVerTagPrefix：`TrustCenter.Abstractions/v`（独立 tag 独立演进）
- **V0.4.0（2026-10-21，I2 发布链第①步——删 `ISsoChannel.cs` 破坏版本）**：通道契约迁出后发布；后续 TrustCenter 0.3.3 / Federation 0.11.0 / 平台库 0.4.1~0.3.1 / WeChatOperations 0.5.0 逐 tag 联动（已执行）
- V0.3.1：删旧过渡（`IAccessCodeService`/`AccessCodeEntity` 过渡期产物清理——用户裁定项目组已适配零兼容层；旧 `ISsoChannel` 面仍含 6 records）
- V0.1.x~V0.3.0：TrustCenter 剥离期（2026-10-09~16）契约面演进（AccessCode 拆分命名 ADR109 D4——面①SealedTicket 面②AuthorizationCode）
- **验证**：TrustCenter 35 / Federation 119 / WeChatOperations 143 受影响面全绿 + slnx 构建 0 错误

<!-- EOF -->