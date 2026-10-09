# TrustCenter 剥离与三层架构 开发方案

> **范围**：TKWF 认证体系三层化重构——`AuthCenter`（用户身份认证）/ `TrustCenter`（应用间信任，从 Federation 剥离）/ `Federation`（对外连接层，装配面保留）。AccessCode 升级为安全数据投递通道。
> **版本**：v0.1.0（草案） ｜ **日期**：2026-10-09
> **状态**：📋 提议（待 Oracle 批判性评审）
> **关键约束**：内网/外网边界隔离（AuthCenter/TrustCenter 零外部协议依赖，Federation 统一对外出口）；数据访问红线（零 IFreeSql/IEntityDAC 直注入）；tkwf-extension skill 铁律（门面经 `User.Use<T>()` / 禁 Store 伪层 / 领域自治）；配置分层（AGENTS §8）
> **前置**：无历史包袱（Federation 消费方极少，重构窗口当前最佳——用户裁定 2026-10-09）

---

## 一、背景与目标

### 问题

TKWF 认证体系当前为两层（AuthCenter + Federation），存在三类缺陷：

| # | 缺陷 | 实证 |
|---|------|------|
| 1 | **AuthCenter 微信登录 100% 不可用** | `WeChatAuthenticationProvider` 调 `GetOpenIdAsync("", code)` 传空 appId → 下游 `GetByAppIdAsync` 精确匹配 `c.AppId==""` 恒失败；`ProviderAuthenticateContext` 扁平 record 无 appId 字段（结构上无法传递）——**P7 静默失败**，零行为测试（Fake 掩盖） |
| 2 | **微信协议双源** | AuthCenter 内 `IWeChatApiClient`/`WeChatApiClient` 与平台库 `Federation.WeChat` 重复实现——P2 唯一事实源违规 |
| 3 | **Federation 职责混杂** | 信任内核（应用注册/token2/accesscode）+ 外部连接（ISsoChannel/编排）同包——内网信任与对外连接边界不清；且单应用需外部验证被迫背负整个联邦体系 |

### 目标

1. **三层边界隔离**：AuthCenter（身份·内网）/ TrustCenter（信任·内网）/ Federation（连接·外网统一出口）——内网两层零外部协议依赖
2. **根因修复微信缺陷**：结构性消除（可扩展键值模型取代扁平 record），非补丁
3. **消除协议双源**：微信协议一处（平台库），AuthCenter 经桥接消费
4. **AccessCode 升级**：安全数据投递通道（Payload + ExpectedClaimant 原子核销），供业务层选用
5. **组合式装配**：4 档装配梯度按需组合（发现≠启用）

---

## 二、范围

### 包含

| # | 模块 | 内容 |
|---|------|------|
| 1 | **TrustCenter 剥离** | Federation 重命名重构为 TrustCenter（信任内核：应用注册 `SsoClientEntity` + token2 ES256 + JWKS + accesscode + ISsoChannel 契约 + 编排面）——**非双扩展并存** |
| 2 | **Federation 重定位** | 外部连接层（连接执行 + 编排 + 桥接实现），**装配面保留**（扩展壳：Initializer + WebExtension 端点 + 多通道配置持久化 `SsoChannelRegistryEntity`） |
| 3 | **AuthCenter 桥接** | `IExternalIdpAuthenticator`（Abstractions）+ `IExternalIdpLoginService` 门面；删 AuthCenter 内微信双源（WeChatAuthenticationProvider/IWeChatApiClient/WechatLoginService/AuthTypes.Wechat） |
| 4 | **AccessCode 增强** | `AccessCodeEntity`（+`PayloadEncrypted` +`ExpectedClaimant`）+ `IssueAsync/PeekAsync/RedeemAsync`——安全数据投递通道（TrustCenter 内置能力，不独立扩展） |
| 5 | **短期凭证算法下沉** | `IShortLivedCredential` **算法工具**（CSPRNG/原子 CAS/SHA256/TTL——Utility 静态方法），**不下沉服务契约** |
| 6 | **测试/文档** | 全量回归 + 新增三层边界用例 + README/使用指南 + 转告 |

### 不包含（去向）

| # | 内容 | 去向 |
|---|------|------|
| 1 | AuthSurface 兑换码增强（Payload + 核销条件） | **落档转告**（`docs/TrustCenter/转告-AccessCode附加信息能力-供业务层选用.md`），随业务需求立项（v0.2.0 候选） |
| 2 | 验证型外链迁移（短信/邮件验证码） | 不迁移——验证型（发送→校验→消费）无数据投递语义，保持各自本地实体（SmsRecord/PasswordResetCode） |
| 3 | m2m 服务认证 / 跨实例联邦 | 未来候选（Federation 应用注册表已备种子） |
| 4 | TrustCenter 管理面（注册信息/配置界面，活动目录式） | 场景 4 增强，独立立项 |

---

## 三、设计依据

| 文档 | 版本/日期 | 路径 |
|------|-----------|------|
| 多轮架构讨论（用户 + Oracle8 评审） | 2026-10-08~09 | 会话记录 |
| Oracle8 评审书（TrustCenter 剥离综合方案） | 2026-10-09 | 见 §八 |
| ADR-AuthCenter-归层与命名（四层模型） | 已批准 | `docs/AuthCenter/ADR/` |
| ADR100 表名前缀（§8.3/§8.4 简称机制） | 已批准 | `docs/目录结构与版本管理规则.md` §八 |
| tkwf-design-principles skill | V1.0 | `../_TKWF/docs/AC-Kit/skills/` |
| AGENTS §8（扩展判据/配置分层） | — | `AGENTS.md` |

---

## 四、任务拆解

| 任务 | 描述 | 关联 | 工作量 |
|------|------|------|--------|
| T1 | **TrustCenter 扩展确立**：Federation 重命名重构（项目/命名空间/MinVerTagPrefix/实体/token2/accesscode/ISsoChannel 契约/编排面）——信任内核 + 契约 | 方案一 | 高 |
| T2 | **TrustCenter.Abstractions**：ISsoChannel + SsoChannelAuthContext/Result + IToken2Service/IAccessCodeService 契约迁入 | 方案二 | 中 |
| T3 | **Federation 重定位**：装配面保留（Initializer + WebExtension 端点 + SsoChannelRegistryEntity 多通道配置持久化）+ 内部逻辑运行库化（连接执行/编排/桥接） | 方案二/四 | 高 |
| T4 | **平台库迁移**：7 平台库引目标改 TrustCenter.Abstractions（实现 ISsoChannel）+ Federation 桥接委托平台库集合 | 方案二 | 中 |
| T5 | **AuthCenter 桥接**：`IExternalIdpAuthenticator`（Abstractions）+ `IExternalIdpLoginService` 门面；删微信双源；端点 `/login/wechat` → `/login/external/{channelType}` | 方案三 | 高 |
| T6 | **AccessCode 增强**：`AccessCodeEntity` + PayloadEncrypted + ExpectedClaimant + Issue/Peek/Redeem + CAS 条件进 WHERE | 方案四 | 中 |
| T7 | **短期凭证算法下沉**：Utility `ShortLivedCredentialUtil`（GenerateCode/HashCode/BuildCasPredicate 静态方法） | 方案四 | 低 |
| T8 | **测试全量**：三层边界用例（内网零外连断言/桥接 fail-hard/单通道选区/核销原子性）+ 既有回归 | — | 高 |
| T9 | **文档收尾**：TrustCenter/Federation/AuthCenter README + 使用指南（4 档装配）+ 转告 | — | 低 |

---

## 五、技术方案

### 5.1 三层架构（边界隔离）

```
┌──────────── 内网（零外部协议依赖） ─────────────────────────┐
│                                                          │
│  AuthCenter（扩展）——用户身份认证（token1）                  │
│    ├─ 内部认证：短信/密码                                   │
│    └─ 借道验证：经 IExternalIdpAuthenticator（Abstractions） │
│                                                          │
│  TrustCenter（扩展）——纯信任内核（token2）                  │
│    ├─ 应用注册（SsoClientEntity）+ token2（ES256）+ JWKS    │
│    ├─ accesscode（数据投递 + ExpectedClaimant 原子核销）     │
│    └─ ISsoChannel 契约 + IToken2Service/IAccessCodeService │
│        （Abstractions——只定义，不编排、不连接）              │
└──────────────┬───────────────────────────────────────────┘
               │ 经 Abstractions 契约（不引主包）
┌──────────────▼───────────────────────────────────────────┐
│  Federation（扩展）——完整对外连接层（连接+编排一体）          │
│    ├─ 装配面：Initializer + WebExtension（/sso/* 端点）     │
│    ├─ 多通道配置持久化（SsoChannelRegistryEntity）          │
│    ├─ 连接执行：平台库集合（实现 ISsoChannel）               │
│    ├─ 编排（SsoLogin：认证→查号→建号→发码→换 token2）        │
│    ├─ ExternalIdpAuthenticator（实现 AuthCenter 桥接）      │
│    └─ 事件验签/加解密工具                                   │
└───────────────────────────────────────────────────────────┘
```

**依赖方向**（无环）：
```
AuthCenter → AuthCenter.Abstractions（借道验证契约）
TrustCenter → TrustCenter.Abstractions（信任契约）
Federation → AuthCenter.Abstractions（实现桥接）+ TrustCenter.Abstractions（ISsoChannel 编排 + 调签发契约）+ 自持 SsoChannelRegistryEntity
平台库 → TrustCenter.Abstractions（实现 ISsoChannel）
```

**边界隔离机制（显式声明——非"装配面"隐式魔法）**：
- **隔离 = 依赖方向无环**（TrustCenter 不引任何外部协议库——grep 断言零 `Federation.WeChat` 等平台库引用）+ **HTTP 端点暴露面归 Federation WebExtension**（新建）
- TrustCenter 部署只引 TrustCenter（不装配 Federation → 无 /sso 外连端点 → 纯内网中枢）；Federation 部署才挂载对外端点
- TrustCenter **可有自己的 WebExtension 暴露 JWKS**（公共密钥分发是信任内核职责，非外连）——不强制零 WebExtension
- 验收（§七.1 修正）：TrustCenter grep 零外部协议库引用 + 不装配 Federation 时无 /sso 端点

### 5.2 TrustCenter 剥离（T1/T2——Federation 重命名重构）

- 项目 `TKWF.Ext.Federation` → `TKWF.Ext.TrustCenter`（MinVerTagPrefix `TrustCenter/v`）
- 信任内核迁入：`SsoClientEntity`（应用注册）/ `Token2Service`（ES256 + JWKS）/ `SsoAccessCodeService` + `AccessCodeEntity`——**纯签发，零具体平台协议依赖**
- `TrustCenter.Abstractions`（新契约包）：`ISsoChannel` + `SsoChannelAuthContext/Result` + `IToken2Service` + `IAccessCodeService`——契约一处，定义"外部如何接入信任网络"（TrustCenter **经契约感知外部存在，不感知具体平台实现**）
- **编排面（`ISsoChannelFactory`/`SsoLogin`）不迁入 TrustCenter**——归 Federation（见 §5.3）
- **多通道设施不迁**——`SsoChannelRegistryEntity` + `IChannelRegistry`/`CompositeChannelRegistry`/`StaticChannelRegistry`/`DbChannelRegistry`/`SsoChannelRegistryService` 全归 Federation（连接层权威数据 + 选区设施）

### 5.3 Federation 重定位（T3/T4——扩展壳 + 内部逻辑运行库化）

- **扩展壳（装配面，新建 FederationWebExtension）**：Initializer（白名单→三钩子）+ **新建 `FederationWebExtension`**（`/sso/login`、回调、JWKS、事件接收端点——⚠️ 当前 Federation **无 WebExtension**，`/sso/*` 端点为新建非保留）+ 多通道配置持久化（`SsoChannelRegistryEntity`）+ 多通道选区设施（`IChannelRegistry` 系列）
- **内部逻辑运行库化**：连接执行（平台库集合）/ **编排（SsoLogin + ISsoChannelFactory）**/ 桥接（ExternalIdpAuthenticator）/ 事件验签——纯逻辑类
- 平台库引目标改 `TrustCenter.Abstractions`（实现 ISsoChannel，L2 门控）
- `ExternalIdpAuthenticator : IExternalIdpAuthenticator`——持 ISsoChannel 集合，按 channelType 委托平台库认证（**委托非双实现**——消除 P2 双源）
- **SsoLogin 编排（归 Federation）**：认证（调 ISsoChannel）→ 查号（调 ISsoChannelMapService，经 AuthCenter.Abstractions）→ 建号 → 发码（调 IAccessCodeService，经 TrustCenter.Abstractions）→ 换 token2（调 IToken2Service，经 TrustCenter.Abstractions）——**Federation 双 Abstractions 依赖（AuthCenter.Abstractions + TrustCenter.Abstractions）为合法 L2 间接层**

### 5.4 AuthCenter 桥接（T5——根因修复）

```csharp
// AuthCenter.Abstractions 新增
public interface IExternalIdpAuthenticator : IDomainService
{
    Task<ExternalIdpAuthResult> AuthenticateAsync(
        string channelType, IReadOnlyDictionary<string, string?> parameters,
        CancellationToken ct = default);
}
public sealed record ExternalIdpAuthResult(bool Success, string? ExternalUserId, string? FailReason, int AuthLevel);

// AuthCenter 新增门面
public interface IExternalIdpLoginService : IDomainService
{
    Task<LoginResult> LoginAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default);
}
```

- 流程：桥接认证 → `ISsoChannelMapService` 映射 → 建号/复用 → 签 token1
- **删除双源**：WeChatAuthenticationProvider / IWeChatApiClient / WechatLoginService / ProviderAuthenticateContext 的 WechatCode/WechatScope / AuthTypes.Wechat
- 端点 `/login/wechat` → `/login/external/{channelType}`
- **token 契约调整**：`authType` 删 "wechat"（改 **"federated"**——与 `AuthLevel.Federated` 一致，非泛化 "external"）+ 新增 `channel_type` 字段（`wechat_mp`/`qq_oauth`）——**破坏性，V1.0.0 窗口显式声明**
- **fail-hard（P7）**：未装配 Federation → `IExternalIdpAuthenticator` 未注册 → `User.Use` 抛守卫（不静默降级）

### 5.5 AccessCode 安全数据投递（T6——TrustCenter 内置能力）

```csharp
// AccessCodeEntity 扩展（表 TKWF_AccessCode——去 Sso 前缀，不撞名不简称）
[Column(Position = 13)] [MaxLength(4096)] public string? PayloadEncrypted { get; set; }  // AES-GCM 附带信息
[Column(Position = 14)] [MaxLength(64)]  public string? ExpectedClaimant { get; set; }   // 谁可核销（null=可转让）

// IAccessCodeService 扩展
Task<string> IssueAsync(string? payloadJson, TimeSpan ttl, string? expectedClaimant, CancellationToken ct);
Task<T?> PeekAsync<T>(string code, CancellationToken ct);                                  // 预读（业务层复杂规则）
Task<T?> RedeemAsync<T>(string code, string? claimant, CancellationToken ct);              // 核销取 payload + 销毁
```

- **条件进原子 CAS**（无 TOCTOU）：
  ```csharp
  EntityUpdateWhereAsync(
    c => c.CodeHash == hash && c.Used == false && (c.ExpectedClaimant == null || c.ExpectedClaimant == claimant),
    c => new { Used = true }, ct);
  // 影响 0 = 不存在/已用/核销人不符 → 重查判因
  ```
- **用途由业务层决定**（人工核验/自动核验）——TrustCenter 只提供能力，不规定用途（见转告文档）
- **不独立扩展**（P9）：TrustCenter 内置能力；未来非信任域场景再评估剥离
- **Payload 密钥归属**：`PayloadEncrypted` 经 keyed `ISymmetricKeyProvider`（新增 **`SymmetricKeyProviderKeys.TrustCenter`**，`TrustCenterOptions.SecretEncryptionKeyPath` 前 32 字节密钥）——不复用 Federation key（信任域密钥独立，安全域隔离）
- **Payload 容量校验（P7 fail-hard）**：`[MaxLength(4096)]` 为密文列——AES-GCM 密文 = 明文 + 12(nonce) + 16(tag)，**明文最大 ~4068 字节**；`IssueAsync` 入口校验明文长度，超限抛 `PAYLOAD_TOO_LARGE`
- **清理任务**：过期 accesscode 行（含 Payload 密文）清理——对齐 BackgroundJobs 清理范式，`RetentionDays` 配置化（默认如 7 天），防业务数据残留
- **PeekAsync 并发安全语义（显式声明）**：Peek 为**只读快照非锁定**——Peek 后行可能被并发 Redeem；业务层须自行处理 `RedeemAsync` 失败（`ACCESS_CODE_CONSUMED`/`ACCESS_CODE_NOT_FOUND` 重查判因），不得基于 Peek 结果做不可逆决策

### 5.6 短期凭证算法下沉（T7——算法工具非服务契约）

- Utility 静态方法：`ShortLivedCredentialUtil.GenerateCode()`（CSPRNG 32B base64url）/ `HashCode(code)`（SHA256 hex）/ `BuildCasPredicate(...)`
- **不下沉服务契约**（`IShortLivedCredential` 接口）——4 处短命凭证（accesscode/SmsRecord/PasswordResetCode/RedemptionCode）签名碰巧相同但语义不同，提取接口是过度抽象（P9）
- 各扩展 Issue/Peek/Redeem 语义不同，保持各自门面；只复用算法工具

### 5.7 单通道 = 多通道选区特例（无需新机制）

- `IChannelRegistry`（组合：DB 命中优先 → 静态回退）：`GetAsync(channelId)` 指定 / `GetDefaultAsync()` 默认
- 只启单通道 = 默认通道；单通道 + 测试通道 = 显式指定（`IsDefault` 标记）——**集合大小为 1 或 2 的特例，配置数量差异，无架构分叉**

### 5.8 命名（裁定）

| 项 | 裁定 |
|----|------|
| `SsoAccessCodeEntity` → `AccessCodeEntity` | 去 Sso（语义泛化为数据投递，非 SSO 专属） |
| 表名 `TKWF_AccessCode` | 不撞名不简称——TrustCenter 扩展内部实体，仅加默认前缀（§8.3） |
| `Entity` 后缀 | 保留（SG1 生成约定） |
| `SsoClientEntity` / `ISsoChannel` | 保留（SSO/信任接入语义仍准确）；若 TrustCenter 定位"通用信任中枢"可后续改名（不阻断） |

---

## 六、破坏性变更与迁移

| 变更 | 级别 | 迁移 |
|------|------|------|
| Federation 重命名 TrustCenter | 破坏性（包/命名空间/版本） | 消费方引用/using 更新；Federation 测试迁移 |
| AuthCenter 删微信 Provider/WechatLoginService | 破坏性（API） | 端点 `/login/wechat` → `/login/external/{channelType}` |
| token `authType` 删 "wechat"（改 "federated"）+ 增 `channel_type` | 破坏性（契约） | 消费方解析侧同步 |
| 平台库引目标改 TrustCenter.Abstractions | 破坏性（依赖） | 7 库 csproj 更新 + 回归 |
| `TKWF_SsoAccessCode` → `TKWF_AccessCode` | 破坏性（表名） | DBA RENAME（若存量） |
| AccessCode 增 Payload/ExpectedClaimant 列 | 破坏性（列新增） | SyncStructure 自动（生产 DBA ADD COLUMN） |

### 6.1 迁移影响清单（Oracle 批判性评审条件 2 补全）

| # | 影响项 | 决策/处置 | 状态 |
|---|--------|----------|------|
| 1 | **7 平台库包名**（`TKWF.Federation.WeChat` → `TKWF.TrustCenter.WeChat`？） | **保留包名** `TKWF.Federation.{平台}`（平台库 = 大使馆，命名空间 `TKWF.Federation.*` 独立于 TrustCenter 扩展名；仅 csproj 引目标改 `TrustCenter.Abstractions`）——避免 7 库 tag/nuget 全改（评审建议） | 📋 待确认 |
| 2 | **Federation 历史 tag 孤儿**（`Federation/v0.1.0`~`v0.3.0`） | 孤儿 tag 保留不删（历史记录）；TrustCenter 从 `TrustCenter/v0.1.0` 重新开始（MinVer） | 📋 定案 |
| 3 | **nuget 已发布包**（`TKWF.Ext.Federation 0.2.1-preview` 等） | 保留不 unlist（已发布即历史）；新版本线 `TKWF.Ext.TrustCenter` 独立发布 | 📋 待确认 |
| 4 | **AuthCenter.Abstractions SSO 契约归属**（`ISsoAccountQueryService`/`ISsoChannelMapService`） | **保留在 AuthCenter.Abstractions**（用户映射契约属认证内核）；TrustCenter.Abstractions 只承载信任契约（ISsoChannel/IToken2Service/IAccessCodeService）——Federation 双 Abstractions 依赖为合法 L2 | 📋 定案 |
| 5 | **DMP-Lite 现装配** | DMP-Lite 当前装配 AuthCenter（`WechatLoginEndpointEnabled:false`，未用微信 Provider）——**零微信引用已核实**；Federation 重命名后 DMP 引用/using/IVT 清单更新 | 📋 待迁移 |
| 6 | **PlatformCredentialEntity 微信凭证存量** | AuthCenter `PlatformCredentialEntity` 表微信凭证（Platform=wechat）在删 `IWeChatApiClient` 后**无消费者**——凭证迁移至 Federation.WeChat `WeChatOptions`（凭证自持 P2-4）；存量表数据留待 DBA 清理或保留（非破坏） | 📋 待确认 |
| 7 | **7 平台库测试宿主白名单** | 当前模拟"已启用 Federation"——重命名后改"已启用 TrustCenter"（信任契约）+ "已启用 Federation"（新连接层）双白名单 | 📋 待迁移 |
| 8 | **Federation.Tests 拆分** | 现有 34 用例拆为 TrustCenter.Tests（信任内核）+ Federation.Tests（连接层） | 📋 待迁移 |
| 9 | **SsoChannelRegistry 多通道设施归属** | `SsoChannelRegistryEntity` + `IChannelRegistry`/`CompositeChannelRegistry`/`StaticChannelRegistry`/`DbChannelRegistry`/`SsoChannelRegistryService` **全归 Federation**（连接层选区设施） | ✅ 定案（评审条件 5） |

---

## 七、验收标准

| # | 验收项 |
|---|--------|
| 1 | 三层边界：TrustCenter grep 零外部协议库引用（不引任何平台库）+ 不装配 Federation 时无 /sso 端点；Federation 部署挂载 /sso/* 端点（新建 FederationWebExtension） |
| 2 | AuthCenter 借道验证：装配 Federation → 微信登录可用；未装配 → `User.Use` 抛守卫（fail-hard 断言） |
| 3 | 微信协议单源：AuthCenter 内零 WeChatApiClient（grep 零命中） |
| 4 | AccessCode：Issue(payload)→Redeem 取回 payload + 销毁；重放拒；ExpectedClaimant 不匹配拒（原子断言） |
| 5 | 单通道选区：默认通道 + 指定通道（含测试通道）双路可用 |
| 6 | 全量回归：slnx 构建 0 错误 + 全部测试项目全绿 |

---

## 八、Oracle 评审记录

### 初评（bg_40a6a64d，2026-10-09）——PASS WITH CONDITIONS

> 5 方案合并方向正确（附条件通过）——**修正 3 点后为最优解**：
> 1. 消除方案一/二表述矛盾（推荐重命名 Federation→TrustCenter 而非双扩展并存）
> 2. IExternalIdpAuthenticator 委托 ISsoChannel 非双实现
> 3. IShortLivedCredential 下沉算法工具非服务契约
>
> 后续讨论修正（用户裁定）：① 编排（SsoLogin）归 Federation（方案 B——TrustCenter 纯签发）；② Federation 装配面保留（扩展壳 + 内部逻辑运行库化）；③ 多通道配置持久化归 Federation；④ 单通道 = 多通道选区特例。

### 批判性复评（bg_7a5a6737，2026-10-09）——需修改（附条件通过）

> **评审结论**：方案方向正确（三层边界隔离 + 根因修复 + AccessCode 能力升级），是"最小成本达成边界隔离 + 根因修复 + 能力升级"的附条件最优解。替代方案（渐进迁移）引入中间态复杂度（P9 违规），不优于一次性重命名。
>
> **5 项通过条件（本方案已全部吸收）**：
> 1. ✅ 消除 §5.2 与 §八 表述矛盾——编排（SsoLogin/ISsoChannelFactory）归属明确为 Federation，TrustCenter 纯签发（§5.2/§5.3 修正）
> 2. ✅ 补全迁移影响清单——7 平台库包名/tag/nuget/契约归属/DMP-Lite/PlatformCredential/测试宿主（§6.1 新增）
> 3. ✅ 补全 AccessCode 4 处遗漏——密钥归属/容量校验/清理任务/PeekAsync 并发语义（§5.5 修正）
> 4. ✅ 消除"装配面"隐式魔法——边界隔离显式声明为"依赖方向无环 + 端点暴露面归 Federation WebExtension（新建）"（§5.1 修正）
> 5. ✅ 多通道设施归属明确——IChannelRegistry/CompositeChannelRegistry/StaticChannelRegistry/DbChannelRegistry/SsoChannelRegistryService 全归 Federation（§5.2/§5.3/§6.1 修正）
>
> **非阻断建议（已吸收）**：authType 值用 "federated" 而非 "external"（§5.4 修正）。
>
> **复评闭环**：5 项条件全部满足 + 非阻断项吸收——**可进入实施**（Phase 0 定稿 → Phase 1-5）。

**本方案已吸收上述全部修订。待批判性复评。**

<!-- EOF placeholder -->
