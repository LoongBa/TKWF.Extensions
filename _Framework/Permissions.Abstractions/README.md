# TKWF.Ext.Permissions.Abstractions 契约抽象

**状态**: 契约抽象（ADR48 L2 依赖倒置） | **框架**: .NET 10 | **依赖**: 主框架 TKWF.Domain（`DomainFlagAttribute` 基类 + `IUserInfo` 约束）

## 定位

Permissions 扩展的公共契约——**实现项目与 Navigation / Notifications 等消费方共用**，替代"扩展直接引用另一扩展实现项目"（ADR48 D7 / L2 门控 TKWF0022 Error）。覆盖权限定义、检查、存储、角色解析全套契约，属 Permissions 扩展（V0.7.0）侧拆出的契约抽象。

## 内容

| 类型 | 说明 |
|------|------|
| `IPermissionChecker` | 权限检查器——`IsGrantedAsync(string)` / `IsGrantedAsync(params string[])`（批量返回 权限名 → 是否授予 字典）。由 `[RequirePermission]` 过滤器在 PreProceed 阶段调用 |
| `IPermissionStore` | 权限授予值持久化——`GetAsync(permissionName, providerName, providerKey)` / `SetAsync(...)`；按 provider 批量 `GetGrantedPermissionNamesAsync(...)`（V0.8.1 N+1 优化，内存 fail-closed 判定） |
| `IRoleProvider<TUserInfo>` | 角色提供者——为检查器解析用户角色列表（`where TUserInfo : class, IUserInfo, new()`；默认 `DefaultRoleProvider{TUserInfo}` 读 `IUserInfo.Roles`，消费方可替换） |
| `IPermissionDefinitionRepository` | 权限定义仓库——`GetAll()`（只读快照）/ `Contains(name)` / `AddRange(...)`；供检查器校验未知权限名（fail-closed） |
| `IPermissionDefinitionContributor` | 权限定义贡献者——业务模块实现 `Define(context)` 声明权限，实现类须标 `[PermissionContributor]` 供 SG1 发现 |
| `PermissionDefinitionContext` | 权限定义上下文——贡献者 `Add(new PermissionDefinition { ... })` 收集（Name 必填且唯一，空名/重复抛异常） |
| `PermissionDefinition` | 权限定义模型（Name / DisplayName / Group / Parent / Description；点分层级，如 `"Order.Create"`） |
| `RequirePermissionAttribute` | 方法/接口级权限声明标记（`DomainFlagAttribute` 基类；`params string[]` + `Logic` All/Any，`AllowMultiple=true`） |
| `PermissionContributorAttribute` | 贡献者标记（纯标记，无载荷）——SG1 据此识别并生成贡献者注册表 |
| `PermissionNames` | 内置权限名常量——`AdminAll = "Admin.All"` 系统管理员通配（隐式定义，不依赖贡献者声明，检查器先行放行拥有者） |
| `PermissionLogic` | 权限判定逻辑枚举——`All` 全部授予（默认）/ `Any` 任一授予 |
| `PermissionGrantResult` | 授予结果值对象——`Granted` / `Denied` 单例（`IPermissionStore.GetAsync` 返回值） |

## 命名空间

- 全部类型命名空间 **`TKWF.Ext.Permissions.Abstractions`**——与实现项目（`TKWF.Ext.Permissions`）命名空间分离。
- 依赖说明：`RequirePermissionAttribute` 的基类 `DomainFlagAttribute` 与 `IRoleProvider<TUserInfo>` 的约束类型 `IUserInfo` 来自主框架 `TKWF.Domain`——故本程序集**非零框架引用**（区别于 BlobStoring.Abstractions / Account.Abstractions 的纯 BCL 契约）。

## 引用关系

```
Permissions（实现）──ProjectReference──▶ Permissions.Abstractions（契约）
Navigation / Notifications（消费方）──ProjectReference──▶ Permissions.Abstractions（契约）
```

消费方引用 Abstractions 而非 Permissions 实现项目；权限检查 / 存储 / 角色解析可自定义实现并 TryAddScoped 覆盖默认。完整权限定义与检查流程见 **Permissions 扩展使用指南**（`docs/Permissions/权限扩展-使用指南.md`）。