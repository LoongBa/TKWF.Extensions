# 转达 · MFA 原子条件更新能力缺口——请框架组裁定

> **转达方**：TKWF 扩展模块组（`_TKWF.Extensions`）
> **接收方**：TKW.Framework 框架组
> **日期**：2026-10-03
> **关联**：v4.10.51 ADR88 构造注入门控（B 红线：DataService 构造注入 `IFreeSql` 需移除）· `ADR-MFA-原子消费与数据访问红线例外.md` · `IEntityDAC.cs`
> **性质**：请裁定 @return——给 `IEntityDAC` 增加原子条件更新能力，还是其它方式

---

## 一、要裁定的问题（一句话）

v4.10.51 ADR88 要求 B 批（MFA 两个 DataService）**移除 `IFreeSql` 直注入，改走 `IEntityDAC<T>` 或 DataService 方法**；但 MFA 当初用 `IFreeSql` **正因 `IEntityDAC` 无能力表达该操作**——请框架组裁定：**给 `IEntityDAC` 增加条件原子更新原语，还是另设合规路径**，以便 B 批在不违背单次消费安全语义（防重放）的前提下完成整改。

---

## 二、为什么 MFA 用了 `IFreeSql`（不得已）

MFA `VerifyChallengeAsync`/`VerifyRecoveryCodeAsync` 需要**原子单次消费**：两并发提交同一 challengeId / 同一恢复码 → 必须**恰一成功一失败**（防重放，ADR C1 安全属性）。

- 现 IEntity T 只能"读→改→按主键 `Update`"（check-then-act）→ 并发窗口内两请求均读到未消费 → 双成功 → **防重放失效**。
- MFA ADR（`docs/MFA/ADR/ADR-MFA-原子消费与数据访问红线例外.md`，Oracle 复核定案）论证：唯一可靠解是引擎级单语句 `UPDATE … WHERE IsConsumed=false`（行锁 + WHERE 重求值天然原子，不依赖事务/隔离级别）。
- 该能力 `IEntityDAC` 目前**没有** → 只能经 `IFreeSql` 直连 ORM 逃生（ADR71 `[DiContractIgnore]` 豁免）。

## 三、`IEntityDAC<T>` 契约现状（铁证，`_TKWF/_Framework/Domain/Interfaces/IEntityDAC.cs`）

| 方法 | 语义 |
|---|---|
| `InsertAsync` / `InsertBatchAsync` / `DeleteAsync` | 按实体/主键 |
| `UpdateAsync` / `UpdateBatchAsync` | 按主键无条件，**无影响行数返回** |
| `UpdateColumnsBatchAsync<TColumns>` | 按实体集合主键批量 + 指定列，**无额外谓词** |

**无"按 `WHERE` 条件更新 + 返回影响行数"原语**——无法表达 `Update().Set(IsConsumed=true).Where(Id==x && !IsConsumed).ExecuteAffrows()` 的单语句原子消费。

## 四、结论：有能力解决，IFreeSql 非唯一答案

**结论**：`IEntityDAC` 具备补足能力，标准做法是**新增条件原子更新原语**（接口 + FreeSql/EF 双桥接），而非依赖 `IFreeSql` 直注入。

能力佐证：
- 接口已有"多实体 + 指定列"批量更新 `UpdateColumnsBatchAsync<TColumns>`——方向支持的先例。
- 桥接实现层（`FreeSqlEntityDAC.cs` L198-220）已在 `Orm.Update<TEntity>().SetSource(...).ExecuteAffrowsAsync(ct)` 承载条件/批量更新——**能力在实现层已存在，仅接口未暴露**。

## 五、建议方案（给框架组）

1. **A（首选）**：`IEntityDAC<T>` 增 `UpdateWhereAsync(Expression<Func<T,bool>> where, Expression<Func<T,T>> update) → Task<bool/int>`（条件更新 + 影响行数判定）。通用能力，Permissions/Tagging/AuditLogging 亦有潜在需求；MFA 挑战/恢复码即时替换，逃生口撤销。
2. **B（等价，落点更贴 DataService）**：在 `DomainDataServiceBase` 上加同一方法（下层调桥接实现），MFA 改调 DataService 方法，不注入 `IFreeSql`。
3. **C（兜底）**：若框架本迭代不及，保留 `IFreeSql` 逃生口 + `[DiContractIgnore]` 豁免，待 A/B 落地后迁移。

## 五、请求

请框架组确认 A 或 B，并告知预计版本；MFA 侧按裁定无损跟进。深入核实（桥接层到接口层全链路）由框架组自查。

---
<!-- EOF -->