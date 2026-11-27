# ADR-AuditLogging-业务审计扩展点与读面暴露

> **版本**：v0.1
> **状态**：提议（待 Oracle 评审）
> **关联**：`docs/AuditLogging/v0.7.0-审计契约收口与统一读模型-开发方案.md` §四/§五/§六/§七；v0.6.0 业务审计方案；《设计原则手册》P2/P3/P5/P7/P14/P15；AGENTS §8（内容列 `[MaxLength(-1)]` 红线）
> **日期**：2026-11-27

## 目的与目标

让"业务审计"既**可扩展**（业务自定义维度 `Attributes`，不靠改表）又**可控暴露**（详情 DTO 含 Before/After/Attributes 仅在授权时返回），并把权限/数据域治理、Permissions 集成与特权删除收敛为**默认安全**的机制。读者 3 句话内明白：业务字段走 `Attributes` JSON 扩展点（脱敏 + `[MaxLength(-1)]` + 非主查询维度）；详情受 `IAuditAccessPolicy`（含 Permissions 组合）门控；特权删除是"默认不注册"的能力，而非开关。

## 问题

### 问题现象

1. 不同业务对象审计维度各异（AppId/ChannelId/DeviceId/RiskScore/工单号…），通用字段覆盖不全；每来一个字段就改表 → 频繁迁移、破坏 P2。
2. 业务审计详情含 `Before/After`（可能 PII），若直接暴露端点则越权/泄露风险。
3. `IAuditAccessPolicy` 在 v0.6 未接 Permissions，Permissions 启用后仍"一刀切拒绝"或依赖旁路开关。
4. `AuditImmutabilityOptions` 已声明但无特权删除实现；直接加 `AllowDelete` 开关属 P3 反模式。

### 触发场景

- 业务方需要审计中携带自有维度并能查询/导出（v0.8）。
- 合规/管理端查看业务审计详情（Before/After）。
- 平台启用 Permissions 后按权限键治理审计可见性。
- 罕见特权的审计记录删除（法务/纠错），需强校验 + 留痕。

### 现有方案的不足

- **每需求加列**：迁移成本、破坏单一事实源。
- **详情直接透传**：PII 越权。
- **策略写死单一来源**：Permissions 启用前/后行为割裂。
- **`AllowDelete` 开关**：运行期能力矩阵（P3 反模式）。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 业务自定义维度 | `BusinessAuditEntry.Attributes`（脱敏后落 `AttributesJson`） |
| 详情查看 | `BusinessAuditDetailDto`（含 Before/After/Attributes），仅 `Detail` 放行返回 |
| 列表 | 裁剪 DTO（不含 Before/After/Attributes 全文） |
| 权限治理 | `IAuditAccessPolicy` + `AuditPermissionNames`；Permissions 可用则按权限键，否则 `AllowWithoutPermissions`（默认拒绝） |
| 数据域 | `QueryOwn` 绑定调用方租户（空租户拒绝）；`QueryAll` 需权限 |
| 特权删除 | `IAuditPrivilegedDeleteService`（默认不注册）+ `Delete` 强校验 + 写 `BusinessAudit` 留痕 |

**不适用边界**：`Attributes` 不作为主查询维度（JSON 列检索弱，强检索需提升为类型化列）；不做跨租户实现层越权；不提供 `AllowDelete` 运行期开关；不把业务字段塞进调用审计/安全事件表。

## 决策

1. **`Attributes` 扩展点**：`BusinessAuditEntry.Attributes`（`IReadOnlyDictionary<string,object?>?`）→ `BusinessAuditEntity.AttributesJson`（**显式 `[MaxLength(-1)]`**，同 `IAuditMaskingPolicy` 脱敏）；详情透出、列表裁剪；**非主查询维度**（升级路径：需要强检索时提升为类型化列）。
2. **受控详情 DTO**：`BusinessAuditDetailDto` 含 Before/After/Attributes，仅 `Detail` 操作经 `IAuditAccessPolicy` 放行时返回；列表 DTO 不含全文。
3. **策略组合 + Permissions 集成**：`DefaultAuditAccessPolicy` 可选依赖 `IPermissionChecker`（Permissions.Abstractions）；`QueryOwn` 已认证 + 调用方租户绑定（空租户拒绝）；敏感操作：Permissions 可用→按 `AuditPermissionNames.*`；否则 `AllowWithoutPermissions`（默认 false）。
4. **特权删除=能力=注册**：`IAuditPrivilegedDeleteService` **默认不注册**；启用时 `Delete` 强校验 + 删除前写 `BusinessAuditEntry`（Action=`Deleted`，Target=目标，Reason）留痕；不使用 `AllowDelete` 开关。
5. **Actor 自动派生**：`IAuditActorProvider` 在 `Actor` 为空时派生，仍空则抛（fail-closed）；默认 provider 返回 null。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 每需求加业务列 | 频繁迁移；破坏 P2 单一事实源 |
| B. 消费方自建 Store/DataService 承载业务字段 | 违扩展红线（禁 Store 伪层）；口径漂移 |
| C. 详情直接透传（不加策略） | PII 越权（P7/P14） |
| D. `AuditImmutabilityOptions.AllowDelete` 开关 | P3 反模式（运行期能力矩阵） |
| E. Permissions 强依赖（无回退） | 未启用 Permissions 的消费方不可用；应可选组合 |

## 后果

- **正面**：业务可扩展、可治理、默认安全；无需频繁改表；详情与权限解耦清晰。
- **负面/缓解**：`Attributes` 检索弱（文档给"提升为类型化列"升级路径）；特权删除默认不可用（有意，合规优先）；Permissions 组合需消费方按需替换策略。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿——Attributes 扩展点 + 受控详情 DTO + 策略组合/Permissions + 特权删除能力=注册；待 Oracle 评审 |
