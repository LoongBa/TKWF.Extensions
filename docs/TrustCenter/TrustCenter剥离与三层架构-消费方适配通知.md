# TrustCenter 剥离与三层架构——消费方适配通知

> **发出方**: TKWF 扩展模块组｜**日期**: 2026-10-10｜**状态**: ✅ 本地全量部署已完成（`F:\TKWF_FRAMEWORK_PATH\build\refs\` 含 `TKWF.Ext.TrustCenter.dll` 等最新 DLL）——可通知适配
> **收件方**: DMP-Lite 等引用 `TKWF.Ext.Federation` / `TKWF.Ext.AuthCenter` / `TKWF.Federation.{平台}` 的消费方项目组
> **关联**: 开发方案 `docs/TrustCenter/TrustCenter剥离与三层架构-开发方案.md`（Oracle8 双评审闭环）· 变更记录（2026-10-10）· 转告 `docs/TrustCenter/转告-SymmetricKeyProviderKeys申请并入TrustCenter键.md`
> **裁定**: **迁移不兼容性适配，通知各方适配即可**（用户 2026-10-10）——无过渡层、无兼容 shim

---

## ⚡ 一句话摘要

**TKWF 认证体系三层化重构（2026-10-09/10）：`TKWF.Ext.Federation` 信任内核迁出为 `TKWF.Ext.TrustCenter`（纯信任内核），Federation 保留为对外连接层壳；AuthCenter 删除内建微信 Provider 改为经桥接借道外部 IdP**。全量 42 项目 1913 用例 0 失败，本地已部署。请按下表适配。

---

## 一、变更总览（三层边界）

```
┌────────── 内网（零外部协议依赖） ──────────────┐
│ AuthCenter —— 用户身份认证（token1）           │
│   └─ 借道验证：IExternalIdpAuthenticator（契约）│
│ TrustCenter —— 应用间信任（token2）【新扩展】    │
│   ├─ 应用注册（SsoClientEntity）+ token2 ES256  │
│   ├─ accesscode（数据投递 + ExpectedClaimant）  │
│   └─ ISsoChannel/IToken2Service/IAccessCodeService（Abstractions）│
└──────────────┬──────────────────────────────────┘
Federation —— 对外连接层壳（/feberation/* 端点 + 编排 + 桥接）
```

| 对象 | 变更前 | 变更后 |
|------|--------|--------|
| 信任内核（token2/应用注册/accesscode） | `TKWF.Ext.Federation` 主包 | **`TKWF.Ext.TrustCenter`**（新扩展，命名空间 `TKWF.Ext.TrustCenter`） |
| 信任契约（ISsoChannel/IToken2Service/IAccessCodeService） | Federation 主包 | **`TKWF.Ext.TrustCenter.Abstractions`**（新契约包） |
| Federation | 信任内核 + 连接混合 | **连接层壳**（/feberation/* 端点 + SsoLogin 编排 + ExternalIdpAuthenticator 桥接 + 多通道设施保留） |
| 平台库 | 引 Federation 主包（实现 ISsoChannel） | 引 `TrustCenter.Abstractions`（新增）+ Federation 主包（保留——ChannelConfig/IChannelSource 仍在连接层） |
| AuthCenter 微信 Provider | `WeChatAuthenticationProvider`（内建双源） | **删除**——经 `IExternalIdpLoginService` 借道 Federation 平台库 |
| 微信协议实现 | AuthCenter 内 `WeChatApiClient` + 平台库双份 | **单源**——仅平台库（`TKWF.Federation.WeChat`） |

---

## 二、逐项适配指引

### 1. 引用/命名空间（最常见改动——Federation → TrustCenter）

```xml
<!-- csproj：Federation 主包引用保留（连接层），新增 TrustCenter 主包 + 契约包 -->
<ProjectReference Include="..\..\_Framework\TrustCenter\TKWF.Ext.TrustCenter.csproj" />
<ProjectReference Include="..\..\_Framework\TrustCenter.Abstractions\TKWF.Ext.TrustCenter.Abstractions.csproj" />
<ProjectReference Include="..\..\_Framework\Federation\TKWF.Ext.Federation.csproj" />   <!-- 连接层壳（如需 /feberation 编排） -->
```

```csharp
// using 变更（信任内核类型）
using TKWF.Ext.Federation;    // ❌ 变更前（token2/accesscode 等）
using TKWF.Ext.TrustCenter;   // ✅ 变更后
// IToken2Service / IAccessCodeService / ISsoChannel / ISsoClientService / TrustCenterOptions 现位于 TKWF.Ext.TrustCenter
```

> 仅消费连接层编排（`ISsoLogin`/`ISsoChannelFactory`/`IChannelRegistry`）的消费方：`TKWF.Ext.Federation` 引用保留 + 补 `TrustCenter.Abstractions`（`ISsoChannel` 类型来源）。

### 2. 白名单声明

```csharp
// 信任内核（token2/accesscode/应用注册）
[TKWFEnabledExtension(typeof(TrustCenterExtensionInitializer<>))]
// 连接层（/feberation 编排 + 桥接——可选，需外连才装）
[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
// 认证中心（身份认证——组合式）
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
```

### 3. 配置节（破坏性）

| 配置节 | 变更前 | 变更后 |
|--------|--------|--------|
| 信任内核（token2/accesscode/密钥） | `TKWF:Federation` | **`TKWF:TrustCenter`**（Issuer/SigningKeyPath/CurrentKid/SigningKeys/Token2ExpirationSeconds/AccessCodeExpirationSeconds/**AccessCodeRetentionDays**/SecretEncryptionKeyPath/IsProduction） |
| Federation 连接层通道注册表密钥 | （无独立节——曾归 FederationOptions） | **`TKWF:Federation:ChannelRegistry`**（SecretEncryptionKeyPath/IsProduction） |
| Federation 静态通道 | `TKWF:Federation:Channels` | 不变（`FederationStaticChannelOptions`） |
| Federation Web 端点 | （无——/feberation 归装配层） | **`TKWF:Federation:Web`**（RoutePrefix 默认 `/feberation` + LoginEndpointEnabled/JwksEndpointEnabled/EventEndpointEnabled） |
| AuthCenter 认证方式 | `EnabledAuthTypes: ["sms","wechat","password"]` | **`["sms","password","federated"]`**（`wechat` 值已删——外部 IdP 登录 authType 用 `federated`） |

### 4. 认证端点（AuthCenter——破坏性）

| 端点 | 变更前 | 变更后 |
|------|--------|--------|
| 微信登录 | `POST {prefix}/login/wechat` | **`POST {prefix}/login/external/{channelType}`**（body = 参数字典含 `code`/`channel_id` 等） |
| Options 开关 | `WechatLoginEndpointEnabled` | **`ExternalLoginEndpointEnabled`** |

### 5. token 契约（破坏性——V1.0.0 窗口显式声明）

- `authType` 值：删 `"wechat"` → **`"federated"`**（与 `AuthLevel.Federated=2` 一致）
- 新增 claim：**`channel_type`**（如 `wechat_oauth`/`qq_oauth`——外部 IdP 登录时写入）
- 存量 token 解析：`TokenService` 反序列化兜底默认 `sms`（不识别值按 sms 处理）——**存量 wechat 值 token 将无法按原语义解析**，消费方须在刷新/重登后依赖新契约

### 6. 表名（破坏性——DBA 若有存量）

| 表 | 变更前 | 变更后 |
|----|--------|--------|
| 授权码 | `TKWF_SsoAccessCode` | **`TKWF_AccessCode`**（+ `PayloadEncrypted`/`ExpectedClaimant` 新列，MaxLength 4096/64） |

> SyncStructure 开发环境自动；生产 DBA RENAME + ADD COLUMN（若存量）。`SsoAccessCodeEntity` → `AccessCodeEntity`（类名）。

### 7. 平台库引用（消费方若直接引平台库）

8 平台库（`TKWF.Federation.WeChat` 等）**包名/命名空间不变**（`TKWF.Federation.*`），仅需确保其 csproj 已引 `TrustCenter.Abstractions`（平台库新版——本地部署根已含）。消费方装配：

```csharp
services.AddWeChatFederationChannels();  // 不变——平台库注册方法，ISsoChannel 契约现来自 TrustCenter.Abstractions
```

### 8. 微信凭证存量（AuthCenter）

`PlatformCredentialEntity` 表/门面**保留**（平台库共用底座）；`Platform=wechat` 存量行**无消费者**（WeChatApiClient 已删）——清理 SQL（生产 DBA 执行）：

```sql
DELETE FROM TKWF_PlatformCredential WHERE Platform = 'wechat';
```

### 9. FederationWebExtension（新增装配面——原 /feberation 归装配层）

消费方如需对外连接端点，装配：

```csharp
// Program.cs——UseWebExtensions 增 FederationWebExtension（须与 UseWebSession 同装配——游客帧依赖）
.UseWebExtensions(e => e.Add<AuthCenterWebExtension<MyUserInfo>>(x => { x.RestoreUser = ...; }))
.UseWebExtensions(e => e.Add<FederationWebExtension<MyUserInfo>>())
```

端点：`POST /feberation/{platformId}/login` / `POST /feberation/{platformId}/login/{channelId}` / `GET /feberation/jwks` / `POST /feberation/{platformId}/event/{channelId}`（路由前缀 `TKWF:Federation:Web:RoutePrefix` 可配，默认 `/feberation`）。

---

## 三、破坏性变更汇总

| # | 变更 | 级别 | 适配动作 |
|---|------|------|---------|
| 1 | Federation 重命名 TrustCenter（包/命名空间/版本） | 破坏性 | csproj 引用 + using 更新；信任内核白名单 `TrustCenterExtensionInitializer<>` |
| 2 | 信任契约迁 `TrustCenter.Abstractions` | 破坏性 | 补契约包引用；平台库同步 |
| 3 | token `authType` 删 "wechat" 改 "federated" + 增 `channel_type` | 破坏性（契约） | 消费方 token 解析侧同步 |
| 4 | 端点 `/login/wechat` → `/login/external/{channelType}` | 破坏性（API） | 装配层端点调用更新 |
| 5 | 配置节 `TKWF:Federation` → `TKWF:TrustCenter` + `TKWF:Federation:ChannelRegistry`/`Web` | 破坏性（配置） | appsettings 更新 |
| 6 | 表名 `TKWF_SsoAccessCode` → `TKWF_AccessCode` + 新列 | 破坏性（表） | DBA RENAME/ADD COLUMN（若存量） |
| 7 | 平台库引目标改 `TrustCenter.Abstractions` | 破坏性（依赖） | 平台库升级（本地部署根已含新版） |
| 8 | AuthCenter 删微信 Provider/WechatLoginService/AuthTypes.Wechat | 破坏性（API） | 借道 `IExternalIdpLoginService`；`EnabledAuthTypes` 用 `federated` |
| 9 | nuget `TKWF.Ext.Federation` 旧包 unlist（计划） | 破坏性（包） | 新线 `TKWF.Ext.TrustCenter` 独立发布（待框架组 TrustCenter 键转正后统一） |

---

## 四、新能力（供业务层选用）

- **AccessCode 安全数据投递**（TrustCenter 内置，`IAccessCodeService`）：`IssueAsync(payloadJson, ttl, expectedClaimant)` / `PeekAsync<T>` / `RedeemAsync<T>`——Payload AES-GCM 密文落库（明文上限 ~4068 字节）、ExpectedClaimant 原子核销（无 TOCTOU）、过期清理（`AccessCodeRetentionDays` 默认 7 天）——见 `docs/TrustCenter/信任中心-使用指南.md`
- **FederationWebExtension**：连接层内建 /feberation/* 端点（登录/JWKS/事件接收）
- **JWKS 公共密钥分发**：`GET /feberation/jwks`（Federation 装配时）

---

## 五、本地验证

- 部署根 `F:\TKWF_FRAMEWORK_PATH\build\refs\` 已含全部最新扩展 DLL（`TKWF.Ext.TrustCenter.dll`/`Abstractions.dll`/`Federation.dll`/8 平台库/AuthCenter）——消费方 DLL 模式引用即可联调
- 全量回归：42 项目 1913 用例 0 失败（TrustCenter.Tests 25 / AuthCenter.Tests 149 / Federation.Tests 32 含新端点冒烟）
- NuGet 发布：**已就绪（CPM 4.10.70 + `SymmetricKeyProviderKeys.TrustCenter` 主框架常量切换完成，转告文档已闭环）**——统一打 tag 发布（TrustCenter V0.1.0 新线 + unlist `TKWF.Ext.Federation` 旧包线）待用户同意；消费方本地 DLL 联调不受影响

<!-- EOF -->
