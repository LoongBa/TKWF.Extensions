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
