# ADR-AuthCenter-密码策略与口令协议

> **状态**：已批准（Oracle1 评审：APPROVE WITH CONDITIONS——B1/B2/B3 Blocker + M1-M6 次要条件已并入；用户裁定"传输/入库/对比均不明文、无明文降级"）
> **日期**：2026-10-07
> **关联**：ADR-AuthCenter-身份域数据模型与密码能力边界（B.9-B.11 归属裁定）· ADR-RateLimiting-点检查原语与三层限流分工（频控接线）· ADR-AuthCenter-归层与命名 · `docs/AuthCenter/v0.9.0-身份域重构与密码能力-开发方案.md`（P0-1 方案 A 覆盖 + T6 协议形态覆盖）· ADR48 D7（依赖倒置）· ADR90（领域自治）· AGENTS §8（配置分层）· SecurityLog README §四（写路径边界声明同步）
> **性质**：密码能力**策略面**裁定——身份域 ADR 已定归属与链路（验证/修改/找回），本 ADR 补足**协议形态 / 限流接线 / 锁定与冻结边界 / 密码策略面 / TokenVersion 触发点**五组裁定

---

## 一、目的与目标

裁定 AuthCenter 密码能力的**策略面**：确立 ① 口令协议走框架 SecurePassword（服务端永不接触明文）；② 密码频控经 `IRateLimitCheck` 双 Provider（内存 fallback + 扩展 DB）；③ **锁定=限流**（不建独立表、不落账号状态）与**账号冻结=持久化账号状态 + 认证检查 + SecurityLog 事件**的边界；④ 复杂度/历史防重用/强制轮换/初始密码策略面；⑤ TokenVersion++ 权威触发点集合。目标读者应在 3 句话内明白：AuthCenter 密码能力 = 框架 SecurePassword 原语自建 UId 编排 + 点检查限流 + 状态列冻结（非限流），密码策略面纯自建挂单入口，改密/找回/绑定变更联动 TokenVersion 失效旧 refresh。

## 二、问题

### 问题现象
1. **口令协议形态错误**：V0.9.0 开发方案 T6 已实现 `PasswordAuthenticationProvider` + `SetPasswordAsync`/`ChangePasswordAsync`，但走 **PasswordHasher 明文协议**（`PasswordHasher.VerifyPassword(context.Password, account.PasswordHash)` 明文比对、`HashPassword(newPassword)` 明文入参）——**服务端接触明文密码**，违反用户裁定"传输/入库/对比均不明文"；`AuthAccount.PasswordHash` 列存的是服务端算的散列（350000 硬编码迭代），非客户端算的 clientHash。框架已有完整 SecurePassword Challenge-Response 协议（服务端零明文），开发方案未消费。
2. **频控方向裁定矛盾**：`docs/AuthCenter/v0.9.0-身份域重构与密码能力-开发方案.md` P0-1 方案 A（Oracle 裁定）「V0.9.0 不消费 IRateLimitCheck，用 AuthLoginAttempt COUNT」与 ADR-身份域 裁定 15「密码能力频控走 IRateLimitCheck」**直接冲突**——defer 前提（v4.10.67 未发布 CI 编译失败）已失效。
3. **登录保护空白**：`IsRateLimitedAsync` 的 `AuthTypes.Password` 落 `_ => false` **fail-open 分支**（口令认证完全无窗口计数）；`RecordAttemptAsync`/`IsRateLimitedAsync` 在 `AuthCenterWebExtension` 中**零调用**（OAuth/Redeem 窗口未接线）；AuthCenter **无锁定机制**（无 LockoutEnd/FailedCount，`IsRateLimitedAsync` 返回 bool 不累积状态）。
4. **账号状态模型残缺**：AuthAccount 唯一状态列 `IsEnabled`（bool 默认 true）；无冻结/禁用门面方法；`TokenService.ValidateTokenAsync` 不查账号态（存量 2h access 禁用后仍有效）；`/verify`、`/grants` 仅验签不查态。冻结（临时安全处置）与禁用（终结关停）语义混淆风险。
5. **TokenVersion 无自动触发**：`IncrementTokenVersionAsync` 仅外部显式调用；`SetFederationAnchorAsync` 改绑定**未挂钩 ++**（与列注释「密码/绑定变更自增」承诺不一致，现存实现缺口）。
6. **密码策略面纯绿地**：框架/扩展全仓 `PasswordPolicy|PasswordValidator|PasswordComplexity|WeakPassword`、`PasswordHistory|PasswordAge|PasswordRotation|InitialPassword|MustChangePassword` **零匹配**——复杂度/历史/轮换/初始密码全部自建。
7. **哈希迭代数口径分裂**：`PasswordHasher`（TKWF.Utility）硬编码 350000 vs `DomainAuthOptions.Pbkdf2Iterations` 默认 600000（SecurePassword 客户端 PBKDF2）。格式自描述（Verify 解析串内迭代数）功能兼容，但口径分裂是设计瑕疵。

### 触发场景
- AuthCenter 新增密码登录 Provider、修改密码、找回密码（短信/邮件/扫码多通道）；
- 消费方多实例部署——密码频控/锁定需跨实例正确（DB 后端）；
- 账号被安全事件冻结（异地登录/爆破响应）——需临时阻断登录 + 可自动到期 + 安全日志留痕；
- 改密/找回/绑定变更——需旧 refresh token 失效（TokenVersion++）。

### 现有方案的不足
- **明文口令路径**（服务端收明文）：明文过网络/落服务端内存/日志风险，AuthCenter 定位认证中心不应接受（用户裁定"不能明文"）。
- **复用 Account 锁定**：`AccountLockout` 表按 `userName` 键模型 + 主框架 `AuthController.LoginByContextAsync` 驱动——AuthCenter Provider 认证矩阵不经过该路径，**不会自动生效**；与 UId 模型结构性不兼容（ADR-身份域 已裁定平行不互认）。
- **锁定落账号状态**（`IsLocked`/`LockoutEnd` 入 AuthAccount）：混淆"限流（时间自动恢复）"与"状态（显式处置）"，且触 A.2 列冻结白名单。
- **冻结复用 AuditLogging 日志**：AuditLogging 实体无 EventType/EventCategory/IpAddress/UserAgent/Result（仅方法调用四元组），冻结事件无字段可落；且边界声明只在 SecurityLog README §一（"方法调用审计归 AuditLog，安全事件归 SecurityLog"）。

## 三、使用场景

### 适用场景
- AuthCenter 密码生命周期：验证（`PasswordAuthenticationProvider`）/ 修改（`SetPasswordAsync`）/ 找回（SMS/Email/扫码多通道，自建 UId 链路）；
- 密码策略：复杂度校验、历史防重用、强制轮换（消费方按需开启）、初始密码强制改密；
- 账号冻结：安全事件临时冻结（带 `FreezeEnd` 自动到期）、管理端解冻；认证路径拦截 + SecurityLog 事件；
- 密码能力频控/锁定：`IRateLimitCheck`（内存默认 + SqlCount 扩展跨实例）。

### 不适用边界
- **Identity userName 模型密码流程**（`IPasswordResetFlow`/`IAccountPasswordManager` 服务 Identity，不用于 AuthAccount——ADR-身份域 B.10 已裁定不实现第二实现）；
- **MFA 本体**（独立扩展；改密/找回挂 MFA = 消费方编排可选强化，ADR-身份域 C.12）；
- **非窗口算法**（token bucket/concurrency）→ 官方 `PartitionedRateLimiter`（Domain AOP 层），不进 `IRateLimitCheck`；
- **Web 层粗粒度限流** → RateLimiting 扩展中间件（三层分工不变）；
- **登录保护审计**（`AuthLoginAttempt`/`SmsRecord` 审计表 COUNT）保留双目的，不并入通用计数表（ADR-RateLimiting 决策规则）；
- **存量 access token 主动撤销**（冻结/禁用/改密均靠 2h 短 TTL 自然失效，"按 UserId 批量撤销"列 v0.2.0 候选，不引入 access 级主动撤销）。

## 四、选项

### 决策点 1：口令协议形态
- **A（选定）**：**统一 SecurePassword 协议，零明文降级**——复用框架原语（`ICredentialProtector` AES-GCM + `IChallengeTokenService` + `DomainAuthOptions.Pbkdf2Iterations` 单一来源），**AuthCenter 自建 UId 面向编排**（不走 `AuthController`/`DomainUserHelperBase`，落 `AuthAccount.PasswordHash` 存客户端算的 clientHash 组装格式）。理由：① 用户裁定"传输/入库/对比均不明文"（服务端零明文）——明文降级选项**不提供**（降级=自开服务端见明文旁路、难移除、违 fail-closed）；② 框架原语现成（V4.5 完整协议）；③ 客户端 ts-client 已对齐（`IdentityPasswordManager` 实证：客户端传 newClientHash+salt 只存不算——**注：此为重置流程实证，登录验证同样适用此形态**）；④ 迭代数口径统一（600000，不采 PasswordHasher 硬编码 350000）；⑤ 改密可 HMAC 校验旧密码。**覆盖开发方案 T6（PasswordHasher 明文协议）+ 现存代码 + PasswordCapabilityTests 测试重写。**
- B：明文一步登录（HTTPS 保护传输），服务端 `PasswordHasher` 算 hash——服务端接触明文，违裁定。**否决（不提供降级）。**

### 决策点 2：密码频控/锁定载体
- **A（选定）**：`IRateLimitCheck`（三层限流分工 ADR 的点检查层）——AuthCenter Initializer `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>()` fallback（未启用扩展限流时频控"始终在"，fail-closed）；消费方显式注册 `SqlCountRateLimitCheck` 得 DB 跨实例版（RC1 确定性路径）。锁定语义 = `TryAcquire` 失败 + `GetRetryAfter` 阻断期剩余，**不建独立锁定表、不落账号状态**。**覆盖 P0-1 方案 A 的 defer**（其前提 v4.10.67 未发布已因框架组 R1 交付 + 扩展组实施中而失效），恢复 ADR-身份域 裁定 15 方向。
- B：AuthLoginAttempt COUNT 滑窗（补 `AuthTypes.Password` 分支 + 端点接线）——当前零接线、无 Password 分支，且 COUNT 是审计查询无锁定阻断语义。否决（仅作审计保留）。

### 决策点 3：账号冻结载体与日志
- **A（选定）**：`IsFrozen`（bool）+ 可选 `FreezeEnd`（DateTime?）**独立列**入 AuthAccount（**不做 bool→枚举**——禁用=终结态/人工恢复 vs 冻结=临时态/可自动到期，语义正交，枚举互斥丢"启用但冻结"组合 + 自动解冻时间维度）；认证路径 4 处 IsEnabled 检查旁补冻结检查（拦新签发 + refresh，存量 access 靠 2h TTL）；日志归 **SecurityLog 扩展事件**（`SecurityLogEventTypes` 加 `Freeze`/`Unfreeze` 两 const，AuthCenter 直写 `User.Use<ISecurityLogStore>()`，不动 SecurityLogFilter 白名单）。三证据：① 数据语义（SecurityLog README §一"方法调用审计归 AuditLog，安全事件归 SecurityLog"）；② 模型可承载（AuditLogging 无 EventType/IP/Result，SecurityLog 有 + `Authorization` 分类预留）；③ 扩展点现成（const string 类非枚举 + `Options.EventTypes` 开关）。
- B：`IsEnabled` 单布尔承载冻结——禁用≠冻结，混为一谈。否决。
- C：并入 `IsEnabled` 改枚举（`Active/Disabled/Frozen`）——互斥丢正交组合 + 无时间维度 + 破坏既有 4 处消费。否决。
- D：冻结日志走 AuditLogging——模型不可承载（无事件类型/无 IP/无 Result）。否决。

### 决策点 4：TokenVersion++ 触发点
- **A（选定）**：权威触发点集合 = 改密（`SetPasswordAsync` 落地）++ / 找回（`CompleteResetAsync` 落地）++ / 联邦绑定变更（`SetFederationAnchorAsync`）++（**收口现存缺口**）。MFA 绑定/解绑登记为关联待办（解绑 MFA=安全降级，严格说旧 refresh 应失效，但属 MFA 域语义，非本次强制）。
- B：仅改密 ++——找回/绑定变更不失效旧 refresh。否决（凭据变更应同语义）。

### 决策点 5：密码策略面（纯绿地自建）
- **A（选定）**：复杂度（Options 可配默认最小长度 8 + 类别 ≥2，挂 `SetPasswordAsync`/`CompleteResetAsync` 单入口强制校验，fail-closed）+ 历史防重用（独立 `PasswordHistoryEntity` 只增表，保留 N 代可配默认 3）+ 强制轮换（Options 默认关，验证时查历史表最近改密时间）+ 初始密码（`MustChangePassword` 标记，首次登录强制改密）。
- **配置分层**（AGENTS §8）：全部归领域配置 `TKWF:AuthCenter:PasswordPolicy` 节，Web 层不存在对应开关（安全行为不可由表现层关闭）。
- **A.2 白名单准入（Oracle1 B2 修正）**：`LastPasswordChangedAt` **不入 AuthAccount 列**（强制轮换默认关 ≠ "每个部署普遍需要"，且认证时不采集）——最近改密时间放 `PasswordHistoryEntity` 最新行；`MustChangePassword` 逐列论证通过（首次登录认证时检查 ✓；初始密码强制改密是多数消费方普遍场景 ✓）。`IsFrozen`/`FreezeEnd` 准入论证见 §六。

## 五、决策

1. 口令协议：**统一 SecurePassword 协议，零明文降级**——复用框架原语（`ICredentialProtector`/`IChallengeTokenService`/`DomainAuthOptions.Pbkdf2Iterations`），AuthCenter 自建 UId 面向编排，`AuthAccount.PasswordHash` 存客户端算的 clientHash 组装格式（自描述 `"{iterations}.{b64salt}.{b64hash}"`，迭代数取 `DomainOptions.Auth.Pbkdf2Iterations` 单一来源，不采用 PasswordHasher 硬编码 350000）。**覆盖开发方案 T6 + 现存 PasswordHasher 明文实现 + PasswordCapabilityTests 测试重写。**
2. 密码频控/锁定：`IRateLimitCheck` 双 Provider（AuthCenter Initializer 注册 `MemoryRateLimitCheck` fallback `TryAddSingleton`；消费方显式注册 `SqlCountRateLimitCheck` 得 DB 跨实例）；**锁定=限流**（`TryAcquire`+`GetRetryAfter`），不建独立锁定表、不落账号状态。**覆盖 P0-1 方案 A defer**。
3. 账号冻结：`IsFrozen` + 可选 `FreezeEnd` 独立列（A.2 白名单准入论证见 §六）；认证路径补冻结检查（拦新签发 + refresh）；SecurityLog 加 `Freeze`/`Unfreeze` 事件类型，AuthCenter 直写 `User.Use<ISecurityLogStore>()`。
4. TokenVersion++ 触发点集合：改密 / 找回 / 联邦绑定变更；MFA 绑定解绑为关联待办。
5. 密码策略面：复杂度 + 历史表（含最近改密时间，`LastPasswordChangedAt` 不入 AuthAccount）+ 轮换（默认关）+ 初始密码标记（`MustChangePassword` 入 AuthAccount，逐列准入论证通过），单入口强制校验，领域配置分层。
6. 冻结日志写入策略：**SecurityLog 未启用时 catch + Warning 降级**（冻结业务关键操作，日志写失败不阻断冻结；`SecurityLogStore.SaveAsync` 自身已静默 catch，语义一致）；写入侧自读 `IOptions<SecurityLoggingOptions>` 判定 `EventTypes` 门控（直写绕过过滤器内过滤，须补）——**Options.Enabled=false 或 EventTypes 不含 Freeze/Unfreeze 时跳过写入**（对齐过滤器语义，不落库）。
7. `SecurityLogEventTypes` 加 2 const（`Freeze`/`Unfreeze`）；事件分类用 `CategoryAuthentication`（`Authorization` 预留但未采集，本次不引入）；`UserName` 填被冻结账号（非操作者），操作者身份放 `Detail`。**同步修订 SecurityLog README §四"唯一写路径"声明**（Oracle1 B3）——改为"过滤器采集为主路径；扩展可经 `User.Use<ISecurityLogStore>()` 直写安全事件（须自读 Options 门控 EventTypes + Options.Enabled）"。

## 六、后果

### 正面影响
- 口令服务端零明文（传输/入库/对比均不明文）+ 迭代数口径统一（600000 单一来源）；
- 锁定归限流层（无状态、时间自动恢复、跨实例可用）与冻结归状态层（持久化、显式处置、可自动到期）边界清晰，各司其职；
- TokenVersion 触发点权威集合收口 `SetFederationAnchorAsync` 现存缺口；
- 密码策略单入口强制校验（fail-closed 不可绕过）+ 领域配置分层（Web 不可关安全行为）；
- SecurityLog 事件扩展是设计内演进（const string + Options 开关），零破坏；README §四边界声明同步修订消除"唯一写路径"歧义。

### 负面影响与风险
- **A.2 白名单准入**：`IsFrozen`/`FreezeEnd`/`MustChangePassword` 三列入 AuthAccount 须按 A.2 准入标准（"认证时采集 ∩ 每个部署每个消费方普遍需要"）论证——IsFrozen/FreezeEnd（认证路径检查 ✓ + 账号安全处置普遍 ✓）通过；MustChangePassword（首次登录检查 ✓ + 初始密码普遍 ✓）通过；**`LastPasswordChangedAt` 不入 AuthAccount**（轮换默认关 ≠ 普遍需要，放 `PasswordHistoryEntity` 最新行）。V0.9.0 开发方案 T1 白名单需更新。
- **存量 access 冻结后仍有效**（2h TTL 自然失效）——如需即时踢下线需"按 UserId 批量撤销"（v0.2.0 候选，不引入）。
- **锁定语义差异**：Memory=sliding / SqlCount=fixed（窗口边界突发行为差异，ADR-RateLimiting RC2 已文档化）——**缓解建议（Oracle1 M6）**：密码登录/找回为低频敏感操作，建议消费方优先显式注册 `SqlCountRateLimitCheck`（fixed 边界双倍突发可接受且跨实例正确）；仅单实例部署可用 Memory fallback。
- **SecurityLog 直写绕过 `EventTypes` 门控**（写入侧补读 Options 已入决策 6，Enabled=false 跳过写入）。
- **跨扩展配置耦合（Oracle1 M5）**：AuthCenter 冻结门面读取 `IOptions<SecurityLoggingOptions>`——记录为已知耦合（SecurityLog Options 形态稳定，可接受）。
- **消费方迁移**：`EnabledAuthTypes` 需显式含 `"password"` 才接线密码 Provider（fail-closed）；SecurePassword 协议要求客户端 PBKDF2（ts-client 已对齐）；冻结/密码列变更随 V0.9.0 破坏性批次，DBA 脚本 + 迁移指引（登记事实，步骤留开发方案）。

### 后续待办
- AuthCenter V0.9.0+ 实施：**重写** PasswordAuthenticationProvider + IPasswordLoginService + SetPasswordAsync/CompleteResetAsync 为 SecurePassword 协议（覆盖 T6 明文实现，PasswordCapabilityTests 重写）+ 冻结门面 + SecurityLog 直写 + 频控接线（**扩展开发组 1 已暂停待框架组 IRateLimitCheck 交付，R1 已提交（03fbcc30 v4.10.67）**；**转告扩展开发组 1 调整：T6 明文协议 → SecurePassword + 频控 T9 恢复 + 冻结列与 SecurityLog 事件 + TokenVersion 触发点收口 + 密码策略面**）；
- SecurityLog 侧：`SecurityLogEventTypes` 加 2 const（唯一必改点）+ README §四 边界声明同步修订；
- 补 AuthLoginAttempt `IsRateLimitedAsync` 的 `AuthTypes.Password` 分支 + 端点接线（审计面，与锁定解耦）；
- MFA 绑定/解绑 TokenVersion 联动（关联待办）；
- "按 UserId 批量撤销 access"（v0.2.0 候选）；
- **迭代数口径统一（用户裁定）**：`PasswordHasher.Iterations` 350000 → 600000 对齐 `DomainAuthOptions.Pbkdf2Iterations`——**一次性迁移，不兼容旧 hash**（旧 350000 存量密码经找回/重置流程作废重设，不做渐进兼容）；密钥强度策略取**保守方案**（默认高强度不可调低，维持 const 底线，文档声明为安全基线，不新增 CryptographyPolicyOptions）；RsaUtil 补 RSA≥2048 守卫——**三项均随本次转达框架组**。

## 七、关联文档

- ADR-AuthCenter-身份域数据模型与密码能力边界（B.9-B.11 归属 / C.15 频控 / A.2 白名单 / A.8 联邦归一化）
- ADR-RateLimiting-点检查原语与三层限流分工（IRateLimitCheck 契约 / RC1 / RC2 / 决策规则）
- `docs/AuthCenter/v0.9.0-身份域重构与密码能力-开发方案.md`（P0-1 方案 A 被本 ADR 决策 2 覆盖）
- ADR-AuthCenter-归层与命名 / ADR48 D7 / ADR90 / AGENTS §8 配置分层
- 主框架：`TKWF.Utility.Cryptography.PasswordHasher` / `Core.AuthController.*`（ICredentialProtector/IChallengeTokenService/IPasswordResetFlow/IAccountLockoutPolicy） / `DomainAuthOptions`
- SecurityLog README §一（与 AuditLogging 划界，权威边界声明所在）

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-07 | 起草（五组裁定：协议形态 / 限流接线 / 锁定冻结边界 / 策略面 / TokenVersion 触发点；覆盖 P0-1 方案 A；白名单准入论证待 Oracle 评审补充） |
| 2026-10-07 | Oracle1 评审 APPROVE WITH CONDITIONS 并入：B1（协议形态修正——覆盖开发方案 T6 明文实现 + 用户裁定统一 SecurePassword 零明文降级 + 测试重写登记）/ B2（LastPasswordChangedAt 移入 PasswordHistoryEntity + MustChangePassword 逐列准入）/ B3（SecurityLog README §四 边界声明同步 + Options.Enabled=false 跳过写入）+ M1-M6（TokenVersion 表述精确化 / P0-1 开发方案 §5.9 标记覆盖 / fail-closed 措辞 / 4 处 IsEnabled 位置 / Options 耦合记录 / sliding-fixed 缓解建议）——定稿 |
| 2026-10-07 | 用户裁定追加：PBKDF2 迭代数一次性迁移不兼容旧 hash；不支持"可调低"（保守方案：默认高强度 const 底线 + 文档安全基线声明）；RsaUtil ≥2048 守卫——三项随本次转达框架组 |

---
<!-- EOF -->
