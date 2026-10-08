# UserCenter（用户中心）退役——消费方适配通知

> **发出方**: TKWF 扩展模块组｜**日期**: 2026-10-08｜**状态**: 待发布（收尾实施完成后定稿）
> **收件方**: DMP-Lite 等引用 `TKWF.Ext.UserCenter` / `TKWF.Ext.UserCenter.Abstractions` 的消费方项目组
> **关联**: 收尾方案 `docs/UserCenter/UserCenter-退役收尾-开发方案.md` · 变更记录（2026-10-08）· 授权面前置裁定（P2 FREEZE）

---

## ⚡ 一句话摘要

**`TKWF.Ext.UserCenter`（用户中心）独立扩展已完整删除（2026-10-08，不留过渡期）**——契约包随删，DTO 资产已归 `AuthSurface`。请按下表适配，**类型签名不变，仅改包引用 / using / 调用门面**。

---

## 一、变更总览

| 对象 | 变更前 | 变更后 |
|------|--------|--------|
| `RedemptionRecordDto` / `UserAppDto` | `TKWF.Ext.UserCenter`（UserCenter.Abstractions 包） | **`TKWF.Ext.AuthSurface`（AuthSurface 主包）**——形状不变 |
| `IRedemptionHistorySource` / `IUserAppsSource` | UserCenter.Abstractions 契约 | **已删除**——兑换/应用查询改经 **AuthSurface 门面** |
| `IUserProfileSource` | UserCenter.Abstractions 契约 | **已删除**——档案读改经 **AuthCenter** 门面 |
| `IUserCenterQueryService` / `UserProfileDto` | UserCenter 主包门面 / 档案 DTO | **已删除**——三数据面分别经属主门面，页面在装配层组合 |

## 二、逐项适配指引

### 1. DTO 引用（最常见改动）

```csharp
// 变更前
using TKWF.Ext.UserCenter;
Task<IReadOnlyList<RedemptionRecordDto>> redemptions = ...;
Task<IReadOnlyList<UserAppDto>> apps = ...;

// 变更后（引 AuthSurface 主包即得）
using TKWF.Ext.AuthSurface;
Task<IReadOnlyList<RedemptionRecordDto>> redemptions = ...;
Task<IReadOnlyList<UserAppDto>> apps = ...;
```

> csproj：`<PackageReference Include="TKWF.Ext.UserCenter.Abstractions" />` 删除；`TKWF.Ext.AuthSurface` 已含 DTO 类型（无需额外包；如未引 AuthSurface 主包需补充引用）。

### 2. 兑换历史 / 我的应用查询（接口删除 → 属主门面）

```csharp
// 变更前（UserCenter 门面 / 依赖 Source 契约——均已删除）
IUserCenterQueryService.GetRedemptionsAsync(userId);
IUserCenterQueryService.GetAppsAsync(userId);

// 变更后（AuthSurface 自有门面——User.Use<T>() AOP 解析）
User.Use<IRedemptionQueryService>().GetRedemptionsAsync(userId);   // 兑换历史（TKWFV_UserRedemptionHistory）
User.Use<IUserAppsQueryService>().GetAppsAsync(userId);            // 我的应用（TKWFV_UserApps）
```

### 3. 公共档案（接口删除 → AuthCenter 内建门面）

```csharp
// 变更后（档案读统一经 AuthCenter；返回 UserProfileEntity——昵称/头像/邮箱等，不含 Phone）
User.Use<IAuthAccountQueryService>().GetProfileByUIdAsync(userId);
// 手机号脱敏展示（如需）：另行经 GetByUIdAsync 取 AuthAccount 后在装配层自行脱敏
```

### 4. 用户中心页面（装配层）

页面端点组合上述三处属主门面即可（档案 + 兑换 + 应用），无聚合层扩展。页面模板/渲染归装配层职责（AGENTS §8 表现层）。

## 三、破坏面与时间线

| 项 | 说明 |
|----|------|
| **包可用性** | `TKWF.Ext.UserCenter` / `.Abstractions` **0.1.0 已 unlist**（仍可 restore 已有 lock file 引用，但不再出现在搜索/列表）；**preview 版本线（`0.1.1-preview.*`）一并 unlist** |
| **编译断裂** | 引用 UserCenter 命名空间/DTO/接口的代码会编译失败——按 §二 迁移 |
| **NuGet restore** | 已锁定 0.1.0/preview 版本的项目可继续 restore（unlist ≠ 删除）——迁移窗口内不破坏构建 |
| **建议** | 立即按 §二 迁移；迁移后删除 UserCenter 包引用 |

## 四、验证

迁移后：`dotnet build` 无 UserCenter 相关错误 + `dotnet test` 全绿；`grep -ri "UserCenter" src/`（自身代码域）零命中。

## 五、咨询

适配问题 → 扩展模块组（转达/本通知回执通道）。

---

<!-- EOF -->