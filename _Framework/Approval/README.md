# TKWF.Ext.Approval 轻量审批引擎扩展技术规范

**状态**: P1 差异化模块 (Differentiation Module) | **版本**: V0.2.0 | **框架**: .NET 10

**核心约束**: 三实体模型（流程定义/审批实例/审批任务）+ 内置自建状态机（Draft→Pending→Approved/Rejected/Withdrawn）+ 审批人解析抽象（User 内置 + Role 消费方）+ 完成事件回调（ILocalEventBus post-commit）+ 顺序步骤链 + 或签/会签 + **v0.2.0 深化：委派（Flowable 两阶段）/加签（运行时追加）/抄送（仅通知）/超时自动处理（5 动作）**；不依赖外部工作流引擎（Elsa/WorkflowCore）。

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

## 六、边界（V0.2.0）

### 包含

- ✅ 顺序步骤链 + 或签（Any）/会签（All）
- ✅ 审批/驳回/转交/撤回
- ✅ 完成事件回调（ILocalEventBus）
- ✅ 审批人解析抽象（User 内置 + Role 消费方）
- ✅ 唯一约束防重复 + 终态释放
- ✅ **委派**（Flowable 两阶段——DelegateTaskAsync/ResolveTaskAsync，原审批人保留，委派中不可 Complete）
- ✅ **加签**（AppendApproverAsync——Participate 建任务 / Notify 事件；与定义时会签区分：运行时动态追加）
- ✅ **抄送**（ApprovalCCEntity + Start/Finish 位置 + ApprovalCcNotifiedEvent——投递组装 Notifications 归消费方）
- ✅ **超时自动处理**（步骤级 TimeoutMinutes + Remind/Transfer/Jump/Approve/Reject 5 动作 + IApprovalTimeoutService 后台触发 + 系统身份审计 system:timeout + 扫描占位防重）
- ✅ 遗留修复（DB 级分页 total 独立 count / 步骤判定上限 int.MaxValue / WithdrawAsync 事务包裹 / RejectedAt 验证）

### 不包含（v0.3.0 候选）

- ❌ 并行分支/条件网关/循环/子流程
- ❌ 审批历史统计/任务保留清理
- ❌ 审批流设计器 UI
- ❌ 外部工作流引擎（Elsa/WorkflowCore——ADR 裁定否决）

---

## 七、v0.2.0 深化（委派/加签/抄送/超时）

### 委派（临时代办——Flowable 两阶段）

```csharp
await approval.DelegateTaskAsync(taskId, "u_manager", "u_deputy");  // 委派（ApproverUserId 切换，原审批人保留）
await approval.ResolveTaskAsync(taskId, "u_deputy");                // 委派人解决 → 回原审批人
await approval.ApproveAsync(taskId, "u_manager");                   // 原审批人复核 Complete（委派中不可）
```
- **委派=临时**（同任务状态切换）/ **转交=永久**（TransferAsync 建新任务）——两模型并存；单层不嵌套；审计 DelegatedAt/DelegatedToUserId/OriginalAssigneeId 全记录。

### 加签（运行时追加审批人）

```csharp
await approval.AppendApproverAsync(taskId, "u_finance", ["u_legal"], mode: ApprovalAppendMode.Participate, remark: "需法务确认");
await approval.AppendApproverAsync(taskId, "u_finance", ["u_expert"], mode: ApprovalAppendMode.Notify);   // 仅通知+事件
```
- 去重（已是本步骤审批人/同批重复拒绝）；加签任务继承步骤超时；Notify 模式发布 `ApprovalAppendNotifyEvent`。

### 抄送（仅通知）

```csharp
var instanceId = await approval.StartAsync("Expense", "exp-002", "expense", "u_submitter", ccUserIds: ["u_finance"]);  // 命名参数（ct 之后）
await approval.AddCCAsync(instanceId, ["u_legal"]);   // 运行中追加
```
- Position：Start/Finish/StartFinish；`ApprovalCcNotifiedEvent`（Result 强类型）供消费方组装 Notifications；Withdrawn 不触发 Finish。

### 超时自动处理

```csharp
await approval.CreateFlowAsync("expense", "报销审批",
    [new(0, "部门经理", ApprovalApproverType.User, "u_manager", ApprovalMode.Any)
     { TimeoutMinutes = 48, TimeoutAction = ApprovalTimeoutAction.Remind },
     new(1, "财务", ApprovalApproverType.User, "u_finance", ApprovalMode.Any)
     { TimeoutMinutes = 72, TimeoutAction = ApprovalTimeoutAction.Transfer, TimeoutTransferToUserId = "u_finance2" }]);

// 消费方后台周期调用（扩展不内建调度器——BackgroundJobs 扩展可观察执行历史）：
var processed = await timeoutService.ProcessTimeoutTasksAsync();
```
- 5 动作：Remind/Transfer/Jump/Approve/Reject；系统身份审计 `system:timeout`；扫描占位防重（条件更新影响行数 0/1）。

---

**文档信息**: V0.2.0 | 2026-09-10 | 关联：ADR-Approval-轻量审批引擎-数据模型与状态机.md（v0.1.0）、ADR-Approval-v0.2.0-委派加签抄送与超时.md、v0.2.0-Approval-委派加签抄送与超时-开发方案.md（主框架私有）
