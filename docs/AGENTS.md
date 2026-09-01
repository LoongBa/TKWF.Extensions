# AGENTS 路由指南

> 本文档是 Agents 的默认上下文。遇到以下问题时，去读对应文档。

---

## 项目总览

- 仓库：TKWF.Extensions — TKWF 业务扩展包仓库
- 定位：`TKWF.Ext.*` 扩展（Tags/Permissions/Navigation 等）独立开发、独立版本，不进入主框架 slnx
- 主框架：`../_TKWF/`（TKW.Framework，经 `$(TKWFRoot)` ProjectReference 引用）
- 扩展进度：主框架私有 `docs/03_扩展模块/总览和跟踪.md`（扩展迭代/跟踪不公开；公开仓库存代码/指南）
- 本仓库文档：`docs/`（指南/模板/规则）

---

## 路由表

| 遇到问题 | 去读 |
|---------|------|
| 目录怎么组织？版本怎么管理？跨仓库引用？ | `docs/目录结构与版本管理规则.md` |
| 扩展路线图、跟踪执行状态？ | 主框架私有 `../_TKWF/docs/03_扩展模块/总览和跟踪.md` |
| 扩展机制基座（TKWFExtension/三钩子/扩展包发现）？ | 主框架 `_TKWF/docs/D17-*.md` + `ADR37-39` |
| 某个扩展怎么用？ | `_Framework/{扩展名}/README.md`（Tags）；`docs/{扩展名}/`（使用指南） |
| 架构决策记录（为什么选 X 不选 Y）？ | 主框架私有 `../_TKWF/docs/03_扩展模块/{扩展名}/ADR/` |
| 文档模板？ | `docs/模板/` |
| 讨论中未定稿？ | `docs/草稿/` |

---

## 核心规则（简要）

- **扩展 = 独立单元**：项目 + 测试 + 文档 + 版本各自独立；`TKWF.Extensions.slnx` 统一注册，**不进** `TKW.Framework.slnx`
- **跨仓库引用**：主框架用 `$(TKWFRoot)`（`Directory.Build.props` 定义 = `../_TKWF/`）；扩展间用 ProjectReference（编译期依赖）
- **MinVer 版本**：`V{major}.{minor}.{patch}`，tag 前缀 `v`，扩展版本独立于主框架
- **扩展 ADR 命名**：`ADR-{扩展名称}-{title}.md`（不走主框架 ADR01-39 序号），三问必填
- **Commit 纪律**：避免频繁，积累后统一提交（一次迭代 1-4 个）
- **Tag 纪律**：必须有开发方案 + 审核报告且征得同意，才能打 tag
- **扩展初始化器**：继承 `ExtensionInitializer<TUserInfo>` + `[TKWFExtension]` 特性 → SG1 编译期发现（生成能力清单）；**启用须消费方在领域初始化器上 `[TKWFEnabledExtension(typeof(XxxExtensionInitializer<>))]` 白名单声明**（发现不自动启用，V4.9.85 ADR47），声明后三钩子自动接线（ConfigureServices/ConfigureFilters/InitializeAsync）