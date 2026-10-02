# ADR-MFA-原子消费与数据访问红线例外

> **版本**：V0.1.0（目标）→ **superseded by ADR89（v4.10.52，2026-10-03）**
> **状态**：📋 提议（随 v0.1.0 实施，Oracle 复核 bg_d97a816a 定案）→ **✅ 已废弃（标注 superseded）**
> **关联**：`docs/MFA/MFA多因素认证-开发方案.md`（Oracle PASS WITH CONDITIONS，C1）、`ADR-MFA-挑战票据与验证模型.md`（单次消费）、`ADR-扩展数据访问红线.md`（红线），DataPort raw SQL 逃生口先例；**superseded by** 主框架 `ADR89-IEntityDAC条件原子更新原语.md`（v4.10.52）
> **关键字**：MFA、单次消费、原子 CAS、IFreeSql、红线逃生口、并发防重放、UpdateWhere
> **生命周期注记（ADR 纪律）**：本 ADR 为永久记录，不可删除；决策已被 ADR89 取代（`UpdateWhereAsync` 原语提供合规路径），正文保留原始决策供追溯。

---

## 目的与目标

裁定 MFA 挑战票据/恢复码**单次消费的原子性实现路径**（Oracle C1 条件：两并发验证同 challengeId/同恢复码 → 恰一成功一失败——防并发重放）。目标状态（3 句内）：**DataService 分部注入 `IFreeSql`**（数据访问红线逃生口，DataPort raw SQL 先例），以**单语句条件 UPDATE**（`WHERE IsConsumed=false` 守卫 + 影响行数判定）实现**引擎级原子消费**——不依赖事务包裹、不依赖隔离级别、不依赖扩展间依赖；**v0.2.0 待主框架 `IEntityDAC<T>.UpdateWhereAsync` 原语发布后替换**为本框架原语（本 ADR 标注 superseded）。

---

## 问题

### 问题现象

- `MarkConsumedAsync` 起初实现 = `EntityUpdateAsync(new XxxEntity { Id = id, IsConsumed = true })` → 基类 `InternalUpdateAsync` → `dac.UpdateAsync(entity)`——FreeSql **按主键不条件 UPDATE**（无 `WHERE IsConsumed=false` 守卫、无影响行数判定）。
- **生产多 scope（每请求独立 UoW/Orm）并发竞态**：两并发 `VerifyChallengeAsync` 同 challengeId → 各自读到未消费 → 各自验证码成功 → 各自无条件 UPDATE → **双成功**——C1「恰一成功一失败」被破坏（防重放失效）。

### 触发场景

- SMS/TOTP 挑战：用户（或攻击者持泄露码）并发提交同验证码——`IsConsumed` 翻转必须恰一。
- 恢复码：并发验证同码（8 码 × 无限尝试——频控缓解但不免疫并发窗口）。
- 绑定确认码：`ConfirmEnrollAsync` 的挑战消费同型。

### 现有方案不足

- **`IEntityDAC<T>` 契约无原子条件更新原语**（主框架 `IEntityDAC.cs` 实证）：`UpdateAsync` 按主键无条件（无返回值）、`UpdateColumnsBatchAsync` 是 SetSource+按主键批量（无额外谓词）——**无 WHERE 守卫原语、无败者检测**。
- **"Serializable + 重读断言 + 无条件 Update" 仍为 TOCTOU**：两并发均读到 `IsConsumed=false`，均按 PK UPDATE（均返回成功）——无败者检测；SQLite SERIALIZABLE 无行锁原语、无 SSI 中止。
- **`EntityGetAsync` 谓词守卫不解决**：读时 `!IsConsumed` 只是让"已消费"读不到——并发窗口内两读都通过。

---

## 使用场景

1. **挑战验证消费（SMS/TOTP，常态）**：`VerifyChallengeAsync` 验码通过后 → `MarkConsumedIfActiveAsync(id, now)` → 影响行数 1=成功 / 0=败（已被并发消费或过期）。
2. **恢复码消费（常态）**：`VerifyRecoveryCodeAsync` 命中码哈希后 → `MarkConsumedIfActiveAsync(id)` → 1=成功 / 0=败（并发竞态——统一返回 false 防枚举）。
3. **绑定确认消费（SMS）**：`ConfirmEnrollAsync` 确认码消费同理。
4. **不适用**：非并发消费路径（Disable 级联清理等）保留无条件 `MarkConsumedAsync`；非 MFA 扩展**不可**援引本例外（红线逃生口按 DataPort 判据逐案评估——业务特殊需要 + 框架不可能满足）。

---

## 决策

### 1. DataService 分部注入 `IFreeSql`（红线逃生口）

- **判据**（红线 AGENTS §8：「不允许直接使用 ORM——除非业务有特殊需要且框架不可能满足」）：①防重放 CAS 是安全正确性需要（非理论）；②`IEntityDAC` 无条件更新原语（契约实证）——**双条件成立**，对齐 DataPort raw SQL（UTC DateTime 语义）先例。
- **边界裁定**：DataService 分部**是**扩展代码，红线适用；但它是**逃生口的正确落点**——SG1 生成路径的标准扩展点（DataPort raw SQL 同理在 DataService 层）；ad-hoc Store/Service **不可**用此例外（应委托 DataService）。判据：所有数据访问（含例外）经 DataService 收敛。
- **构造兼容**：`.g.cs` 生成物**不声明主构造器**（仅 internal 转发方法）——手写分部加 `IFreeSql` 参数零冲突；`ActivatorUtilities.CreateInstance`（测试 `AddTestConstructibleDataService` / 生产 `AddConstructibleDataService`）自动解析额外参数。

### 2. 单语句条件 UPDATE（引擎级原子）

```csharp
// 挑战（含 TTL 谓词）：返回 1=成功 / 0=败
await _fsql.Update<MfaChallengeEntity>()
    .Set(x => x.IsConsumed, true)
    .Where(x => x.Id == id && !x.IsConsumed && x.ExpireAt > now)
    .ExecuteAffrowsAsync(ct);
// 恢复码（无 TTL 列）：.Where(x => x.Id == id && !x.IsConsumed)
```

- **原子性来源**：单语句 `UPDATE…WHERE` 的行锁 + WHERE 重求值是 DB 引擎级原子（T2 阻塞于 T1 行锁 → T1 提交后 WHERE 重求值失败 → 0 行 → 败者检测）——**不依赖事务包裹、不依赖隔离级别**（Noop 事务管理器测试宿主亦成立）。
- **调用点**：`VerifyChallengeAsync`（SMS/TOTP）`affected != 1 → MfaVerifyResult(false, "ChallengeAlreadyConsumed")`；`VerifyRecoveryCodeAsync` `affected != 1 → 统一 false`（防枚举 Oracle Q6）。

### 3. 保留无条件 `MarkConsumedAsync`（最小变更）

- 非并发路径（Disable 级联清理）无竞态——保留原方法，仅验证路径改调 `MarkConsumedIfActiveAsync`。

### 4. v0.2.0 演进：主框架 `IEntityDAC<T>.UpdateWhereAsync`

- 通用原子条件更新原语（返回影响行数）是**框架正确归宿**——Permissions/Tagging/AuditLogging 等扩展亦需；主框架发布后 MFA 替换 `IFreeSql` 直调，本 ADR 标注 superseded、降级为历史记录（ADR 生命周期：不可删除）。

> **否决"接受文档化限制"**（Oracle 裁决）：防重放是 `IsConsumed` 核心安全语义；MFA 威胁模型**包含码泄露**（SIM swap/钓鱼/中间人）——攻击者持码即可在窗口内并发提交；"理论窗口 + 频控缓解"是安全退化，非 C1 条件满足。
> **否决"Serializable + 重读断言"**：无 SELECT FOR UPDATE / 无 SSI 中止 / SQLite 无行锁——仍是 TOCTOU，无败者检测。
> **否决"唯一约束标记表"**：需新表+新 DAC+迁移——v0.1.0 过重，且异常驱动（UniqueConstraintException）反模式。

---

## 备选方案

### 接受文档化限制（理论窗口 + 缓解）
- **否决**：防重放是安全属性；持码攻击者可并发提交；C1 条件必须满足而非降级（Oracle bg_d97a816a 裁定）。

### Serializable + 重读断言 + 无条件 Update
- **否决**：无 SELECT FOR UPDATE 原语、无 SSI 中止、SQLite 无行锁——TOCTOU 不变，无败者检测（IEntityDAC.cs 实证无条件更新）。

### 唯一约束标记表（INSERT 幂等）
- **否决**：新实体 + 新 DAC + 表迁移（v0.1.0 过重）；异常驱动消费（反模式）。

### 等主框架 `UpdateWhereAsync` 原语（选项 D）
- **正确归宿但跨仓库发布周期**——v0.1.0 不可达；作为 v0.2.0 演进路径记录（见决策 4）。

---

## 后果

### 正面
- C1 原子消费真正达成（引擎级 WHERE 守卫 + 影响行数判定），防并发重放可靠。
- 单语句原子性不依赖事务/隔离级别——测试宿主（Noop 事务 + SQLite 文件模式）即可真实验证。
- 最小变更面（2 DataService 方法 + 4 调用点 + 测试双 scope 改造），非并发路径零改动。

### 负面
- `IFreeSql` 直注入是红线例外——须文档化（本 ADR）+ 测试/评审关注；`IEntityDAC` 契约缺口暴露（框架后续补原语）。

### 缓解
- 逃生口收敛于 DataService（所有数据访问经 DataService，例外最小化）；v0.2.0 主框架 `UpdateWhereAsync` 发布即替换（本 ADR superseded）。
- 已通知框架组增 `IEntityDAC<T>.UpdateWhereAsync` 通用原语（选项 D 转正）。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-10-01 | 📋 提议 | 初稿——Oracle 复核（bg_d97a816a）定案：C1 必须 v0.1.0 原子达成（C 否决）；路径 B（DataService 分部注入 IFreeSql + 单语句条件 UPDATE）为唯一可达合规解；.g.cs 构造兼容与 DI 解析实证；测试双 scope 改造；v0.2.0 UpdateWhereAsync 演进路径 |