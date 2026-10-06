# ADR-AuthCenter-密钥管理抽象上提主框架

## 状态

活跃

> 本 ADR 为永久架构决策记录，不可删除。如后续决策被推翻，须在本 ADR 标注「已废弃」并引用新 ADR，而非删除本文件。

## 一、目的与目标

确立「**密钥管理抽象上提主框架 + DI 化**」决策——扩展侧 5 份静态密钥持有者复制链（族系 A dev 密钥缓存 ×2 + 族系 B AES-GCM 密钥持有者 ×3）收敛为框架共享抽象 `ISymmetricKeyProvider`/`FileSymmetricKeyProvider`（`TKW.Framework.Domain.KeyManagement`）+ `DevKeyCache<TKey>`（`TKW.Framework.Utility.Caching`），经 **keyed services**（`AddKeyedSingleton` + `[FromKeyedServices]`）DI 注册消费。读者应在 3 句话内明白：目标状态 = 三扩展（AuthCenter V0.7.0 / Federation V0.2.0 / MFA V0.2.0）零静态密钥类、密钥生命周期统一（生产 fail-fast / 开发两分支 + Reset 契约 internal）、AES-GCM 格式统一单段规范（`base64(nonce[12]‖cipher‖tag[16])`，委托 `AeadEncryptionUtil`）；本 ADR 裁定落点 = **上提主框架**（推翻转达 §四.3「不上主框架 TKWF.Utility」——上提触发独立评估，本次即该评估，用户为最终裁定方，2026-10-06）；同时确立 keyed DI 首次引入主框架的先例与 `SymmetricKeyProviderKeys` 扩展名常量耦合取舍。

## 二、问题

### 问题现象

1. **5 份复制链**：族系 A `DevRsaKeyCache`（AuthCenter 57 行）+ `DevEcKeyCache`（Federation 49 行）逐行一致（仅类型参数 `RsaKeySet`/`EcKeySet` 不同）；族系 B `PlatformCredentialKeyStore`（104）+ `FederationSecretKeyStore`（107）+ `MfaSecretKeyStore`（98）`Initialize` 主体逐行同构（仅错误消息前缀 `AuthCenterOptions.`/`FederationOptions.`/`MfaOptions.` 不同），`Encrypt`/`Decrypt`（`base64(iv).base64(tag).base64(cipher)` 三段点分）三份完全一致——**且三份均内联 `new AesGcm(key, 16)` 重实现，未复用框架 `AeadEncryptionUtil` 既有资产**。
2. **生命周期纪律分裂**（复制链扩散的真实缺陷）：`ResetForTests` 覆盖不均——AuthCenter 2/5 有且被测试调用；Federation 2/5 **定义但零调用（死钩子）**；MFA 1/5 **完全缺失**（`MfaSecretKeyStore` 无 Reset 方法，测试被迫反射 `GetField("_key")` 复位）。
3. **静态单例实测代价**：AuthCenter 测试项目因 `DevRsaKeyCache` 进程静态缓存冲突**禁用测试并行**（`AssemblyInfo.cs` `DisableTestParallelization`）。
4. **格式分裂**：扩展三段点分 vs 框架规范单段（`AeadEncryptionUtil` + `AesGcmCredentialProtector` 两处一致）——扩展是离群者，与框架资产不可互通。

### 触发场景

- 认证体系与相关扩展已有 **3 个密钥管理消费方 / 5 份静态密钥持有者**（逐行复制粘贴、零共享抽象）——E4「密钥管理抽象」触发条件① 满足且超出（第 2 个消费方 → 已第 3 个）。

### 现有方案不足

- **转达 §四.3 裁定「不上主框架 TKWF.Utility」**：其论据「密钥管理等容量对象的生命周期/生产路径应留在扩展域」与既有代码事实矛盾——`TKW.Framework.Domain/AuthController/AesGcmCredentialProtector`（`internal sealed`，持有 `readonly byte[] _key`，DI 单例经 `RegisterAuthServices` 注册）**已是框架侧有状态密钥持有者先例**；且 `TKWF.Utility.Cryptography` 有「无状态静态」铁律（组件指南 §2.2），有状态容量对象本就不入 Utility（落 Domain 与先例同域，不冲突）。OAuthClient 归层判据（纯协议引擎→Utility）**不适用于**密钥持有者。

## 三、使用场景

### 适用场景

- **扩展侧对称密钥持有**（族系 B）：AuthCenter `PlatformCredentialKeyStore` / Federation `FederationSecretKeyStore` / MFA `MfaSecretKeyStore` → `[FromKeyedServices(SymmetricKeyProviderKeys.Xxx)] ISymmetricKeyProvider` 构造注入（生产 fail-fast / 开发两分支语义保留）。
- **扩展侧开发模式临时签名密钥缓存**（族系 A）：`DevRsaKeyCache`/`DevEcKeyCache` → `DevKeyCache<TokenService.RsaKeySet>`/`DevKeyCache<Token2Service.EcKeySet>` DI 单例（`where TKey : class, IDisposable`——密钥集实现 `IDisposable` 自管内部密钥释放）。
- **业务领域项目**（框架消费方）：任意需要"文件路径驱动 AES-GCM 对称密钥"的消费方，经 `AddKeyedSingleton` + `[FromKeyedServices]` 复用同一抽象（跨扩展/跨项目零复制）。

### 不适用边界

- **签名密钥管理全量统一**（prod PEM 加载 + dev 缓存 + kid 轮换合并为 KeyRing）：TokenService/Token2Service 加载逻辑各异（RS256 PKCS#1 vs ES256）——E4 原始意图（OpenIddict 四层凭证 + 自动轮换 + 自定义 KeyStore + HSM）未触发（无独立 OAuth 服务器/通知签名/HSM 消费方出现），演进方向单列，不在本 ADR 范围。
- **`ICredentialProtector` 合并**：不同契约（byte[] 凭据保护 vs string 业务密文），保持并列。
- **`ResetForTests` 公开化**：测试钩子 internal（`InternalsVisibleTo` 测试访问），公开接口不含测试关注（契约约束）。

## 四、选项

### 选项 ①：上提主框架（选定——本次裁定）

- 描述：`ISymmetricKeyProvider`（接口落 `Core/KeyManagement`，命名空间 `TKW.Framework.Domain.KeyManagement`）+ `FileSymmetricKeyProvider`（实现落 `Domain/KeyManagement`，`public sealed`，ctor `(string? keyPath, bool isProduction, ILogger? logger = null)` 构造即加载：生产缺失 fail-fast / 开发两分支）→ 框架 Domain；`DevKeyCache<TKey>`（`Utility.Caching`，`where TKey : class, IDisposable`）→ 框架 Utility；`SymmetricKeyProviderKeys` 常量（Core）→ 框架；扩展侧 keyed 注册消费。
- 优点：零复制（三扩展 + 未来消费方共用）；格式统一（委托 `AeadEncryptionUtil` byte[] 重载，单段规范）；DI 化消灭静态单例（测试并行化、Reset 契约统一）；与 `AesGcmCredentialProtector` 有状态先例同域一致。
- 缺点：主框架首次承载扩展名常量（`SymmetricKeyProviderKeys.AuthCenter/Federation/Mfa`——C11 耦合取舍：单源 typo-proof，第 4 个扩展需 keyed 时主框架加 const）；keyed DI 首次引入（ADR96 已收编，C4 端到端实证 `ActivatorUtilities` 守卫工厂对 `[FromKeyedServices]` 生效）。

### 选项 ②：扩展侧独立 `KeyStore.Abstractions` 契约包

- 描述：扩展侧建契约包承载密钥抽象。
- 缺点：承载实现而非契约，破坏 ADR48 D7「契约包零实现/零框架引用」语义（`KeyStoreBase` 需引 `Microsoft.Extensions.Logging`/Options）；三扩展 + 未来消费方复制面仍在；格式统一仍需框架侧 `AeadEncryptionUtil`。

### 选项 ③：扩展组内共享工具类

- 描述：扩展仓库内共享项目/源码收敛。
- 缺点：本仓库无共享项目载体（目录全为每扩展一项目），触及"扩展间 ProjectReference 打包传递"与"公共内容不打包"边界；业务领域消费方（非扩展）无法复用。

## 五、决策

选定：**选项 ①——密钥管理抽象上提主框架（Domain/Utility/Core 分域）+ keyed DI 注册消费**。

理由：
1. **框架已有有状态密钥持有者先例**（`AesGcmCredentialProtector` in Domain）——「容量对象留扩展域」裁定与既有代码矛盾，上提是纠正而非偏离。
2. **消除 5 份复制 + 3 份内联 AesGcm + 格式分裂**——委托 `AeadEncryptionUtil`（v4.10.61 补 byte[] 重载）单段规范，零扩展侧密码学实现。
3. **DI 化消灭静态单例**——`ResetForTests` 分裂（2 死钩子 + 1 缺失靠反射）终结为 internal 契约；`DisableTestParallelization` 撤销（测试并行化）。
4. **keyed DI 是 .NET 8+ 原生最优解**（ADR96 收编）——三扩展同容器 last-wins 冲突的唯一干净解；`ActivatorUtilities` 守卫工厂对 `[FromKeyedServices]` 生效已框架侧 C4 端到端实证。
5. **加密边界统一服务/方法层**——AuthCenter PlatformCredential 加密从 DataService 边界上移（消除唯一 SG1 固定两参 ctor 阻塞点，拓扑对齐 Federation/MFA 既有服务层加密）。
6. 否决 ②（契约包承载实现语义破坏）/③（无共享载体 + 消费面窄）。

## 六、后果

### 正面影响

- 三扩展零静态密钥类（AuthCenter V0.7.0 / Federation V0.2.0 / MFA V0.2.0）；密钥生命周期统一（生产 fail-fast / 开发两分支 + internal Reset 契约）；格式统一单段（存量三段密文一次性迁移或重注册——用户裁定不考虑兼容/过渡）。
- 测试并行化（AssemblyInfo 撤销 DisableTestParallelization）+ MFA 反射复位删除 + Federation 死钩子删除。
- 框架侧通用资产（任意消费方复用）+ keyed DI 首次引入（ADR96 先例）。

### 负面影响 / 风险

- **key 字符串隐式契约**：`SymmetricKeyProviderKeys` 常量类单源（typo 防呆）；第 4 个扩展需 keyed 时主框架加 const（C11 取舍已记录）。
- **存量三段密文不可读**：切换后既有 `PlatformCredential.AppSecretEncrypted`/`SsoClient` secrets/`MfaSecret.SecretEncrypted` 三段格式不可读——一次性数据迁移（旧 key 解密→单段重加密）或接受重注册/重绑定；三扩展均为 2026-10-05/06 新发 + DMP-Lite 未迁移——大概率无生产存量。
- **框架发布依赖**：扩展侧 CPM 升级 4.10.61（v4.10.61 已发布，本 ADR 前置门解除）。

### 后续待办

- 演进方向（E4 原始意图）：签名密钥管理全量统一（`ISigningKeyProvider`/KeyRing）——触发条件：独立 OAuth 服务器/通知签名/第三方 IdP 需 HSM；自动轮换 + 自定义 KeyStore（OpenIddict 四层凭证 + Duende 自动密钥管理）。

## 七、关联文档

- 转达：主框架 `docs/03_扩展模块/转达/转达-E4密钥管理抽象-触发成熟-请扩展组立项.md`（2026-10-05）
- 转告回复：主框架 `docs/03_扩展模块/转达/转告回复-E4密钥管理抽象-已立项-请框架组配套实施.md`（2026-10-06）
- 转达（发布）：主框架 `docs/03_扩展模块/转达/转达-v4.10.61-E4密钥管理配套发布-请扩展组CPM升级.md`（2026-10-06）
- 开发方案：`docs/KeyStore/E4密钥管理抽象-开发方案.md`（Oracle PASS WITH CONDITIONS，6×P1 + 4×P2 修订闭环——bg_af4c585d）
- 框架侧：ADR96-E4密钥管理抽象上提主框架与keyedDI首次引入.md（主框架）+ `Core/KeyManagement/` + `Domain/KeyManagement/` + `Utility/Caching/DevKeyCache.cs`
- 被替代：`docs/AuthCenter/ADR/ADR-Authentication-DevRsaKeyCache.md`（「可复用模式」注记标注废弃引用本 ADR——E4 已抽象化落地）

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-06 | 初始版本——E4 密钥管理抽象上提主框架裁定（推翻转达 §四.3 不上主框架）：5 份复制链收敛（族系 A → `DevKeyCache<TKey>` + 族系 B → `ISymmetricKeyProvider`/`FileSymmetricKeyProvider`）+ keyed DI 首次引入（ADR96）+ 格式统一单段 + 加密边界上移服务层 + Reset 契约 internal；Oracle 评审 PASS WITH CONDITIONS（6×P1 + 4×P2 修订闭环，bg_af4c585d）；三扩展实施（AuthCenter V0.7.0 / Federation V0.2.0 / MFA V0.2.0）全量回归通过 |

<!-- EOF -->
