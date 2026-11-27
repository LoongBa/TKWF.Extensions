# TKWF.Ext.UI.Abstractions 表现层页面装配契约包技术规范

**状态**: 契约抽象包（零实体、零 SG1、零 Initializer、零 `[ApiExpose]`——对齐 Navigation.Abstractions 先例） | **版本**: V0.1.0（功能线，未 tag） | **框架**: .NET 10 | **依赖**: `TKWF.CodeGeneration.Abstractions`（`PageDescriptor`/`PageTypes` 数据形状——纯类型依赖，非分析器）

**归属判据**: ADR106 决策 1 / 框架 `D23-表现层页面装配-设计方案.md` §二 明示——`PageDescriptor`（数据形状）归主框架 `CodeGeneration.Abstractions`；`IPageContributor`（声明接口）归**表现层 Abstractions 包（外部 `_TKWF.Extensions` 仓）**，即本包。

---

## 一、定位

扩展模块「UI 装配面」的**唯一契约入口**——跨扩展共用一份，消费方自写贡献者亦引本包（不引任何重 UI 包）。

| 类型 | 说明 |
|------|------|
| `IPageContributor` | 页面贡献者契约：`IReadOnlyList<PageDescriptor> GetPages()`。实现即意图声明（A+ 模式，无标记特性）；实现类必须 `public` + `public` 无参构造（违反 → `TKWF_SG1b_PAGE002` Error）；**纯元数据函数**（幂等、无 IO/时钟/随机、无状态；禁 `yield`/LINQ——未来 SG 编译期解释方法体） |

namespace：`TKWF.Ext.UI`（包名带 `.Abstractions`、命名空间不带 —— 与 `TKWF.Ext.Navigation.IMenuContributor` 同规则）。

## 二、为什么是"单一共享包"

| 方案 | 结论 |
|------|------|
| 单一共享包（本包） | ✅ SG1b 按**接口短名**收集，检测与程序集无关；类型同一性由一份程序集保证，消费方多包共存零歧义 |
| 每扩展一份 `{Ext}.UI.Abstractions` | ❌ 类型身份碎片化——消费方同时引用两个 → `CS0433` 歧义，或两个同名接口同时命中短名扫描 |
| 塞进各 `{Ext}.UI.{TechStack}` RCL | ❌ 消费方（贡献者主场景）为一个单方法接口背重 RCL；跨技术栈重复接口 |

## 三、贡献者归属铁律（当前 SG 机制决定）

**合法**：① 消费方领域项目（主场景——SG1b 只收集**当前编译自身程序集**）；② 扩展主包（白名单扩展程序集——其 `GeneratedPageCatalog` 被 `AggregatePageExtensionCatalogs` 聚合）。

**禁止**：`{Ext}.UI.{TechStack}` RCL 内 —— RCL 不在领域项目编译内、也不在白名单聚合内（WASM 拓扑下 RCL 挂在 Client 项目），页面**静默不注册**。RCL 只承载组件 + 静态资产 + 契约表。

## 四、消费方式

```xml
<!-- 页面贡献者（消费方领域项目，或扩展主包） -->
<PackageReference Include="TKWF.Ext.UI.Abstractions" Version="0.1.0" />
```

```csharp
public sealed class WeChatOpsPageContributor : TKWF.Ext.UI.IPageContributor
{
    public PageDescriptor[] GetPages() =>            // 协变返回：数组字面量合法
    [
        new("wechat-ops.materials.list", "/admin/wechat-ops/materials", "wechatops.materials",
            "素材库", "file-text", "运营", PageTypes.Business,
            "WeChatOperations.Query", ""),
    ];
}
```

## 五、边界

- **不含** `[PageVisibleTo]`：该特性属未来 SG 结构化提取阶段（框架侧归属待定，天然位置在 `CodeGeneration.Abstractions` 的 `PageDescriptor` 旁）——现发会造成"没人读的声明" + 双属性歧义。过渡期 `RequiredPermissions` 用字面量 pipe 串。
- **永不引用** `TKWF.CodeGeneration` 分析器包（本包非扩展项目、不得产 Catalog/注册表）与 `Permissions.Abstractions`（权限键是描述符中的字面量字符串）。
- 契约语义（九字段冻结表 / 路由权限约定 / 诊断现状 / R2 上限 / 端点前置）唯一事实源：仓库 `docs/AE-Kit/skills/tkwf-extension-ui/references/page-contract.md`。

## 六、版本与发布

- MinVerTagPrefix：`UI.Abstractions/v`（独立版本线）
- V0.1.0（功能线，未打 tag）——首发 `IPageContributor`；tag 须征得用户同意（AGENTS §5），发布走 `workflow_dispatch`

<!-- EOF -->
