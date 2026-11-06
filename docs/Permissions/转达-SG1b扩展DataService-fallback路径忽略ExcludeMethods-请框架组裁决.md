# 转达：SG1b 扩展 DataService「TUser 推断失败 fallback 路径」忽略 ExcludeMethods/IncludeMethods（请框架组裁决）

> **日期**：2026-11-06
> **提交方**：TKWF.Extensions（扩展组）
> **性质**：框架缺陷转达（SG1b `ControllerGenerator` 生成器判据缺口）
> **优先级**：中（扩展侧有 `[GenerateIgnore]` 绕行方案，不阻塞；影响所有依赖 ExcludeMethods 收敛暴露面的非泛型扩展 DataService）

---

## 一、现象

扩展 DataService 上的 `[GenerateController(FromDataService = true, ExcludeMethods = new[] { ... })]` **不生效**：

- 扩展侧生成的 ExtI 契约接口（`{扩展根Ns}.I{X} : IDomainService`）仍包含被排除的方法；
- 消费方 SG1b 从 ExtI 接口提取方法生成控制器/装饰器/Meta，同样包含被排除的方法；
- 结果：依赖 `ExcludeMethods` 收敛 REST/GraphQL 暴露面的场景失效（例：Permissions `GetGrantedNamesByProviderKeyAsync` 返回 `Dictionary<string, HashSet<string>>`（GraphQL 不可映射）→ 消费方 `GEN007` error 编译阻塞）。

## 二、根因（代码定位）

`_Domain.SG/CodeGeneration/ControllerGenerator.cs` 存在两条方法提取路径：

| 路径 | 代码位置 | ExcludeMethods/IncludeMethods | GenerateIgnore |
|------|---------|------------------------------|----------------|
| 主路径（TUser 推断成功） | L752-800 `if (isDataService)` 分支 | ✅ 按优先级链过滤（GenerateIgnore → ExcludeMethods → GenerateControllerMethod → IncludeMethods） | ✅ |
| **fallback 路径（TUser 推断失败）** | **L706-730** | **❌ 完全忽略** | ✅（唯一生效项） |

fallback 路径片段：

```csharp
if (fbIsDataService)
{
    fbMethods = ServiceMethodExtractor.ExtractMethods(typeSymbol,
        includeAsyncMethodsOnly: true,
        methodPredicate: method =>
            !(method.GetAttributes().Any(a =>
                a.AttributeClass?.Name == GenerateMethodIgnoreAttributeName)));
}
```

**扩展 DataService（非泛型 `DomainServiceBase`——扩展需兼容任意消费方 TUserInfo）在扩展自身编译中必然无具体 TUser → 恒走 fallback 路径** → `ExcludeMethods`/`IncludeMethods` 全部失效，仅 `[GenerateIgnore]` 生效。

## 三、实证（Permissions V0.9.7 修复过程）

1. `PermissionGrantEntityDataService` 特性值经 ALC 反射确认正确：`ExcludeMethods = ["CreateAsync","UpdateAsync","DeleteAsync","GetGrantedNamesByProviderKeyAsync"]`
2. `Permissions` 项目**清空 obj/bin + `dotnet build-server shutdown` 后重建**：ExtI.g.cs（2783 字节）仍含全部 7 方法（未被排除）
3. 消费方（`Extension.Permissions.Consumer.Tests`，ProjectReference 源码模式，清 obj/bin 重建）控制器生成物同样含被排除方法 → 与 DMP-Lite 侧 `GEN007` 实证一致
4. **改用 `[GenerateIgnore]` 后同一流程复验**：ExtI 接口 7→6 方法、消费方控制器 0 命中 → 证实 fallback 路径只认 GenerateIgnore

## 四、影响面

所有「非泛型扩展 DataService + `[GenerateController(FromDataService=true)]` + 依赖 `ExcludeMethods`/`IncludeMethods` 收敛暴露面」的扩展（Permissions / AuditLogging / FeatureManagement 等管理 API 扩展统一样式）。

## 五、建议修复

fallback 路径的 `methodPredicate` 对齐主路径（复用一个提取函数，消除双推导链）：

```
[GenerateIgnore] → 强制排除
ExcludeMethods 模式匹配（含 * 通配符，复用 MatchesAnyPattern）→ 排除
[GenerateControllerMethod] → 包含
IncludeMethods 模式匹配 → 包含
否则 → 排除（V4.9.40 CRUD 白名单收紧语义）
```

## 六、绕行方案（扩展侧已采用，不阻塞）

受影响方法标 `[GenerateIgnore]`（fallback 路径明确支持、优先级最高）；同时保留 `ExcludeMethods` 项作双保险（主框架修复 fallback 后两路径语义一致）。

Permissions V0.9.7（tag `Permissions/v0.9.7`）已按此落地：`GetGrantedNamesByProviderKeyAsync` 标 `[GenerateIgnore]`，ExtI 7→6 方法、控制器 0 命中、Permissions 51 + Consumer 5 测试全绿。

---

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-06 | 初稿：fallback 路径忽略 ExcludeMethods/IncludeMethods 缺陷定位（代码位置 + ALC 特性实证 + 清缓存重建复验 + GenerateIgnore 对照实证） |

---

## 七、闭环结论（框架组，2026-11-06，v4.10.113 / ADR129）

**已根治**（方案 A：补全过滤链 + 单源实现 + 保留默认纳入；不绕行、不兼容）。

| 项 | 结论 |
|---|---|
| 根因 | 转达定位准确（fallback 只判 `[GenerateIgnore]`）。框架侧另发现两处更深缺口：① fallback「默认纳入」vs 主路径「默认排除」（V4.9.40）——同一 DataService 因 TUser 可推断性产生两套契约；② fallback 未传 `reportDiagnostic`，且扩展编译分支在诊断上报循环前 `return`（`TKWCTRL007` 从不发射） |
| 修复 | 谓词单源 `BuildDataServiceMethodPredicate`（优先级 `GenerateIgnore > ExcludeMethods > GenerateControllerMethod > IncludeMethods > 默认`）；fallback = 默认纳入、主路径 = false（行为逐字节不变）；`MatchesAnyPattern` 上提共用；诊断链路补齐（含扩展分支上报点）；**Service 目标 `[GenerateIgnore]` 一并修复**（此前两路径均 no-op） |
| 已知边界（ADR129） | fallback 不补充基类 CRUD（逃生通道 = 扩展内声明同名 `new` 转发方法；`override` 会被提取器跳过）；Service 目标的 `ExcludeMethods`/`IncludeMethods` 仍为 no-op；扩展分支 `allExtTargets` 不含引用程序集中非扩展程序集的 B2 目标（诊断仍不上报，预存） |
| 扩展侧动作 | CPM lockstep ≥ **4.10.113** → 重建；`GetGrantedNamesByProviderKeyAsync` 可从 `[GenerateIgnore]` 回退为纯 `ExcludeMethods` 声明（双保险可留，但不再必需）；若存在「剥 Async 后同名」方法，重建后会新增 `TKWCTRL007` Warning（`TreatWarningsAsErrors` 需处理） |
| 验证 | `Domain.SG.Tests` **328/328**（+14：F1~F14）、`Domain.ApiService.SG.Tests` 163/163、`Domain.ApiClient.SG.Tests` 72/72、slnx Release 0 警告 0 错误；变异 ×5 全拦截 |

文档：`_TKWF/docs/02-迭代开发/ADR129-SG1b扩展DataService方法提取语义-单源过滤链与默认纳入策略.md`、`V4/v4.10.113-*-{开发方案,审核报告}.md`、CHANGELOG v4.10.113。
