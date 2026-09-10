# TKWF.Ext.Account.Abstractions 契约抽象

**状态**: 契约抽象（ADR48 D7 依赖倒置） | **框架**: .NET 10 | **依赖**: 零框架引用（纯 BCL）

## 定位

Account 扩展的密码落地适配器契约——`IAccountPasswordManager` 是密码重置流程与用户存储之间的抽象接缝：重置后的新密码（客户端已计算 PBKDF2 散列）经此写入用户存储。Account 扩展**不提供默认实现**，由消费方或其它扩展实现并注册——V0.3.0 起 Identity 扩展提供 `IdentityPasswordManager` 开箱实现（注入 `IUserManager` 落库，组装方案 + IOptions 迭代配置）。属 Account 扩展（V0.3.0 拆包）侧拆出的契约抽象。

## 内容

| 类型 | 说明 |
|------|------|
| `IAccountPasswordManager` | 密码落地抽象——`UserExistsAsync(userName, ct)`（防用户枚举）/ `SetPasswordAsync(userName, newClientHash, salt, ct)`（newClientHash = 客户端 PBKDF2 散列，salt = 盐；均支持 CancellationToken） |

## 零破坏铁律

- **命名空间保持 `TKWF.Ext.Account`**（不追加 `.Abstractions` 后缀）——既有消费方 `using TKWF.Ext.Account;` 不变（Oracle P2-1c：既有接口迁移不破消费方 using）。
- 仅程序集从 `TKWF.Ext.Account` 拆出为 `TKWF.Ext.Account.Abstractions`；接口签名与拆分前完全一致。
- **纯 BCL**：`IAccountPasswordManager` 仅用 `string` / `CancellationToken` / `Task`——零框架引用（区别于 Permissions.Abstractions 依赖 `TKWF.Domain`）。

## 引用关系

```
Account（实现，密码重置流程 DefaultPasswordResetFlow）──ProjectReference──▶ Account.Abstractions（契约）
Identity（消费方，IdentityPasswordManager 默认实现）──ProjectReference──▶ Account.Abstractions（契约）
```

**注意**：`IAccountPasswordManager` 未注册时，`DefaultPasswordResetFlow.InitiateResetAsync` 返回 `false` 并记录 Warning。账户锁定 / 密码重置完整用法见 **Account 扩展使用指南**（`docs/Account/账户管理扩展-使用指南.md`）。