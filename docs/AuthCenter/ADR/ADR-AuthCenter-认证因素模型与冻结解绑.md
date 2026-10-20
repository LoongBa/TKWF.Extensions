# ADR-AuthCenter-认证因素模型与冻结解绑

> **状态**：✅ 已定稿（2026-10-20，Oracle 评审 PASS WITH CONDITIONS，P0/P1 已并入）
> **日期**：2026-10-20
> **关联**：ADR-AuthCenter-身份域数据模型与密码能力边界（V0.9.0 凭据/档案分离）· ADR-AuthCenter-归层与命名 · ADR-Authentication-账号写契约 · ADR-扩展数据访问红线 · 开发方案 `docs/AuthCenter/v0.11.0-认证关系模型与冻结解绑-开发方案.md` · Iteration 2（渠道关注关系域，草稿）
> **性质**：外部依赖零变更；**扩展间协议变更**（`AuthCenter.Abstractions` V0.2.0）+ **关键设计取舍**（身份锚/凭据/订阅三分离 · 零投影 · 冻结多原因 · 解绑语义）

---

## 一、目的与目标

把 AuthCenter 的认证关系从「内联列 + 单槽冻结」改为**可组合的因素行集合 + 多原因冻结 + 可解绑关系**，使「一个账号能凭几种方式证明自己」「因何被冻结、能否部分解除」「何时绑、何时解、由谁解」三问在数据层直接可答，而不是靠散落的列与日志文本推断。

目标状态：

- `AuthAccount` **只承载身份与安全态**（`UId` / `IsEnabled` / `TokenVersion`）——不含任何凭据、不含任何派生状态；
- `AuthFactor` 承载一切**凭据**（其 `Identifier` 即身份锚：手机号 / 邮箱 / openid；**unionid/联盟锚点 = 特殊通道联邦因素**，D2b）；
- `AuthAccountFreeze` 承载**多原因冻结**，`IFreezeStateProvider` 提供生效判定；
- `AuthFactorEvent` 承载**关系变更历史**；
- 订阅/关注语义**完全不属于本域**。

## 二、问题

### 问题现象

1. **凭据无统一抽象**：主因素（`Phone`/`PasswordHash`）、联邦映射（`PlatformAccountMap`）、第二因素（MFA `MfaSecretEntity`）三套互不共享的存储与词表；全仓 `CredentialType|AuthFactorType|AuthMethodType` 零命中。新增一种认证方式 = 新增一列 + 改所有读取点。
2. **冻结单槽双向覆盖**：`IsFrozen` + `FreezeEnd` 仅一组；`FreezeAsync`/`UnfreezeAsync` 整实体读改写。关注失效、风控、欠费三类冻结互相覆盖——**恢复关注时把风控冻结一起解除**（安全隐患），且无法按原因查询或部分解除。
3. **绑定不可逆**：`PlatformAccountMapEntityDataService` 明确「无删除路径（T7 定稿）」+ 唯一索引 `(ChannelId, ExternalUserId)` + `LinkAsync` upsert → 用户无法解绑、无法换绑，旧 refresh token 继续有效（无 `TokenVersion++`）。
4. **关系变更无事实源**：SecurityLog 只覆盖 Freeze/Unfreeze；绑定/解绑无领域事件，无法重建历史、无法审计「谁在何时解绑了什么」。
5. **身份与凭据混居导致写者分裂**：`Phone` 同时是身份锚与短信凭据，写入点分散在 `SmsAuthenticationProvider`（首登建号）与 `FederatedLoginOrchestrator`（联邦建号）；`AuthLevel` 同样有 2 处写入点。任何「单写者」假设都会随新增登录路径失效。

### 触发场景

- 微信/QQ/支付宝等联邦账号需支持用户自助解绑、换绑；
- 业务层因「取关 / 欠费 / 违规 / 风控」分别冻结账号，需要独立解除其中一项而不影响其他项；
- 新增认证方式（邮箱验证码、Passkey、TOTP 收编）需在不改 `AuthAccount` 表结构的前提下接入；
- 安全审计需回答「某账号绑过哪些身份、何时解除、由谁操作」。

### 现有方案的不足

| 现有做法 | 不足 |
|---|---|
| `AuthAccount.Phone` 兼身份锚 + 短信凭据 | 加一种凭据加一列；写入点分散；无法表达「一个账号多手机号」 |
| `AuthAccount.PasswordHash` 内联 | 与「因素」概念同构却不同存储；MFA 无法复用 |
| `PlatformAccountMap` 混装认证映射与业务映射 | `LinkAsync` 把 channelId 塞进 `BusinessAppId`；认证侧无法独立演进 |
| `IsFrozen` + `FreezeEnd` 单槽 | 不可多原因、不可部分解除、解冻无条件清空 |
| 绑定 upsert 无删除 | 不可解绑/换绑；旧 token 不失效 |
| SecurityLog 承载关系历史 | 非领域事实源，无法重建；日志可被清理策略删除 |

## 三、使用场景

### 适用

| 场景 | 行为 |
|---|---|
| 用户自助解绑微信 | `UnbindAsync` → 删因素 + 事件 + `TokenVersion++`（同 UoW） |
| 业务层因取关冻结账号 | `FreezeAsync(uid, reason:"wechat_unfollow", scope:0)`；日后重新关注 `ReleaseFreezeAsync(reason:"wechat_unfollow")`，**风控冻结不受影响** |
| 用户改绑手机号 | 解绑旧 Sms 因素 + 绑定新 Sms 因素；`GetPrimaryPhoneAsync` 自动指向新因素 |
| 联邦首登建号 | 建 `AuthAccount` + `BindAsync(Federated)`；`GetAuthLevelAsync` 返回 2 |
| 新增 Passkey | 加 `FactorType=Passkey` 分支 + 绑定；`AuthAccount` 零改动 |
| 审计追溯 | 查 `AuthFactorEvent` 按 UId / FactorType / EventType |

### 不适用（边界）

| 场景 | 归属 |
|---|---|
| 渠道关注/取关状态与事件 | **Federation / Iteration 2**（AuthCenter 不感知「关注」概念） |
| 业务惩罚落地（停卡券、停权益） | 业务层（本域只提供 `FreezeAsync`） |
| 会话级 AAL（本次登录实际强度） | V0.12.0（本期保留账号级推导） |
| 邮箱升级为可登录因素 | 独立评估（需邮箱验证码 Provider） |
| MFA TOTP 收编 | 独立评估（MFA 自有 secret/恢复码生命周期） |

---

## 四、决策

### D1：身份 / 凭据 / 冻结 / 订阅 分层

- **身份** = `AuthAccount`（`UId` + 安全态）
- **凭据** = `AuthFactor`（`FactorType` + `Identifier` + `SecretHash`），其 `Identifier` 承载身份锚语义；**unionid / 联盟锚点 = 特殊通道的联邦因素**（`channelId` 命名空间表达），不是独立实体概念
- **冻结** = `AuthAccountFreeze`（多原因策略）
- **订阅** = 不在本域

> 判据：能独立回答「凭什么证明」的 → 凭据；只能回答「是否订阅某渠道」的 → 渠道域。**不设「链接键」独立概念**——曾设计 `AuthIdentityAnchor`（AnchorType=UnionId）表，2026-10-20 核实后废弃：`FederationAnchorOpenId` 生产调用点 0 处、`SsoAccountAnchor` 仅测试引用、`WeChatOauthChannel.cs:17` 证实「联盟锚点」本质 = 特殊服务号通道的联邦身份 → 锚表是空转概念，特殊通道照常走 `AuthFactor(Federated, channelId=<联盟服务号>)`（D2b）。

### D2：统一因素模型

`AuthFactor(FactorType, Identifier)` 全表唯一。`FactorType`：`1=Password / 2=Sms / 3=Email / 4=Federated`（预留 `5=Totp / 6=Passkey`）。
- `Identifier` 规范化在门面单点（Sms=E.164、Email=小写、Federated=`channelId|externalUserId`、Password=`UId`）
- 上限 **256**（覆盖 `channelId(64)+"|"+externalUserId(128)=193` + 余量）；超长**直接拒绝**（不做 hash 降级——避免查询路径分叉）
- `State` 仅 `Active / Unverified`；**解绑 = 物理删**（唯一索引释放 → 可重绑），不设 `Revoked` 态

### D3：关系历史独立事实源

`AuthFactorEvent`（`Bind / Unbind / Verify / Reset` + `Source` + 操作者 + `OccurredAt/ReceivedAt`）为领域事实源，SecurityLog 为安全审计副本。
- `DedupeKey` **可空**，由调用方提供幂等键（如业务事件 id）；未提供则不入去重语义——避免时间戳精度碰撞误拒合法重绑

### D4：冻结多原因 + 派生判定，**零投影**

- `AuthAccountFreeze`：一原因一行，`Reason` 机器可读、`Scope` 预留、`FreezeEnd` 可空（永久）、`ReleasedAt/ReleasedBy` 留痕
- **删除 `IsFrozen` / `FreezeEnd` / `IsFrozenEffective`**：真值只在冻结行，`IFreezeStateProvider`（Scoped，帧内缓存）负责生效判定
- `UnfreezeAsync` **删除**，改为 `ReleaseFreezeAsync(uid, reason)`（按原因）+ `ReleaseAllFreezesAsync(uid)`（显式全解）；`FreezeAsync` 旧签名删除（只留 `FreezeAsync(uid, reason, ...)`）
- 理由：投影列需在各写路径同步；不投影则「恢复关注误解除风控」在结构上不可能发生

### D5：解绑语义

1. 归属校验
2. 最后因素守卫 → `LAST_FACTOR_CANNOT_UNBIND`
3. 追加 `AuthFactorEvent(Unbind)`
4. 物理删除因素行
5. `TokenVersion++`（CAS）
6. SecurityLog `Unbind`

- 3~5 在**同一 UoW**（`ITransactionManager.Begin()`，`Propagation.Required`；`FreeSqlEntityDAC` 经 `uowManager.Current?.GetOrBeginTransaction()` 绑定）
- **无投影需重选**（D4 的直接收益）
- 唯一冲突：`FACTOR_ALREADY_BOUND`（复用多方言判定）；同 UId 同 Identifier 并发 → 幂等

### D6：映射契约合并为单一消费者意图契约（2026-10-20 用户确认）

- `PlatformAccountMapEntity` **删 `ChannelId` / `ExternalUserId` / `UnionId`** → 只保留业务映射（`PlatformAccountId` / `BusinessAppId` / `BusinessLocalId`）
- 认证映射迁 `AuthFactor(Federated)`；「锚点/unionid」作为特殊/虚拟通道因素（D2b），不建独立表
- `ISsoChannelMapService` + `ISsoAccountLinkService` **合并** → **`IExternalIdentityService`**（`LinkAsync` / `GetByExternalIdAsync` / `GetByAccountAsync` / `UnlinkAsync`）；`SsoChannelMapDto` → `ExternalIdentityDto(UId, ChannelId, ExternalUserId, LinkedAt)`；`SetFederationAnchorAsync`/`GetByFederationAnchorAsync` **删除**（D2b 锚点消除）
- **不选「退役后让 Federation 直接用 `IAuthFactorService`」**：Oracle 建议退役（少一层间接、少一个契约），本 ADR **经用户确认偏离**——若 Federation 直接消费 `IAuthFactorService`，则 `FactorType` 枚举与 `{channelId}|{externalUserId}` 编码规则外泄到消费方（编码一旦调整即静默破坏跨包调用），且该服务是**凭据**服务（签名含 `secretHash`/`MustChange`），整体暴露违背最小面；本门面承载的正是**标识编码**这一职责，是接缝而非冗余层。已送 oracle6 复审（2026-10-20）。

### D7：建号策略在领域配置，不破 F7

`AuthCenterOptions.FederatedProvisioning.AutoProvision`（默认 `true`；`false` → `ACCOUNT_NOT_PROVISIONED` fail-closed）。
建号路径：建 `AuthAccount`（**不写 Phone/AuthLevel**）→ `BindAsync(Federated)`；事件侧保持只读（F7 不破）。配置分层：领域安全决策在 `TKWF:AuthCenter`，Web 层无对应开关。

### D8：表 / 索引命名

- 表：`TKWF_AuthFactor` / `TKWF_AuthFactorEvent` / `TKWF_AuthAccountFreeze`（**不建锚表**，D2b）
- 索引：`TKWFIX_AuthFactor_Type_Identifier`（UNIQUE）、`TKWFIX_AuthFactor_UId`、`TKWFIX_AuthAccountFreeze_UId`
- 删除：`TKWFIX_AuthAccount_Phone`、`TKWFIX_AuthAccount_FederationAnchorOpenId`、`TKWFIX_PlatformAccountMap_Channel`

### D9：禁止「派生状态投影列」原则

**凡可由其他表推导的状态，不在主表设列。**（`Phone` / `AuthLevel` / `IsFrozen` / `FederationAnchorOpenId` 一律删）

- 理由一：投影需要「唯一写者」不变量，而写路径会随新登录方式增加（`Phone` 已有 2 个写点，`AuthLevel` 已有 2 个）；
- 理由二：解绑/改绑时投影需重选，重选逻辑与业务语义耦合，易遗漏；
- 理由三：陈旧投影是静默缺陷（读到的值不报错但错），派生查询是显式成本（可从缓存缓解）。

> 权衡记录：派生需查询（认证热路径），故配 `IFreezeStateProvider` 帧内缓存；`GetPrimaryPhoneAsync` / `GetAuthLevelAsync` 在单次请求内最多各一次，可接受。若未来出现高频只读面，应加**显式缓存层**（带失效协议），而非回退到主表投影列。

---

## 五、备选方案与否决理由

| 备选 | 否决理由 |
|---|---|
| `Phone` / `AuthLevel` / `FederationAnchorOpenId` 保留为主表投影列 | 写点分散（`Phone`×2 / `AuthLevel`×2；锚列生产**零调用**——D2b 核实后证伪其必要性），「唯一写者」不可维护；解绑需重选投影；长期易漂移（Oracle P0 #1/#2/#4） |
| 冻结状态存 JSON 列 | 不可按原因部分解除、不可按原因索引查询 |
| 解绑用软删 + 部分唯一索引 | FreeSql 跨库不支持部分索引；且 `hasSoftDelete:false` 定稿需推翻 |
| 事件 handler 建号 | 事件可重放（F7 风险） |
| `ISsoChannelMapService` 原样保留 | 命名承载 `PlatformAccountMap` 遗留语义，与 D6 合并冲突 |
| Federation 直接用 `IAuthFactorService` | 跨域领域泄漏（暴露 `FactorType`/标识编码/凭据签名） |
| `Identifier` 用 hash 降级存储 | 查询路径分叉（精确 vs 前缀），重绑语义复杂 |
| `DedupeKey` 由 `OccurredAt` 生成 | 时间精度碰撞会误拒合法重绑 |
| `AuthLevel` 改为会话级 AAL（本期） | 需改 `TokenIssueRequest` 契约与全部 Provider；本轮聚焦关系模型，留 V0.12.0 |

---

## 六、后果

### 正面

- 新增认证方式零 `AuthAccount` 变更（D2）
- 冻结可多原因、可部分解除、可按原因查询（D4）
- 绑定关系可逆、可审计、解绑即失效旧 token（D3 / D5）
- 认证映射与业务映射彻底分离（D6）
- 删除全部派生投影 → 消除「多路径同步」类静默缺陷（D9）

### 负面 / 成本（已接受）

- **改造面大**：`Phone` 读取约 23 处 + 写入 2 处、`AuthLevel` 读 1 写 2、`IsFrozenEffective` 9 处、`PasswordHash` 5 处；契约重命名波及 Federation（4 处 + `Federation.csproj`）
- 认证热路径多一次派生查询 → 配帧内缓存
- 破坏性契约变更（`AuthCenter.Abstractions` 0.1.x → 0.2.0），触发 `__ALL__` 全量回归
- 解法：分层验证（L0 编译门 → L1 受影响面 → L2 全量）+ `grep` 零残留验收

### 中性

- `AuthLevel` 仍为账号级语义（与 V0.9.0 行为一致），会话级 AAL 另立项
- `Metadata` JSON 列不参与查询（仅承载平台附加属性）

---

## 七、实施约束

- 门面 `AuthFactorService` / `FreezeService` 继承 `DomainServiceBase`，经 `AddConstructibleService` 注册，消费侧 `User.Use<T>()`
- 禁手写 Store；数据访问仅经 SG1/xCodeGen 生成的 DataService；禁裸 `IFreeSql` / `IEntityDAC`
- 跨表原子用 `ITransactionManager`（非裸事务）
- `.g.cs` 重生成入库（`run-xcodegen.ps1 -Ext AuthCenter`）
- 测试走生产路径（真实 DI + `User.Use<T>()`），不手写测试宿主绕过门面

---

## 八、状态与后续

| 项 | 状态 |
|---|---|
| 本 ADR（D1-D9） | ✅ 定稿，随 AuthCenter V0.11.0 实施；2026-10-20 关键调整（D1 去链接键层 / D6 契约合并 / D2b 锚点消除）已送 oracle6 复审 |
| `AuthLevel` 会话级 AAL | 📋 V0.12.0 评估 |
| MFA TOTP 收编 `FactorType=Totp` | 📋 独立评估 |

---

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-20 | 草案（Oracle or4 评审 PASS WITH CONDITIONS 后重写：P0×4/P1×6 全采纳；删全部投影列；多原因冻结；解绑六步） |
| 2026-10-20 | **关键调整（用户确认 + 事实核实）**：D6 `IExternalIdentityLinkService` 重命名 → **`ISsoChannelMapService`+`ISsoAccountLinkService` 合并为 `IExternalIdentityService`**（凭据签名不外泄；编码规则留在接缝）；D1 去「链接键」层 + **D2b 锚点消除**（`FederationAnchorOpenId`/`SsoAccountAnchor`/`ISsoAccountLinkService` 生产零调用；联盟锚点 = 特殊服务号通道的联邦因素 → 不建锚表，unionid = 虚拟通道因素）；送 oracle6 复审（进行中） |
| `UserProfile.Email` 升级为凭据 | 📋 独立评估 |
| 渠道关注关系域（Follow） | 📋 Iteration 2 草稿（`docs/Federation/I2-渠道关注关系域-开发方案.md`，未评审） |

> 若后续推翻任一决策，**不删除本 ADR**，在对应决策下标注「已废弃」并引用新 ADR。
