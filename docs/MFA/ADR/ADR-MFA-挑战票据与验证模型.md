# ADR-MFA-挑战票据与验证模型

> **版本**：V0.1.0（目标）
> **状态**：📋 提议（随 v0.1.0 开发方案，Oracle 评审 bg_025c18fc 定案）
> **关联**：`docs/MFA/MFA多因素认证-开发方案.md`（Oracle PASS WITH CONDITIONS，C3/C4）、`OAuthTicketEntity` 一次性票据先例、`ADR-扩展数据访问红线.md`
> **关键字**：MFA、挑战票据、MfaChallengeEntity、单次消费、TTL、无状态验证、装饰器管道

---

## 目的与目标

裁定 MFA 挑战-验证流程的**状态模型**：`RequestChallengeAsync` → `VerifyChallengeAsync` 之间，挑战状态如何承载（持久票据 vs 无状态 vs 装饰器管道）。目标状态（3 句内）：采用**挑战票据实体**（`MfaChallengeEntity`——一次性、TTL 5min、`IsConsumed` 原子翻转单次消费）；SMS 码 SHA256 落库于票据行（CodeHash 可空——TOTP 无码，票据仅作流程句柄 + 频控挂点）；**不设 `Attempts` 列**——尝试频控归内存滑动窗口（per-user×method），DB 列冗余且 TOTP 无状态挑战语义不清。

---

## 问题

### 问题现象

- MFA 验证码（SMS/TOTP）需要"发码 → 待验证 → 消费"的状态承载：码放哪里、过期如何处理、重复使用如何防、尝试次数如何限。
- 候选模型三个，各有取舍：**持久票据**（DB 落码 + 一次性消费）、**无状态验证**（TOTP 直接按 secret + 时间窗验，无中间状态）、**装饰器管道**（认证链上插 MFA 步骤）。
- TOTP 本质无状态（HMAC + 时间窗即可验），SMS 本质有状态（码需落库比对）——两方法若走不同模型，`IMfaMethod` 契约不统一，消费方编排被迫分叉。

### 触发场景

- SMS 二次验证：发码后用户可能等待、重发、过期、重复提交——码的状态必须明确。
- TOTP 二次验证：用户输入验证器码，可能错码多次、超时——无中间状态但需**频控挂点**与统一流。
- 并发提交：同 challengeId 两并发验证——单次消费原子性必须保证（防重放）。

### 现有方案不足

- **无状态验证（TOTP 纯）**：无频控挂点（跨请求无法按挑战计数）、无统一流程句柄（SMS 无法复用同一 `IMfaMethod` 契约）、无审计轨迹（挑战发起/验证时序）。
- **装饰器管道**：需认证链改造（主框架无 MFA 调用点——见 ADR-独立扩展与零依赖边界），侵入式强，零破坏目标不满足。
- **SMS 独立实体**（码表独立于挑战）：码生命周期 = 挑战生命周期（TTL + 单次消费 + attempts）——独立实体是无谓拆分（对齐 `SmsRecordEntity` 码+票据一体的先例）。

---

## 使用场景

1. **SMS 挑战**（常态）：`RequestChallengeAsync` 生成 6 位码 → SHA256 落 `MfaChallengeEntity.CodeHash` → 经 `IMfaSmsSender` 发送 → 用户输码 → `VerifyChallengeAsync` 比对（恒定时间）+ `IsConsumed` 翻转 → 后续同码失败（单次消费）。
2. **TOTP 挑战**：`RequestChallengeAsync` 建票据行（CodeHash null——无码落库）→ 用户输验证器码 → `VerifyChallengeAsync` 按 secret + RFC 6238 时窗验证（无状态语义）+ 票据行消费标记（流程句柄 + 审计）。
3. **并发防重放**：两并发 `VerifyChallengeAsync` 同 challengeId → 恰一成功（`IsConsumed` 原子翻转 + UX 约束兜底）。
4. **不适用**：挑战票据不承载授权（验证通过 ≠ 已登录——令牌签发归消费方）；票据不跨方法复用（每挑战单方法单码）。

---

## 决策

### 1. 挑战票据实体模型（Oracle 定案）

`MfaChallengeEntity`：`UserId + Method + CodeHash（SMS 可空）+ ExpireAt + IsConsumed`（`CanUpdate=false`，对齐 `OAuthTicketEntity` 一次性票据先例）。

- **TTL 5min**（`ChallengeTtlSeconds=300`，对齐 OAuthTicket/SmsRecord 同窗）。
- **单次消费**：`IsConsumed` 原子翻转（`ConsumeChallengeAsync` → `IsConsumed=true`），成功消费后同码/同票据拒绝。
- **SMS 码 SHA256 落库**（明文不落库）+ 恒定时间比较。
- **TOTP 无码落库**（CodeHash null）——票据仅作流程句柄 + 频控挂点 + 审计（Oracle Q5 裁决：统一流收益 > 免票据的微小省写）。

### 2. 不设 Attempts 列（Oracle C4）

- 尝试频控归**内存滑动窗口**（per-(UserId, Method)，5 次/5min）——跨挑战、跨票据计数（同一窗口覆盖该用户该方法的全部验证尝试）。
- DB `Attempts` 列**冗余**（内存窗口已覆盖）+ TOTP 无状态挑战不落 Attempts 语义不清——删。

### 3. 统一 `IMfaMethod` 契约（TOTP/SMS 同签名）

- `RequestChallengeAsync` → `MfaChallengeRequest?`（TOTP 建句柄票据；SMS 建码票据 + 发码）。
- `VerifyChallengeAsync(user, challengeId, code)` → `MfaVerifyResult`（TOTP 无状态验码 + 消费票据；SMS 比对 + 消费）。

> **否决"无状态验证"**（Option ②）：TOTP 虽可无状态验，但失去频控挂点/审计/统一契约——消费方编排须分叉，两方法无法同一 `IMfaMethod` 接口。
> **否决"装饰器管道"**（Option ③）：需认证链改造（主框架无调用点），破坏零依赖目标（见 ADR-独立扩展与零依赖边界）。

---

## 备选方案

### 无状态验证（TOTP 纯，SMS 独立处理）
- TOTP 直接 secret + 时窗验（无票据行）；SMS 独立码表。
- **否决**：契约分叉（消费方编排分叉）；TOTP 无频控挂点；无审计轨迹；两方法无法统一 `IMfaMethod`。

### 装饰器/认证链管道
- 在认证链插入 MFA 步骤（`IMfaChallengeStep`）。
- **否决**：主框架/认证扩展无 MFA 调用点（勘察实证）；侵入式强；零破坏目标不满足；与"消费方编排"哲学冲突（见 ADR-独立扩展与零依赖边界）。

### SMS 码独立实体（MfaSmsCodeEntity）
- 码表独立于挑战票据。
- **否决**：码生命周期 = 挑战生命周期（TTL + 单次消费 + 频控）——合并天然；独立 = 无谓拆分（对齐 `SmsRecordEntity` 码+票据一体先例）。

---

## 后果

### 正面
- 单次消费原子性由 DB 层保证（IsConsumed + UX），防重放可靠。
- 统一 `IMfaMethod` 契约（TOTP/SMS 同签名）——消费方编排无分叉。
- 挑战审计轨迹完整（发起/验证/消费时序落库）。

### 负面
- TOTP 每次挑战建票据行（微小写放大——挑战低频，可接受）。
- 票据过期行滞留（低频，v0.2.0 可加清理任务）。

### 缓解
- TOTP 票据行 CodeHash null 不引安全面（无明文/无码落库）。
- 过期清理列 v0.2.0 候选（对齐 SmsRecord 清理先例）。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-10-01 | 📋 提议 | 初稿——随 v0.1.0 开发方案 Oracle 评审（bg_025c18fc）定案：票据实体模型 + 不设 Attempts 列（C4）+ TOTP 无码票据（Q5 裁决） |
| 2026-10-01 | 🔧 实现修订 | 单次消费**原子化实现路径**定案（Oracle 复核 bg_d97a816a）——`IsConsumed` 原子翻转经 DataService 分部注入 `IFreeSql` 单语句条件 UPDATE（`WHERE IsConsumed=false` + 影响行数判定，引擎级原子，红线逃生口）；详见 [ADR-MFA-原子消费与数据访问红线例外](./ADR-MFA-原子消费与数据访问红线例外.md)；v0.2.0 待主框架 `UpdateWhereAsync` 原语替换 |
