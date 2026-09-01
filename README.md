# TKWF.Extensions

> **TKWF 扩展仓库**：所有 TKWF 业务扩展（`TKWF.Ext.*`）的独立仓库。
>
> 扩展包与主框架 **TKW.Framework**（`_TKWF/`）解耦——扩展代码、扩展测试、扩展文档独立演进，不进入主框架解决方案（`TKW.Framework.slnx`）。

---

## 仓库定位

| 项 | 说明 |
|----|------|
| 主框架 | [`_TKWF/`](https://github.com/LoongBa/TKW.Framework)（TKW.Framework 领域框架） |
| 本仓库 | TKWF 业务扩展包（`TKWF.Ext.*`）——权限、导航、标签、身份、审计等 |
| 关系 | 扩展 **ProjectReference 引用主框架源码**（`$(TKWFRoot)`，跨仓库编译期依赖），**不进入主框架 slnx** |
| 版本 | MinVer 自动管理（git tag 即版本，前缀 `v`）——与主框架版本**独立** |

### 引用模式

```
_TKWF.Extensions/                     ← 本仓库
├── Directory.Build.props             ← 定义 TKWFRoot = ../_TKWF/
└── _Framework/
    └── Tagging/
        └── TKWF.Ext.Tagging.csproj   ← <ProjectReference Include="$(TKWFRoot)_Framework\Domain\TKWF.Domain.csproj" />
```

扩展包编译时通过 `$(TKWFRoot)`（MSBuild 属性，相对路径指向同级 `_TKWF/`，不依赖环境变量——对齐 DMP-Lite 跨仓库引用模式）引用主框架。主框架发布 NuGet 后可选切换为 PackageReference。

---

## 目录结构

```
_TKWF.Extensions/
├── _Framework/               # 扩展包源码（按扩展组织，各扩展含自身 README + docs 使用指南）
│   ├── Permissions/          # 权限扩展（主框架 V4.9.72 迁出候选；docs/ 含使用指南）
│   ├── Navigation/           # 导航扩展（主框架 V4.9.74 迁出候选；docs/ 含使用指南）
│   └── Tagging/              # 标签扩展（V4.9.79 迁出，首个独立扩展）
│       ├── TKWF.Ext.Tagging.csproj
│       ├── README.md         # 标签提取引擎技术规范
│       ├── Matchers/         # 匹配器家族
│       ├── Processors/       # 后处理器（互斥裁剪/默认兜底）
│       └── *.cs              # ITagService / TagService / 流水线 / 初始化器
├── _Tests/                   # 扩展测试（一组扩展一个测试项目）
│   └── Extension.Tagging.Tests/
├── docs/                     # 扩展仓库级文档（公开：指南/规则/模板）
│   ├── AGENTS.md             # Agents 路由指南
│   ├── 目录结构与版本管理规则.md
│   ├── Permissions/          # 权限扩展指南（使用指南）
│   ├── Navigation/           # 导航扩展指南（使用指南）
│   ├── Tagging/              # 标签扩展（文档/指南）
│   ├── 模板/                  # 文档模板
│   └── 草稿/
├── Directory.Build.props     # TKWFRoot + MinVer + 打包属性 + _PushToRefs
├── Directory.Packages.props  # CPM 集中包版本
├── .gitignore
└── TKWF.Extensions.slnx      # 扩展解决方案（不进入主框架 slnx）
```

> 📦 **扩展迭代开发/ADR/总览跟踪**（内部工作产物）存放于**主框架私有仓库** `_TKWF/docs/03_扩展模块/`（不公开）；本公开仓库存**代码 + 测试 + 指南**。边界详见 `docs/目录结构与版本管理规则.md`。

---

## 快速开始（新增一个扩展包）

1. **建项目**：`_Framework/{扩展名}/TKWF.Ext.{扩展名}.csproj`（net10.0，引用 `$(TKWFRoot)_Framework\Domain\TKWF.Domain.csproj`）
2. **注册 slnx**：`TKWF.Extensions.slnx` 加 Folder + Project
3. **建测试**：`_Tests/Extension.{扩展名}.Tests/`（xunit.v3，引用本仓库扩展 + 主框架测试依赖）
4. **写文档**：扩展迭代开发/ADR 登记到主框架私有 `_TKWF/docs/03_扩展模块/`（开发方案 → 审核报告 → ADR）；使用指南放本仓库 `docs/{扩展名}/`
5. **接线**：扩展初始化器继承 `ExtensionInitializer<TUserInfo>` + `[TKWFExtension]` 特性（SG1 编译期发现生成能力清单；**启用须消费方在领域初始化器上 `[TKWFEnabledExtension(typeof(XxxExtensionInitializer<>))]` 白名单声明**——发现不自动启用，V4.9.85）

> 详细规则见 `docs/目录结构与版本管理规则.md` + `docs/AGENTS.md`。

---

## 扩展规划与跟踪

扩展路线图、各扩展迭代/ADR（内部工作产物，不公开）→ **主框架私有仓库** [`_TKWF/docs/03_扩展模块/总览和跟踪.md`](../_TKWF/docs/03_扩展模块/总览和跟踪.md)（唯一跟踪入口）。

| 扩展 | 状态 | 版本 | 说明 |
|------|:----:|------|------|
| Tags（Tagging） | ✅ 已迁出 | v0.1.0（独立起点） | 标签提取/匹配/格式化（从框架核心迁出，D17 模式 3） |

---

## 版本管理一览

```
MinVer 风格：V{major}.{minor}.{patch}（git tag 前缀 v）

扩展包独立版本：与主框架 _TKWF 版本独立（各打各的 tag）
开发方案命名：{扩展名称}-开发方案.md
审核报告命名：{扩展名称}-审核报告.md
ADR 命名：ADR-{扩展名称}-{title}.md

Tag 纪律：必须有开发方案 + 审核报告，且征得同意
```

---

## 适用场景 / 边界

- ✅ TKWF 官方业务扩展（D17 Phase 4：Identity / AuditLogging / Settings / BlobStoring / Emailing / Account / Tagging）
- ✅ 第三方开发者基于 TKWF 的扩展（可独立仓库，不强制入本仓库）
- ❌ 主框架核心（Domain / Core / SG / Infrastructure）——留在 `_TKWF/`
- ❌ 扩展机制基座本身（D17/ADR37-39）——主框架文档，不受本仓库约束

---

## 许可证

Copyright © LoongBa.cn 2026 · MIT

## 相关仓库

- [TKW.Framework（主框架）](https://github.com/LoongBa/TKW.Framework) — 领域框架 + 扩展机制（`TKWFExtensionAttribute` + `ExtensionInitializer` 三钩子 + SG1 发现 + `[TKWFEnabledExtension]` 白名单启用）
- [LoongBa-Scaffold](https://github.com/LoongBa/LoongBa-Scaffold) — 本文档体系脚手架来源