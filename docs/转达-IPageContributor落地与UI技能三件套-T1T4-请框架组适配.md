# 转达-IPageContributor 落地与 UI 技能三件套（T1–T4）— 请框架组适配

> **来源**：扩展模块组 ｜ **日期**：2026-11-28 ｜ **性质**：跨组对齐 + 四项请求
> **背景**：扩展仓落地表现层页面契约包与三个 UI 技能后，框架侧存在 4 处需要对齐/排期的点（1 项签名钉死、1 项已完成待复核、2 项请求排期）。
> **关联**：框架 `docs/D23-表现层页面装配-设计方案.md`、ADR106 决策 1；扩展仓 `_Framework/UI.Abstractions/`、`docs/AE-Kit/skills/{tkwf-extension-ui,tkwf-use-extension-ui,tkwf-ui-split-back}/`（AE-Kit canonical 位置）。

---

## T1【必办·签名钉死】`IPageContributor` 已落地，签名以扩展仓为准

**事实**：扩展仓已建 `TKWF.Ext.UI.Abstractions` 包（`net10.0`，`MinVerTagPrefix UI.Abstractions/v`，功能线 V0.1.0，未打 tag），内含：

```csharp
namespace TKWF.Ext.UI;
public interface IPageContributor
{
    IReadOnlyList<PageDescriptor> GetPages();   // PageDescriptor 仍来自 TKW.Framework.CodeGeneration
}
```

**裁定依据**：ADR106 决策 1 / D23 §二 已将接口归属"外部 `_TKWF.Extensions` 仓的表现层 Abstractions 包"——扩展仓为属主，签名一次定死（单向门：`IPageContributor` 落地后每个新页面都是实现者，改返回类型即破坏全部实现者）。

**请求框架组对齐的两处措辞**：

| 位置 | 现状 | 请改为 |
|---|---|---|
| `_Domain.SG/CodeGeneration.Abstractions/FrameworkTypes.cs` L55 附近注释 | "返回 PageDescriptor[]" | `IReadOnlyList<PageDescriptor> GetPages()`（并注明接口属主 = 扩展仓 `TKWF.Ext.UI.Abstractions`） |
| `docs/D23-表现层页面装配-设计方案.md` §三 C1 行 / §四 两阶段注记 | "返回 `PageDescriptor[]`" / "IPageContributor 接口本体在外部表现层 Abstractions 仓未落地" | 同上——已落地，签名 `IReadOnlyList<PageDescriptor> GetPages()` |

> 已完成（扩展仓侧主动纠偏）：AE-Kit `tkwf-extension` §4.11 的旧示例（`IEnumerable<PageDescriptor> CreatePages()` + `yield`）与"⚠️ 未落地"标注**已就地修正**为 canonical 签名 + 数组字面量 + 已落地说明；同文件 §4.12 自检行与 §6 CONTEXT 同步更新。请复核是否与框架侧理解一致。

---

## T2【已完成·请复核】框架 AE-Kit 三技能与漂移纠正

已在 canonical 位置新增三个技能（`.agents/skills/tkwf` 为 Junction，写入即生效）：

| 技能 | 定位 |
|---|---|
| `tkwf-extension-ui` | 扩展侧：UI 装配面决策（是否出 UI/能力域拆分/命名/包形态/页面契约冻结/路由权限/端点前置/贡献者落点/拆回前置） |
| `tkwf-use-extension-ui` | 消费侧：RCL 引用 + 版本锁 + 白名单拓扑（UI 选择 ≠ 后端白名单集合）+ Blazor WASM/Server 双宿主 + 真实范围验证 + 排障 |
| `tkwf-ui-split-back` | 回收评审：六标准 + 验收 + 7 步 runbook + 反模式 |

契约单一事实源：`tkwf-extension-ui/references/page-contract.md`（`skills/README.md` 已登记为**唯一允许的跨技能共享文件**）。

**同期纠正的两处框架侧事实性偏差**（请复核）：

1. `tkwf-extension` §4.11/§4.12/§6：旧签名 + "未落地" → 已落地 + canonical 签名。
2. `tkwf-extension` §4.11 页面门控段：原表述把 `PAGE002-004` 当作整组生效门控；实际 **`PAGE003`（路由冲突）/`PAGE004`（资源键冲突）已定义但未接线**（触发依赖页面描述数据，见 `Guard.cs` L1061 与 D23 §四）。已改为 **PAGE002 ACTIVE / PAGE003·004 DORMANT**，并注明当前冲突防护 = 契约冻结 + code-reviewer 门禁。

`skills/README.md` 总表三行实测状态暂标"未实测（首发）"——首次真实切片跑通后回写。

---

## T3【请求排期】结构化页面数据提取阶段（四问）

该阶段是 PAGE003/004 从 DORMANT 转为 ACTIVE、`[PageVisibleTo]` 落地、运行时 resolver/loader/guard 接线的前提。请排期时确认：

| # | 问题 | 扩展仓当前立场 |
|:-:|---|---|
| a | SG 是否**编译期解释 `GetPages()` 方法体**？ | 已在 `page-contract.md` §1 立纪律：纯元数据函数、数组/集合字面量、禁 `yield`/LINQ、无 IO/时钟/随机。请确认该纪律与提取实现匹配（不匹配请早告知，避免全部实现者返工） |
| b | 描述符 `RequiredPermissions` 是否与**同编译 `[RequirePermission]` 字面量**做编译期校验？ | 当前无校验（权限键是字面量字符串，无常量类）：`PermissionNames` 仅 `Admin.All`，扩展门面用字面量。若能加 PAGE 族诊断（键不存在 → Error/Warning），价值高 |
| c | `Title`/`Group` 是否赋予**本地化键**语义？ | 扩展仓已冻结为**显示字面量**并禁止自造 `$key` 约定（避免无人解析的伪契约）。若后续键化，属"数据替换"（零签名变更），可平滑迁移 |
| d | `[PageVisibleTo]` 落地归属？ | 扩展仓**不发**（避免与未来框架版双属性歧义）。建议归属 `CodeGeneration.Abstractions`（`PageDescriptor` 旁）——因提取 SG 需在**可能不引用扩展仓包的编译**（如 RCL）中看到它 |

---

## T4【请求排期】页面目录读出端点（`/_tk/pages`）

`PageAssemblyWebExtension.DeclareEndpoints` 当前为空（"占位——本批不挂载"）。后果：消费方与技能**无法在运行时读出** `GeneratedPageRegistrations`（聚合目录），`tkwf-use-extension-ui` 的"路由/权限核对"只能靠静态表 + API 403 语义，无法工具化。

请在端点注册表所有权落定后优先安排该 metadata 端点（或等价诊断读出）。在此之前，消费侧技能已明确标注"catalog 级验证待框架"，不会虚报。

---

## 回执要求

- T1：确认签名钉死 → 我方按此发布 `UI.Abstractions/v0.1.0`（tag 待用户同意）。
- T2：复核两处漂移纠正是否正确（如有异议请指出）。
- T3/T4：给出是否排期 / 时间窗，或明确"暂不"（我方据此调整 `page-contract.md` §8 待定项表述）。
