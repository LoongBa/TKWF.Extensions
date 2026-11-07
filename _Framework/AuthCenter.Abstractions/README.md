# TKWF.Ext.AuthCenter.Abstractions 认证中心契约抽象

**状态**: 契约抽象（ADR48 L2 依赖倒置 / ADR-AuthCenter-归层与命名） | **框架**: .NET 10 | **依赖**: `TKWF.Domain`（`IDomainService`——`User.Use<T>()` 帧内解析准入）+ `TKWF.Federation.Abstractions`（`FederatedTicketPayload`——D03 1.4 供给层契约）

## 定位

认证中心（AuthCenter）主包的**跨扩展消费契约**（L2 门控：扩展间只引 Abstractions，不引实现主包——`TKWF0022`）。承载外部 IdP 桥接、身份供给与 SSO 账号查询面——`Authentication` → `AuthCenter` 归层（2026-10-06）后契约统一收口本包（原 `Authentication.Abstractions` 演进），Federation 连接层、平台网关与消费方经契约消费，零实体零 SG1 零 Initializer。

**namespace 保持 `TKWF.Ext.AuthCenter`**（跨程序集同 namespace——消费方零 using 迁移，仅 csproj 改引）。

## 内容

| 契约 | 说明 |
|------|------|
| `ISsoAccountQueryService : IDomainService` | SSO 账号查询——`GetByUIdAsync(uid)`（账户自适配层回读，Federation/消费方取账号谱系用） |
| `IExternalIdentityService : IDomainService` | 外部身份映射——`LinkAsync` / `UnlinkAsync` / `GetByExternalIdAsync` / `GetByAccountAsync`（联邦 Id 归一化映射面，`PlatformAccountMap` 通道语义） |
| `IExternalIdpAuthenticator : IDomainService` | 外部 IdP 认证桥接——`AuthenticateAsync(channelType, parameters)` → `ExternalIdpAuthResult(Success, ExternalUserId, FailReason, AuthLevel)`（借道 Federation 平台网关库单源实现） |
| `IExternalIdentityProvisioningService : IDomainService` | 身份供给（D03 1.4）——`ProvisionAsync(FederatedTicketPayload)` → `ProvisionedIdentity(PlatformUid, IsNew)` + `MergeProfileAsync`（事件路径 L2 预置吃免登票据载荷） |
| `IWeakIdentityCleanupService : IDomainService` | 弱身份清理——`QueryWeakIdentitiesAsync` / `PurgeAsync`（SoftDelete/PhysicalDelete）/ `GetWeakStatsAsync`（弱身份治理面，D03 1.4） |
| DTO / record | `ExternalIdentityDto` / `ExternalProfile` / `SsoAccountDto`（不可变 DTO，wire 契约）+ 各门面方法 record 结果 |

## 版本与发布

- MinVerTagPrefix：`AuthCenter.Abstractions/v`（独立 tag 独立演进）
- **V0.2.0（2026-10-30，D03 阶段 3/4 发布链路）**：新增 `IExternalIdentityProvisioningService`/`IWeakIdentityCleanupService` 双契约 + 新增 `→ Federation.Abstractions` 引用（`ProvisionAsync` 入参 `FederatedTicketPayload`）；依赖图单向（FA 不反向引用，无循环）——minor bump；发布顺序 `Federation.Abstractions/v0.6.0` → `AuthCenter.Abstractions/v0.2.0` → `Federation.WeChat/v0.17.0` → `Federation/v0.17.0` → `AuthCenter/v0.13.0` → `WeChatOperations/v0.7.0`（已执行，CI 6/6 全绿）
- 历史：SSO 立项期（2026-10-05）以 `Authentication.Abstractions` 名拆包（契约 `ISsoAccountQueryService`/`ISsoChannelMapService` + DTO），归层后随 `AuthCenter` 改名；V0.1.x 为 preview 线

<!-- EOF -->