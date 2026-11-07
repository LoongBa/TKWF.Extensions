# TKWF.Ext.Navigation.Abstractions 导航契约抽象

**状态**: 契约抽象（ADR48 D7 依赖倒置 / ADR-Navigation-贡献者机制A+迁移） | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Permissions.Abstractions`（`PermissionLogic`——`MenuItemDefinition` 权限过滤声明用）

## 定位

导航扩展（Navigation）的**公共契约**——`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext` 三件套自主框架迁出时拆出的契约抽象（A+ 贡献者机制阶段 2 契约归位）：**消费方/业务模块实现 `IMenuContributor` 声明菜单**，Navigation 主包框架化收集与装配（多菜单分区 + 权限过滤）。扩展间只引契约不引实现（L2 门控 `TKWF0022`）。

## 内容

| 类型 | 说明 |
|------|------|
| `IMenuContributor` | 菜单贡献者接口——`ConfigureMenu(MenuConfigurationContext)`（业务模块声明菜单的唯一切入点） |
| `MenuItemDefinition` | 菜单项定义（`Name`/`DisplayName`/`Url`/`Icon`/`Order`/`Parent`/`MenuName`/`RequiredPermissions`/`PermissionLogic`/`IsEnabled`/`IsVisible`——`MenuName` 多菜单分区、权限过滤声明含 Logic 逻辑） |
| `MenuConfigurationContext` | 菜单配置上下文——`Add(MenuItemDefinition)`（Name/MenuName 非空校验 + 重名校验） |

**零破坏铁律**：namespace 保持 `TKWF.Ext.Navigation`（不追加 `.Abstractions` 后缀）——既有消费方 `using TKWF.Ext.Navigation;` 不变（对齐 Account.Abstractions Oracle P2-1c 先例）。

## 版本与发布

- MinVerTagPrefix：`Navigation.Abstractions/v`（独立 tag 独立演进）
- 当前 V0.1.0（A+ 阶段 2 拆包首发；V4.10.32 起纳入 slnx 注册与 CI pack——阶段 2 拆包遗漏曾致该包未发布，补注册后发布闭环）
- 契约演进随 Navigation 主包迭代（贡献者机制 A+ 迁移、MenuContributorAttribute 移除与契约归属收敛见 Navigation 使用指南与 ADR）

<!-- EOF -->