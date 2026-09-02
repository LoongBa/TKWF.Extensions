# TKWF.Extensions

> TKWF 扩展模块仓库——权限、导航、身份、审计、设置、邮件、存储、账户、数据字典等。
>
> 扩展模块与主框架 **[TKW.Framework](https://github.com/LoongBa/TKW.Framework)** 解耦：扩展代码、测试、使用指南独立演进，不进入主框架 `TKW.Framework.slnx`。

---

## 扩展模块一览

> 各扩展模块使用说明

| 扩展                         | 版本            | 说明                                                                     | Tag                     | README                                   | 指南                                     |
| ---------------------------- | --------------- | ------------------------------------------------------------------------ | ----------------------- | ---------------------------------------- | ---------------------------------------- |
| **Permissions**              | V0.7.0 + V0.8.0 | 细粒度权限定义 / fail-closed 检查 / 编译期权限名校验（PERM001）          | `v0.7.0`                | [README](./_Framework/Permissions/README.md) | [指南](./docs/Permissions/权限扩展-使用指南.md) |
| **Permissions.Abstractions** | V0.1.0          | 权限契约抽象（`IPermissionChecker`/`RequirePermission`/`IRoleProvider`） | —                       | —                                        | —（并入 Permissions）                    |
| **Permissions.Validation**   | V0.8.0          | 扩展侧 PERM001 DiagnosticAnalyzer（从内核移除耦合）                      | —                       | —                                        | —（并入 Permissions）                    |
| **Identity**                 | V0.1.0          | 用户 / 角色 / 用户角色分配 + PasswordHasher 凭据验证                     | `Identity/v0.1.0`       | [README](./_Framework/Identity/README.md)      | [指南](./docs/Identity/身份管理扩展-使用指南.md) |
| **Account**                  | V0.1.0          | 账户锁定 + 密码重置流程（主框架 V4.9.45 缺口补齐）                       | `Account/v0.1.0`        | [README](./_Framework/Account/README.md)       | [指南](./docs/Account/账户管理扩展-使用指南.md) |
| **Navigation**               | V0.1.0          | 菜单数据模型 / 贡献机制 / 权限过滤（从主框架迁出）                       | `Navigation/v0.1.0`     | [README](./_Framework/Navigation/README.md) | [指南](./docs/Navigation/导航扩展-使用指南.md)  |
| **AuditLogging**             | V0.1.0          | 审计日志 FreeSql 存储 + SG1 实体                                         | `AuditLogging/v0.1.0`   | [README](./_Framework/AuditLogging/README.md)  | [指南](./docs/AuditLogging/审计日志扩展-使用指南.md) |
| **Settings**                 | V0.1.0          | 全局/用户级配置持久化 + 分层读取                                         | `Settings/v0.1.0`       | [README](./_Framework/Settings/README.md)      | [指南](./docs/Settings/设置管理扩展-使用指南.md) |
| **BlobStoring**              | V0.1.0          | 二进制大对象本地文件系统存储 + FreeSql 记录                              | `BlobStoring/v0.1.0`    | [README](./_Framework/BlobStoring/README.md)   | [指南](./docs/BlobStoring/二进制存储扩展-使用指南.md) |
| **Emailing**                 | V0.1.0          | SMTP/MailKit 邮件发送 + FreeSql 发送记录                                 | `Emailing/v0.1.0`       | [README](./_Framework/Emailing/README.md)      | [指南](./docs/Emailing/邮件发送扩展-使用指南.md) |
| **DataDictionary**           | V0.1.0          | 数据字典集中管理（定义 + 项 + 按编码查询）                               | `DataDictionary/v0.1.0` | [README](./_Framework/DataDictionary/README.md) | [指南](./docs/DataDictionary/数据字典扩展-使用指南.md) |
| **Tagging**                  | V0.2.0          | 标签存储扩展（标签算法已回归 `TKW.Framework.Utility.Tags`，ADR52 瘦身）  | `v0.1.0`                | [README](./_Framework/Tagging/README.md)       | [指南](./docs/Tagging/标签服务扩展-使用指南.md)  |

> 列说明：**README** = 扩展技术规范（随 NuGet 发布，位于 `_Framework/{扩展名}/`）；**指南** = 使用指南（公开文档，位于 `docs/{扩展名}/`）。Permissions.Abstractions/Validation 无独立文档，详见 Permissions 的 README 与指南。

> 全量 **289 测试全绿**——`dotnet test` 零失败。

---

## 仓库定位

| 项       | 说明                                                                                                               |
| -------- | ------------------------------------------------------------------------------------------------------------------ |
| 主框架   | [`_TKWF/`](https://github.com/LoongBa/TKW.Framework)（TKW.Framework 领域框架）                                     |
| 本仓库   | TKWF 业务扩展包（`TKWF.Ext.*`）——独立版本，与主框架版本无关                                                        |
| 引用模式 | 扩展经 `$(TKWFRoot)` ProjectReference 引用主框架源码（跨仓库编译期依赖）；主框架发布 NuGet 后可切 PackageReference |
| 版本管理 | MinVer 自动管理（git tag 即版本）；各扩展独立版本（各打各的 tag，命名空间前缀如 `Identity/v0.1.0`）                |

---

## 目录结构

```
_TKWF.Extensions/
├── _Framework/                     # 扩展源码（每个扩展一个项目）
│   ├── Permissions/                 # 权限扩展（V0.7.0：定义/检查/存储/管理 API/Admin.All）
│   ├── Permissions.Abstractions/    # 权限契约抽象（ADR48 D7 依赖倒置）
│   ├── Permissions.Validation/       # PERM001 编译期校验 Analyzer（V0.8.0，扩展侧）
│   ├── Identity/                     # 用户 + 角色 + 用户角色分配 + 凭据验证
│   ├── Account/                      # 账户锁定 + 密码重置流程（主框架缺口补齐）
│   ├── Navigation/                  # 菜单数据模型 + 贡献机制 + 权限过滤
│   ├── AuditLogging/                # 审计日志 FreeSql 存储
│   ├── Settings/                    # 设置管理 FreeSql 存储 + 分层读取
│   ├── BlobStoring/                 # 二进制存储（本地文件系统 + FreeSql 记录）
│   ├── Emailing/                    # SMTP/MailKit 邮件发送
│   ├── DataDictionary/              # 数据字典集中管理
│   └── Tagging/                     # 标签存储扩展（算法已回归 TKW.Framework.Utility.Tags）
├── _Tests/                          # 测试（一组扩展一个测试项目）
│   ├── Extension.Permissions.Tests/
│   ├── Extension.Permissions.Consumer/    # 消费方集成验证
│   ├── Extension.Permissions.Validation.Tests/  # Analyzer 单测
│   ├── Extension.Identity.Tests/
│   ├── Extension.Account.Tests/
│   ├── Extension.Navigation.Tests/
│   ├── Extension.AuditLogging.Tests/
│   ├── Extension.Settings.Tests/
│   ├── Extension.BlobStoring.Tests/
│   ├── Extension.Emailing.Tests/
│   ├── Extension.DataDictionary.Tests/
│   └── Extension.Tagging.Tests/
├── docs/                           # 公开使用指南（每个扩展一份）
│   ├── Permissions/权限扩展-使用指南.md
│   ├── Identity/身份管理扩展-使用指南.md
│   ├── Account/账户管理扩展-使用指南.md
│   ├── Navigation/导航扩展-使用指南.md
│   ├── AuditLogging/审计日志扩展-使用指南.md
│   ├── Settings/设置管理扩展-使用指南.md
│   ├── BlobStoring/二进制存储扩展-使用指南.md
│   ├── Emailing/邮件发送扩展-使用指南.md
│   ├── DataDictionary/数据字典扩展-使用指南.md
│   └── Tagging/标签服务扩展-使用指南.md
├── Directory.Build.props           # TKWFRoot + MinVer + 打包属性
├── Directory.Packages.props         # CPM 集中包版本
├── AGENTS.md                        # 扩展仓库开发规则（AI Agent 与人工开发者必读）
└── TKWF.Extensions.slnx            # 扩展解决方案
```

> 扩展迭代开发方案 / 审核报告 / ADR / 总览跟踪存放于**主框架私有仓库** `_TKWF/docs/03_扩展模块/`（不公开）；本公开仓库存**代码 + 测试 + 使用指南**。

---

## 架构模式

所有扩展遵循统一架构模式（异常静默 + TryAddScoped + SG1 声明式实体）：

```
扩展项目（net10.0）
├── Entity（partial class + [Table] + [DomainGenerateCode] + FreeSql [Column]）
├── Store 抽象 + FreeSql 实现（internal sealed + 异常静默）
├── Manager 门面（internal sealed + 聚合查询）
├── ExtensionInitializer（[TKWFExtension] + TryAddScoped 三钩子）
└── README.md（技术规范）

测试项目（xunit.v3 + FreeSql SQLite 内存）
├── ConsumerHostInitializer（[TKWFEnabledExtension] 白名单样板）
└── 测试类（Store CRUD + Manager 聚合 + Initializer DI + 异常静默）
```

### 扩展启用（v4.9.85+ 必需）

扩展不再"发现即启用"——消费方须在领域初始化器上显式声明白名单：

```csharp
using TKWF.Ext.Identity;

[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

声明后扩展三钩子（`ConfigureServices`/`ConfigureFilters`/`InitializeAsync`）自动接线。

### 依赖倒置（ADR48 D7）

扩展间依赖走 `.Abstractions`（接口/契约），不引用实现项目——L2 门控硬约束（`TKWF0022` Error）。

```
Navigation → Permissions.Abstractions（✅ 合法）
Navigation → Permissions（❌ TKWF0022 Error）
```

---

## 快速开始

### 消费方引用扩展

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\..\_Framework\Identity\TKWF.Ext.Identity.csproj" />
```

### 启用 + 使用

```csharp
// 1. 白名单声明（v4.9.85+）
[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 2. 注入 + 使用
public class AuthService(IUserManager userManager)
{
    public async Task<UserEntity?> LoginAsync(string name, string password)
        => await userManager.VerifyCredentialsAsync(name, password);
}
```

---

## 版本管理

```
MinVer：V{major}.{minor}.{patch}（git tag 命名空间前缀，如 Identity/v0.1.0）

扩展包独立版本：与主框架 _TKWF 版本完全独立
Tag 纪律：必须有开发方案 + 审核报告，且征得用户同意
```

---

## 扩展规划

P0（必须）：**9/11 已实施**——Identity / Account / Navigation / AuditLogging / Settings / BlobStoring / Emailing / DataDictionary / Tagging + Permissions（V0.7.0 + V0.8.0 编译期校验）。注：Tagging 标签算法已按 ADR52 回归主框架 `TKW.Framework.Utility.Tags`，扩展保留存储层。

P0 剩余：**PrintTemplates**（打印模板，需先写 ADR 定模板引擎选型 Scriban vs RazorLight + 版本化策略）

路线图与跟踪详见主框架私有 [`_TKWF/docs/03_扩展模块/总览和跟踪.md`](https://github.com/LoongBa/TKW.Framework/blob/master/docs/03_扩展模块/总览和跟踪.md)。

---

## 许可证

Copyright © 2026 LoongBa · [Apache-2.0](./LICENSE)

> 开源、允许商用与闭源衍生，但必须保留版权与归属声明（Attribution）。

## 相关仓库

- [TKW.Framework（主框架）](https://github.com/LoongBa/TKW.Framework) — 领域框架 + 扩展机制（`TKWFExtensionAttribute` + `ExtensionInitializer` 三钩子 + SG1 发现 + `[TKWFEnabledExtension]` 白名单启用 + 三层门控 ADR50）
- [LoongBa-Scaffold](https://github.com/LoongBa/LoongBa-Scaffold) — 文档体系脚手架来源
