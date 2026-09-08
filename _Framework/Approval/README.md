# TKWF.Ext.Approval 轻量审批引擎扩展技术规范

**状态**: P1 差异化模块 (Differentiation Module) | **版本**: V0.1.0 | **框架**: .NET 10

**核心约束**: 三实体模型（流程定义/审批实例/审批任务）+ 内置自建状态机（Draft→Pending→Approved/Rejected/Withdrawn）+ 审批人解析抽象（User 内置 + Role 消费方）+ 完成事件回调（ILocalEventBus post-commit）+ 顺序步骤链 + 或签/会签；不依赖外部工作流引擎（Elsa/WorkflowCore）。

---

## 一、需求分析

企业应用普遍需要审批流（报销/请假/采购/发布），但主框架无任何工作流/状态机基座。ABP 商业模块才提供，Orchard 用外部 Elsa——重且与 TKWF 领域自治冲突。

- **定位**：P1 差异化模块（对标清单 §四）——轻量审批引擎，不依赖外部工作流引擎
- **场景**：DMP-Lite 报销审批（部门经理→财务→总经理，或签）、请假审批（会签）、内容发布审批（通过后触发上线事件）
- **约束**：顺序步骤链 + 或签/会签；复杂流程（并行分支/条件网关）v0.2.0 候选

---

## 二、三实体说明

| 实体 | 表名 | 职责 |
|------|------|------|
| `ApprovalFlowEntity` | `ApprovalFlow` | 审批流定义——Code 唯一 + 步骤链 JSON + 启用/禁用 |
| `ApprovalInstanceEntity` | `ApprovalInstance` | 审批实例——业务弱关联（BusinessType+BusinessId）+ 状态机 + 唯一约束防重复 |
| `ApprovalTaskEntity` | `ApprovalTask` | 审批任务——步骤级待办（审批/驳回/转交载体） |

### 唯一约束

- **UX_ApprovalFlow_Code**：`Code` 唯一（审批流编码不可重复）
- **UX_ApprovalInstance_Active**：`BusinessType+BusinessId+IsActive` 唯一（活动实例同业务唯一防重复；终态 IsActive=false 释放约束允许重新提交）

---

## 三、状态机

### 实例状态

```
Draft ──Submit──▶ Pending ──全步骤通过──▶ Approved（终态）
  │                  │
  └──Withdraw──▶ Withdrawn（终态）  └──任一步驳回──▶ Rejected（终态）
```

### 任务状态

```
Pending ──Approve──▶ Approved（本任务通过）
   │       └──Reject──▶ Rejected（实例 Rejected）
   │       └──Transfer──▶ Transferred（新任务建给 toUserId）
   └──会签同步骤：步骤判定 All 通过后，其余 Pending 任务→Completed
```

### 关键约束

- **C1 身份硬校验**：ApproveAsync/RejectAsync/TransferAsync 校验 `approverUserId == task.ApproverUserId`（任何人不可代审）
- **C2 IsActive 约束**：终态（Approved/Rejected/Withdrawn）时 `IsActive=false` 释放唯一约束
- **C3 事件派发**：ITransactionManager CommitAsync 成功后 ILocalEventBus.PublishAsync（事务外）
- **C4 转交边界**：仅任务审批人可转交；转交次数不限

---

## 四、安装接线

### 1. 白名单启用（消费方 DomainInitializer）

```csharp
using TKWF.Ext.Approval;

[TKWFEnabledExtension(typeof(ApprovalExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

### 2. 流程定义（一次性配置）

```csharp
var approval = serviceProvider.GetRequiredService<IApprovalService>();

await approval.CreateFlowAsync("expense", "报销审批",
[
    new(0, "部门经理", ApprovalApproverType.User, "u_manager", ApprovalMode.Any),
    new(1, "财务",     ApprovalApproverType.User, "u_finance", ApprovalMode.Any)
]);
```

### 3. 业务提交审批

```csharp
var instanceId = await approval.StartAsync("Expense", "exp-001", "expense", currentUser);
await approval.SubmitAsync(instanceId);
```

### 4. 审批

```csharp
await approval.ApproveAsync(taskId, currentUser, "同意");
```

### 5. 完成事件 handler（业务解耦）

```csharp
[DomainEventHandler]
public class ExpenseApprovedHandler(IMoneyTransferService money) : ILocalEventHandler<ApprovalCompletedEvent>
{
    public async Task HandleEventAsync(ApprovalCompletedEvent e, CancellationToken ct)
    {
        if (e.BusinessType == "Expense" && e.Result == ApprovalInstanceStatus.Approved)
            await money.PayAsync(e.BusinessId, ct);
    }
}
```

---

## 五、审批人 Resolver

### 内置 DefaultApprovalAssigneeResolver

- **User 类型**：直接返回 `[ApproverValue]`（用户 ID）
- **Role 类型**：抛 `NotSupportedException`（提示消费方注册自定义 resolver）

### 消费方自定义（Role→用户解析）

```csharp
// 注册（Initializer 中 TryAddEnumerable 覆盖默认）
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IApprovalAssigneeResolver, MyRoleResolver>());

// 实现（经 Identity UserRoleEntity 反查）
public class MyRoleResolver(IEntityDAC<UserRoleEntity> dac) : IApprovalAssigneeResolver
{
    public async Task<IReadOnlyList<string>> ResolveUserIdsAsync(ApprovalStepDefinition step, CancellationToken ct)
    {
        if (step.ApproverType != ApprovalApproverType.Role)
            throw new NotSupportedException("仅支持 Role 类型");

        // 查 Identity UserRoleEntity 反查角色成员
        var userRoles = await dac.SelectAsync(ur => ur.RoleName == step.ApproverValue, ct);
        return userRoles.Select(ur => ur.UserId.ToString()).ToList();
    }
}
```

---

## 六、边界（V0.1.0）

### 包含

- ✅ 顺序步骤链 + 或签（Any）/会签（All）
- ✅ 审批/驳回/转交/撤回
- ✅ 完成事件回调（ILocalEventBus）
- ✅ 审批人解析抽象（User 内置 + Role 消费方）
- ✅ 唯一约束防重复 + 终态释放

### 不包含（v0.2.0 候选）

- ❌ 并行分支/条件网关
- ❌ 委托审批/超时自动处理
- ❌ 审批流设计器 UI
- ❌ 外部工作流引擎（Elsa/WorkflowCore）
- ❌ 审批任务保留清理

---

**文档信息**: V0.1.0 | 2026-09-09 | 关联：ADR-Approval-轻量审批引擎-数据模型与状态机.md、v0.1.0-Approval-轻量审批引擎-开发方案.md（主框架私有）
