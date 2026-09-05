# TKWF.Ext.PrintTemplates 打印模板引擎与版本化扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (Scriban 沙箱渲染 + 模板版本化) | **框架**: .NET 10

**核心约束**: Scriban 沙箱契约（操作者可编辑安全）、Draft/Active/Archived 版本生命周期、`{Key}@{Version}` 审计固定渲染、存储异常传播不静默（审计关键）、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

打印模板（发票/凭证/单据打印格式）是 TKWF P0 扩展清单**最后一项**——ABP/OrchardCore 均无独立模块，TKWF 差异化。企业打印场景对模板有三个特殊要求：**操作者/租户可编辑**（非开发者专属）、**审计可追溯**（历史单据按打印时激活版本渲染，模板改版不改变历史单据外观）、**布局复杂**（行项目循环、金额/日期/文化格式化、条件分支）。

- **消费场景**：DMP-Lite 商户发票/单据打印——业务操作者经管理 UI 按商户排版要求调整模板（字段增删、金额格式、页眉页脚），不依赖开发者改代码。
- **选型背景**：四引擎调研（Scriban / RazorLight / Fluid / Handlebars.NET，ADR 明细）——RazorLight 模板 = 运行时编译为完全受信 C# 程序集（操作者可编辑即 RCE，且 AOT 不兼容、2023 年 stale）；Handlebars.NET 为 logic-less 设计，金额/日期格式化全部卡 C# helper；**Scriban 因安全沙箱 + AOT 兼容 + 零依赖 + 语法适配打印布局 + ABP TextTemplating 默认引擎先例（`IsSandboxed=true`）获选为唯一内置引擎**。
- **历史教训**：`StatEngine` POC 失败品（Metrics 承接归档裁定）的教训延伸——模板引擎不得引入"模板 = 任意代码执行"的运行时能力；安全姿态是选型第一判据。

## 二、设计原理 (Design Principles)

本扩展采用 **"Scriban 沙箱引擎（核心渲染）+ TKWF 集成层（扩展包）"** 单包架构（区别于 Metrics 的 Utility 拆分——渲染属安全敏感件，与持久化/门面同包内聚）。

### 1. 分层结构

- **模板存储（`ITemplateStore`）**：模板/版本 CRUD + 按 `Key`/`Key@Version`/`Active` 查询。FreeSql 实现，**异常传播不静默**——模板是审计关键资产，存储层错误必须显式暴露（C1）。
- **沙箱渲染（`ITemplateRenderer`）**：Scriban 沙箱渲染——`MemberFilter` 公共属性白名单 + **不配置 `TemplateLoader`**（阻断 `include` 读盘）+ 数据模型递归转换（不暴露原始 .NET 对象）+ 解析缓存 + 执行限制。无状态 → Singleton 线程安全。
- **管理门面（`ITemplateManager`）**：版本生命周期（Draft→Active→Archived）+ 发布版本自动 minor 递增 + 渲染入口（`version=null` 取最新 Active / 显式 version 精确固定，审计用）。Scoped。
- **扩展接线（`PrintTemplatesExtensionInitializer`）**：`[TKWFExtension]` SG1 发现 + 三钩子——ConfigureServices 注册三件套（TryAdd 语义，消费方可覆盖）+ Options 绑定；ConfigureFilters/InitializeAsync 不调用（V0.1.0 无过滤器/无种子数据）。

### 2. 关键设计

- **沙箱契约（ADR 决策 2，安全关键）**：仅公共属性可访问（`MemberFilter = m is PropertyInfo p && p.GetMethod?.IsPublic == true`，阻断方法/字段/`GetType`/`ToString`/反射逃逸，对齐 ABP 10.4 Scriban 姿态）；无 TemplateLoader；数据经 `IReadOnlyDictionary<string, object?>` 递归转换（C5：嵌套字典→脚本嵌套对象、集合→脚本数组、原始类型原样、**其余 .NET 类型转字符串**，杜绝业务对象泄漏）；沙箱配置随 `TemplateContext` 每渲染新建。
- **版本化语义（ADR 决策 3）**：`{Key}@{Version(SemVer)}` 复合身份；V0.1.0 全部 DB 持久化；`TemplateId + Version` 唯一约束（并发发布败者显式异常，C1）；发布自动 minor 递增（首版 `1.0.0`，后续 `max(非 Draft Minor)+1`，M4）；Draft 每模板一个（upsert），不占 Active 槽位，Publish 不提升 Draft（C3）。
- **发布便捷（C2）**：`PublishAsync`/`DraftAsync` 遇 Key 不存在自动建 `PrintTemplate` 行（`Name=Key`）——消费方无需预建模板即发布首版。
- **执行限制可调（C4）**：LoopLimit / RecursiveLimit / LimitToString / RegexTimeOut 由 `PrintTemplatesOptions` 绑定 `TKWF:PrintTemplates` 节（默认对齐 Scriban 默认值）。
- **解析失败显式化（M6）**：`Template.Parse` 返回 `HasErrors`/`Messages` → 抛 `TemplateParseException`（含解析消息列表），编译期错误哲学对齐 Metrics 严格失败姿态。

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`ITemplateStore`** | 模板/版本 CRUD + 按 Key/Key@Version/Active 查询；**异常传播**（审计关键，不静默，C1） | `FreeSqlTemplateStore`（FreeSql） |
| **`ITemplateRenderer`** | Scriban 沙箱渲染（公共属性白名单 + 无 TemplateLoader + 递归转换 + 解析缓存 + 执行限制） | `ScribanTemplateRenderer`（Singleton） |
| **`ITemplateManager`** | 管理门面：版本生命周期 + 发布自动 minor 递增 + 渲染入口 | `TemplateManager`（Scoped） |
| **`PrintTemplateEntity`** | 模板定义实体（Key 唯一 + Name + Description + 审计时间） | SG1 声明式实体 → `PrintTemplate` 表 |
| **`PrintTemplateVersionEntity`** | 版本实体（TemplateId + Version 唯一 + Content + Status + 发布审计） | SG1 声明式实体 → `PrintTemplateVersion` 表 |
| **`PrintTemplateVersionStatus`** | 版本状态枚举（Draft / Active / Archived） | 内置 |
| **`PrintTemplatesOptions`** | 执行限制配置（`TKWF:PrintTemplates` 节） | 内置（默认 Scriban 值） |
| **`TemplateParseException`** | 模板解析异常（含解析消息列表） | 内置 |
| **`PrintTemplatesExtensionInitializer<TUserInfo>`** | 扩展初始化器（`[TKWFExtension]` SG1 发现 + 三钩子 DI 接线） | 本扩展 |

## 四、版本化语义 (Versioning Semantics)

```
Draft（草稿）──── Publish ────→ Active（唯一）──── Archive ────→ Archived（可追溯渲染，不可再发布）
   │ 每模板仅一个（upsert）        │ 新 Active 发布时旧 Active 自动 Archived
   │ 版本号 "1.{next}.0-draft"    │ 发布自动 minor 递增：1.0.0 → 1.1.0 → 1.2.0 ...
   └── Publish 不提升 Draft（另传 content 建新 Active，C3）
```

- **版本号（M4）**：首版 = `1.0.0`；后续 = `max(现有非 Draft 版本 Minor) + 1`（Draft 版式 `1.{next}.0-draft` 不参与计算）。
- **唯一约束**：`TemplateId + Version` 唯一（`IX_ptv_template_version`）——并发发布同 Key 版本冲突时败者收到存储层显式异常（C1）。
- **生命周期规范**：Draft 草稿态不占 Active 槽位；发布即建新 Active 并归档旧 Active；Archived 可追溯渲染但不可再发布；改版不删除历史版本（审计前提）。
- **渲染解析**：`RenderAsync(key, model)` → 最新 Active；`RenderAsync(key, model, version)` → 精确版本固定渲染（历史单据审计；模板或版本缺失抛 `InvalidOperationException`）。
- **发布审计**：`PublishedAt` 写入（UTC）；`PublishedBy` 实体含列但 v0.1.0 Manager 未赋值（接消费方当前用户上下文为 v0.2.0 候选）。

## 五、配置 (Configuration)

`PrintTemplatesOptions` 绑定 `TKWF:PrintTemplates` 配置节（`BindConfiguration` + 默认值兜底）：

```json
{
  "TKWF": {
    "PrintTemplates": {
      "LoopLimit": 1000,
      "RecursiveLimit": 100,
      "LimitToString": 1048576,
      "RegexTimeOut": 10000
    }
  }
}
```

| 属性 | 默认 | 说明 |
|------|------|------|
| **`LoopLimit`** | 1000 | 模板循环上限（Scriban LoopLimit，防死循环） |
| **`RecursiveLimit`** | 100 | 模板递归上限（Scriban RecursiveLimit） |
| **`LimitToString`** | 1048576 | 字符串转换上限（1 MB，Scriban LimitToString） |
| **`RegexTimeOut`** | 10000 | 正则表达式超时（毫秒，Scriban RegexTimeOut） |

消费方亦可 `services.Configure<PrintTemplatesOptions>(o => ...)` 程序化覆盖。

## 六、安全契约 (Security Contract)

操作者可编辑模板的**强制约束**（ADR 决策 2；沙箱逃逸阻断测试为安全门禁）。

| 项 | 约束 | 说明 |
|----|------|------|
| **成员访问（MemberFilter）** | 仅公共属性且有公共 getter | `m is PropertyInfo p && p.GetMethod?.IsPublic == true`——阻断方法/字段/`GetType`/`ToString`/反射逃逸（对齐 ABP 10.4 Scriban 姿态） |
| **文件访问** | **不配置 `TemplateLoader`** | 模板正文中的 `include` 无法读盘 |
| **数据传递（递归转换，C5）** | `IReadOnlyDictionary<string, object?>` 递归转换 | 嵌套 `IDictionary` → 嵌套 `ScriptObject`；`IList`/数组 → `ScriptArray`；原始类型（string/数值/bool）原样；**其余 .NET 类型转字符串**——不暴露任何原始业务对象（含嵌套） |
| **解析缓存** | `ConcurrentDictionary<string, Template>`，key = **模板正文字符串本身** | 防 hash 碰撞渲染错模板（M7）；Template 不可变复用，`TemplateContext` 每渲染新建（沙箱配置随上下文）→ Singleton 线程安全 |
| **执行限制** | LoopLimit / RecursiveLimit / LimitToString / RegexTimeOut | 默认 Scriban 值，`PrintTemplatesOptions` 可调（C4） |
| **解析失败** | `Template.Parse` 有错 → 抛 `TemplateParseException` | 含解析消息列表（M6），严格失败优于静默产出错误单据 |

> **非进程级沙箱（官方明示）**：Scriban 沙箱不是进程级隔离——模板可访问的数据仅限经递归转换后暴露的字典内容。操作者可编辑模板须限定在**受信后台（管理 UI + 权限门控）**，不用于公开/不可信输入。

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 双实体（`PrintTemplate` + `PrintTemplateVersion`）SG1 声明式 + FreeSql 持久化，slnx/CPM 接线完成
- **Scriban 7.0.0** 唯一内置引擎（BSD-2-Clause，首个非存储类第三方运行时依赖，ADR 已裁定）
- 沙箱渲染：MemberFilter 白名单 + 无 TemplateLoader + 递归转换 + 解析缓存 + 执行限制
- 版本化门面：自动 minor 递增 + Draft/Active/Archived + 唯一约束 + 审计固定渲染
- 消费方一包可用：ProjectReference + `[TKWFEnabledExtension]` 白名单声明即接线（TryAdd 三件套）
- 测试/文档/审核：`Extension.PrintTemplates.Tests` 项目 + `ConsumerHostInitializer` 样板就绪（对齐 V4.9.85 消费方启用模型）；按开发方案 **29 用例**规划（Renderer 沙箱 8 / Store 7 / Manager 10 / Initializer 4）与 README/使用指南（本文）同步进行

### V0.2.0（候选，能力完善）
- 显式 SemVer 版本号指定（v0.1.0 仅自动 minor 递增；手动版本号允许并发小版本/补丁发布）
- 设计期 git-tracked 模板文件（embedded/VFS，对齐 ABP `VirtualFileTemplateContentContributor`）+ DB 覆盖模型（租户覆盖默认模板 + 权限门控）
- 模板启用/禁用管理（`IsEnabled` schema 列 + 管理 API，v0.1.0 schema 不含该列）
- 管理 UI（模板编辑界面——v0.1.0 提供 Manager API 供 UI 调用）
- 发布审计完善：`PublishedBy` 链路写入（接消费方当前用户上下文）
- 模板本地化（多语言模板正文）+ 模板导出/导入

### 远期 / 评估
- 自定义模板源抽象（`ITemplateSource`，对齐 ABP TextTemplating 模型）与多来源解析优先级
- Razor 开发者级模板独立可选包（ADR 决策 4：`IsSandboxed=false` + 权限门控，对齐 `Volo.Abp.TextTemplating.Razor`，**本期不实施**）
- 批量渲染性能评估（大单据多行项目吞吐瓶颈出现时，评估 Fluid 替代——ADR 记载可推翻条件：MIT 硬性要求或 11.4× 渲染差距成为瓶颈）
- `TextTemplates`（邮件正文）扩展复用 `{Key}@{Version}` 版本化模型（引擎选型独立评估）

---

## 八、THIRDPARTY (Third-Party Notices)

本扩展使用以下第三方软件（扩展首个**非存储类**第三方运行时依赖）：

| 组件 | 版本 | 许可 | 用途 |
|------|------|------|------|
| **Scriban** | 7.0.0 | **BSD-2-Clause** | 打印模板引擎——安全沙箱渲染（模板正文 = Scriban 语法；`MemberFilter` + 无 `TemplateLoader` + 执行限制） |

**Scriban**（Copyright © 2016-2024, Alexandre Mutel）——BSD 2-Clause 许可证（宽松许可，允许闭源商用衍生，须保留版权与许可声明）。许可证全文见 [Scriban GitHub 仓库](https://github.com/scriban/scriban)。本扩展经 ADR-PrintTemplates 裁定选型（安全沙箱 + AOT 兼容 + 零依赖 + ABP TextTemplating 默认引擎先例），按 CPM（`Directory.Packages.props`）锁定版本 7.0.0。

---

**文档信息**: V0.1.0 | 2026-09-06 | 关联：ADR-PrintTemplates-模板引擎选型与版本化.md、v0.1.0-PrintTemplates-打印模板引擎与版本化-开发方案.md（主框架私有）、[打印模板扩展-使用指南](../../docs/PrintTemplates/打印模板扩展-使用指南.md)