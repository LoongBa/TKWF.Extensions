# ADR-Authentication-DevRsaKeyCache

## 状态

活跃

> 本 ADR 为永久架构决策记录，不可删除。如后续决策被推翻（如开发模式密钥持久化方案落地替代静态缓存），须在本 ADR 标注「已废弃」并引用新 ADR，而非删除本文件。

## 一、目的与目标

确立「**开发模式临时 RSA 密钥进程内静态缓存**」模式——`TokenService` 在 `SigningKeyPath` 未配置 && `IsProduction=false` 时，进程内所有实例共享**同一临时密钥集**（签发/验签一致性），消除跨 scoped 实例 `INVALID_SIGNATURE`；生产分支（PEM 文件）天然一致，**绝不**走缓存。读者应在 3 句话内明白：缺陷 = 每个 TokenService 实例各自 `Lazy<RsaKeySet>` → dev 分支每次 `RSA.Create()` 新建密钥 → 签发实例与验签实例密钥不同 → 带 Bearer 验签 INVALID_SIGNATURE（EduPlatform 反馈，框架组转达 2026-10-05）；本 ADR 裁定新增 `DevRsaKeyCache`（internal static + 双重校验锁 + `ResetForTests()` 测试隔离钩子）接入 `LoadKeysCore` dev 分支；同时确立**可复用模式**（未来 dev 临时加密材料可同形处理）与**测试隔离契约**（静态缓存必须有 Reset 钩子，勿重犯 `PlatformCredentialKeyStore` 缺 Reset 缺陷）。

## 二、问题

### 问题现象

- **开发模式跨实例验签失败**：`TokenService` 每实例字段 `private readonly Lazy<RsaKeySet> _keys`（L51/L66），dev 分支（`LoadKeysCore` L278）`signingKey = RSA.Create(RsaMinKeyBits)` ——**每次调用生成新密钥**且无进程内共享。每个 scoped 实例（每请求一个）密钥集不同 → 短信登录签发实例与 `/me` 带 Bearer 验签实例（`LocalJwtTokenVerifier` 经 `User.Use<ITokenService>()` 解析的**另一个实例**）密钥集不同 → `ValidateTokenAsync`（L131-134 `RSA.VerifyData`）用完全不同密钥 → 抛 **`INVALID_SIGNATURE`**。
- **既有测试漏检**：`TokenServiceTests.Dev_MissingSigningKey_AutoGeneratesTemporary`（L267-277）**只测单实例**自带签发+验签——单实例密钥集内部自洽必通过，无法暴露跨实例不一致（skill §4.8 心得 8 同型：测试桩/单实例掩盖真实缺陷）。

### 触发场景

- 开发模式（`SigningKeyPath` 空 && `IsProduction=false`）下，短信登录签发 token → 任意带 Bearer 受保护端点验签 → INVALID_SIGNATURE（EduPlatform 反馈实证）。
- 多实例/多请求并发（每个请求一个 scoped TokenService）——密钥集各自独立。

### 现有方案不足

- **每实例 Lazy**（缺陷本体）：`Lazy<RsaKeySet>` 保证**单实例内**一致，但**跨实例**无共享——RSA 密钥是重对象（RSA.Create 2048-bit 生成耗时 + 内存），每请求重新生成既慢又不一致。
- **V0.3.1 "static 单一真相源" 误导**：`LoadKeysCore` static 只共享**实现代码路径**（runtime 实例与 Initializer 预检共用同一方法），**非密钥集实例共享**——与缺陷无关，勿被其注释误导（转达 §二 明确）。
- **`PlatformCredentialKeyStore` 既有模式缺 Reset**：其双重校验锁（`_key` + `Gate`，L16-17/L22-25）是本 ADR 镜像基础，但**无测试隔离 Reset 钩子**——静态缓存跨测试类泄漏，是既有缺陷，本模式**不延续**（转达 §三 明确要求）。

## 三、使用场景

### 适用场景

- **开发模式令牌链路**（Authentication 扩展）：无 PEM 配置下进程内所有 TokenService 实例共享临时密钥 → 签发/验签成功（缺陷消除）。
- **未来 dev 临时加密材料**（可复用模式）：任何"开发模式临时生成 + 进程内一致"的加密材料（如 dev AES 密钥、dev 证书）可同形处理（internal static + 双重校验锁 + ResetForTests）。
- **测试**：`DevRsaKeyCacheTests`（T1 跨实例签发/验签成功 + T2 Reset 隔离）+ 既有 dev 用例（setup Reset 防泄漏）。

### 不适用边界

- **生产分支绝不走缓存**：`IsProduction=true` 缺 PEM 必须 fail-fast（`LoadKeysCore` L275-276 既有语义不变）——不得静默缓存临时密钥掩盖生产配置错误。
- **PEM 配置分支不走缓存**：文件密钥天然跨实例一致（每次从 PEM 加载同一密钥对）——缓存冗余。
- **不缓存派生公钥单独对象**：缓存**整个 `RsaKeySet`**（CurrentKid + SigningKey + VerifyKeys）——签发/验签需一致性密钥对，只缓存 SigningKey 而每次重算派生公钥 = 密钥集不一致复现（转达 §三 明确）。
- **多 AuthCenter 实例同进程 dev 场景（Oracle 评审 MINOR）**：单槽缓存进程内只一份——若两个不同配置的 AuthCenter 实例（不同 `Issuer`/`CurrentKid`）同进程 dev 运行，共享同一密钥集（iss/kid 校验链会捕获配置不一致——`ISS_MISMATCH`/`KID_UNKNOWN`，可观察可调试）；若需严格隔离请各自配置 PEM。

## 四、选项

### 选项 ①：`DevRsaKeyCache` 进程内静态缓存（选定）

- 描述：新增 `DevRsaKeyCache.cs`（internal static）：`_cached`（`TokenService.RsaKeySet?`）+ `Gate` 双重校验锁 `GetOrCreate(Func<RsaKeySet>)`（工厂仅首次执行）+ `ResetForTests()`（Dispose 已缓存密钥后置 null）。`LoadKeysCore` dev 分支改从 `DevRsaKeyCache.GetOrCreate(() => CreateDevKeySet(...))` 取；生产分支不变。
- 优点：零破坏（internal 新增 + dev 分支行为修复）；镜像 `PlatformCredentialKeyStore` 既有模式（一致性认知）；补 Reset 钩子（测试隔离）；进程内共享（性能 + 一致性双赢）；生产语义零变化。
- 缺点：进程内静态状态（重启即变——dev 语义本就"重启即变"（L277 注释），可接受）；测试需 Reset 管理（已内置）。

### 选项 ②：dev 密钥持久化（写入固定 PEM 文件）

- 描述：dev 模式首次生成后写入固定路径（如 `./keys/dev-rsa.pem`），后续从文件加载。
- 缺点：**破坏 dev 便利性**（L277 "不落盘——重启即变，仅开发便利" 设计意图）；跨实例**不持久化也够**（进程内共享即可解决）；引入文件写入副作用 + 清理负担——过度设计。

### 选项 ③：`TokenService` 改 singleton

- 描述：TokenService 注册改 singleton，进程内单实例天然共享密钥。
- 缺点：**违反守卫工厂 scoped 语义**（`AddConstructibleService` 注册 + ctor(IDomainUser)——IDomainUser 是 scoped/请求级，singleton 无法供给）；TokenService 依赖 scoped DataService 链——singleton 生命周期错配（captive dependency）；破坏 V4.10.53 领域自治整改。

### 选项 ④：不实施（维持现状 + 文档说明"dev 模式需配 PEM"）

- 描述：使用指南标注"dev 模式必须配置 SigningKeyPath"。
- 缺点：**阻断开发环境认证链路**（转达定性 P0——EduPlatform 已用固定 PEM 绕行，但其他消费方会踩）；dev 便利性丧失；缺陷保持。

## 五、决策

选定：**选项 ①——`DevRsaKeyCache` 进程内静态缓存（internal static + 双重校验锁 + ResetForTests 测试隔离钩子）**。

理由：
1. **最小修复面**：仅 dev 分支接入缓存，生产分支/PEM 分支零变化——零破坏增量。
2. **缺陷根治**：进程内所有 TokenService 实例共享同一密钥集 → 签发/验签一致性（INVALID_SIGNATURE 消除的唯一证明 = T1 跨实例测试）。
3. **模式自洽**：镜像 `PlatformCredentialKeyStore` 双重校验锁既有模式（仓库认知一致）；补其缺 Reset 的缺陷（测试隔离契约）。
4. **性能收益**：RSA.Create（2048-bit）重对象每请求重建 → 进程内一次性生成（dev 场景并发请求收益明显）。
5. **生产安全**：`IsProduction` 分支绝不走缓存（fail-fast 语义保留）——缓存仅 dev 便利，不掩盖生产配置错误。
6. 否决 ②（破坏 dev 不落盘便利 + 文件副作用）/③（scoped 守卫工厂语义 + DataService 链 captive）/④（P0 缺陷不修）。

## 六、后果

### 正面影响

- 开发模式令牌链路恢复（签发/验签跨实例成功——T1 证明）；EduPlatform 可去 PEM 绕行回 dev 便捷模式。
- 模式可复用（未来 dev 临时加密材料同形处理）+ 测试隔离契约确立（Reset 钩子规范）。
- 性能：dev 场景每进程 1 次 RSA.Create（替代每请求重建）。

### 负面影响 / 风险

- **进程内静态状态**：dev 模式下密钥集进程生命周期内固定（重启即变）——符合 dev 语义（L277 "重启即变" 设计意图），多实例部署各自生成（dev 场景无跨进程一致性要求）；文档标注。
- **测试管理负担**：任何走 dev 分支的测试须 setup Reset（已内置 `DevRsaKeyCacheTests` ctor + 既有 dev 用例）；`AuthenticationProductionPathTests`（PEM 分支）零接触。
- **RsaKeySet Dispose**：`ResetForTests` 先 `SigningKey.Dispose()` + `VerifyKeys` 全量 Dispose 再置 null（防托管资源泄漏——转达 §六 要求，已实现）。

### 后续待办

- 使用指南标注 dev 模式行为（进程内共享临时密钥；重启即变；多实例各自生成）。
- `PlatformCredentialKeyStore` 补 `ResetForTests()`（本 ADR 模式确立后回补既有缺陷——独立小任务，不随 V0.5.3）。
- dev 密钥持久化（若未来有跨重启 dev 一致性需求）评估——YAGNI 当前不预做。

## 七、关联文档

- 转达：主框架 `docs/03_扩展模块/转达/转达-Authentication开发模式临时RSA密钥跨实例不一致-INVALID_SIGNATURE-请扩展模块组修复.md`（2026-10-05，P0）
- 缺陷代码：`_Framework/Authentication/TokenService.cs`（L51/L66 `Lazy<RsaKeySet>`；L261-304 `LoadKeysCore`；L278 `RSA.Create`；L131-134 验签）
- 镜像模式：`_Framework/Authentication/PlatformCredentialKeyStore.cs`（L14-58 双重校验锁）
- 测试：`_Tests/Extension.Authentication.Tests/DevRsaKeyCacheTests.cs`（T1/T2）+ `TokenServiceTests.cs` L267-277（既有 dev 用例）

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-05 | 初始版本——框架组转达（P0）闭环：`DevRsaKeyCache`（internal static + 双重校验锁 + ResetForTests）接入 `LoadKeysCore` dev 分支；生产分支不变；RsaKeySet private → internal（复用同一类型）；T1 跨实例签发/验签 + T2 Reset 隔离回归测试；否决 ②持久化/③singleton/④不修 |
| 2026-10-05 | Oracle 评审调整（bg_a0ced91a，近最优可发布）：**MAJOR** `PlatformCredentialKeyStore` 补 `ResetForTests()`（本 ADR 契约闭环——镜像模式不留缺 Reset 缺陷）+ `PlatformCredentialServiceTests` ctor Reset；**MINOR** `DevRsaKeyCache._cached` 加 `volatile`（DCL 教科书完整性）；`CreateDevKeySet` 移除恒 null 的 `pemSigningKey` 参数（死代码收敛）；§三 不适用边界补多 AuthCenter 同进程 dev 注记 |
