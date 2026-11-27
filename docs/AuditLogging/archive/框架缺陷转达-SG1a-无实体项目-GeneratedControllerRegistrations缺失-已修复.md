# 框架缺陷转达：SG1a 无实体项目 + [GenerateController] → `GeneratedControllerRegistrations` 缺失

> **状态：已修复并归档（框架 v4.10.147，2026-11-27）**——缺陷已由框架修复（"无实体 + `[GenerateController]` 门面"为受支持形态）；`TKWF.Ext.AuditLogging.Web` 已回迁独立包（`AuditLogging.Web/v0.1.0`，主包 `AuditLogging/v0.9.0` 移除 AspNetCore 依赖）。本文保留作历史记录，不再作为待办。

> 提交方：TKWF 扩展模块组（扩展仓 `F:\LoongBa_Git\_TKWF.Extensions`）
> 接收方：主框架组（`F:\LoongBa_Git\_TKWF`）
> 发现日期：2026-11-27
> 严重度：**阻断**（无法创建"无实体 + `[GenerateController]`"的 Web 扩展包）
> 框架版本：本地 feed `TKWF.CodeGeneration 4.10.113-local`（扩展仓 CPM 锁定版本）

---

## 一、现象

新建一个**无领域实体**的 Web 扩展包（net10.0 classlib + `FrameworkReference Microsoft.AspNetCore.App` + `[assembly: ExtensionModule(...)]`），内含一个 `[GenerateController]` 门面实现类 + `[ApiExpose]` 门面接口（无任何 `[DomainGenerateCode]` 实体），编译失败：

```
error CS0234: 命名空间"TKW.Framework.SourceGen"中不存在类型或命名空间名"GeneratedControllerRegistrations"
  (_ProjectMetaContext.g.cs(28,31) / (40,31))
```

同时伴随警告：

```
warning TKWF_SG1a_WRN_001: SG1a: EntityMetadataGenerator 未扫描到任何领域类型。
```

---

## 二、最小复现

1. `dotnet new classlib -f net10.0`，项目名如 `TKWF.Ext.AuditLogging.Web`。
2. csproj 引用：
   - `TKWF.Domain`、`TKWF.Domain.Web`、`TKWF.CodeGeneration.Abstractions`（双模式 DLL/Package）
   - `TKWF.CodeGeneration`（`PrivateAssets="all"`，纯 Analyzer）
   - `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
3. `AssemblyInfo.cs`：
   ```csharp
   using TKW.Framework.CodeGeneration;
   [assembly: ExtensionModule("AuditLogging.Web", "audit-logging")]
   ```
4. 门面接口（无实体）：
   ```csharp
   [ApiExpose(Alias = "business-audit")]
   public interface IBusinessAuditWebApiService : IDomainService
   {
       [ApiExpose(RestRoute = "/business-audit/query", RestMethod = "POST")]
       Task<BusinessAuditPagedResult> QueryAsync(BusinessAuditQueryInput input, CancellationToken ct = default);
   }
   ```
5. 实现类：
   ```csharp
   [GenerateController]
   public sealed class BusinessAuditWebApiService : DomainServiceBase, IBusinessAuditWebApiService { ... }
   ```
6. `dotnet build` → 上述 CS0234。

已排除：`TKWFRole=Client`、删除 `obj/`+`bin/` 全量重建、补 `[assembly: ExtensionModule]`、补全部引用 —— 均**无效**。

---

## 三、证据（生成物）

### 3.1 SG1a 生成的 `_ProjectMetaContext.g.cs`（无实体 → 回退分支）

```csharp
private ProjectMetaContext()
{
    // 控制器元数据由 SG1b (ControllerGenerator) 生成的 GetMetadatas() 提供
    foreach (var m in global::TKW.Framework.SourceGen.GeneratedControllerRegistrations.GetMetadatas())
        _allMetadatas.Add(m);
}
...
public override IEnumerable<DomainServiceRegistration> GetServiceRegistrations()
{
    // 由 SG1b (ControllerGenerator) 生成的控制器注册
    foreach (var r in global::TKW.Framework.SourceGen.GeneratedControllerRegistrations.GetServiceRegistrations())
        yield return r;
    yield break;
}
```

### 3.2 SG1b 实际生成的类型（无 `GeneratedControllerRegistrations`）

`_GeneratedControllerCatalog.g.cs`：`public static class GeneratedControllerCatalog`
`_GeneratedEndpointRegistrations.g.cs`：`public static class GeneratedEndpointRegistrations`（含 `BusinessAuditWebApiServiceEndpointRegistration` 与 `Map<TUserInfo>(facadeType, group)`）

→ SG1a 引用的 `GeneratedControllerRegistrations` **从未被 SG1b 发射**。

### 3.3 有实体项目对照（正常）

同仓 `_Framework/AuditLogging`（有 `[DomainGenerateCode]` 实体）的 `_ProjectMetaContext.g.cs` 直接列实体 Meta，不引用 `GeneratedControllerRegistrations`，构建 0 错误 → 缺陷仅在"**无实体**"时触发。

---

## 四、根因判断

SG1a（EntityMetadataGenerator）在"未扫描到任何领域实体"时走 fallback 模板，硬编码引用 SG1b 的 `GeneratedControllerRegistrations`（`GetMetadatas`/`GetServiceRegistrations`），但当前 SG1b（ControllerGenerator）**只发射** `GeneratedControllerCatalog` + `GeneratedEndpointRegistrations`，名称/契约不一致 → CS0234。

（有实体时 SG1a 走实体枚举分支，永不触发该引用，故既有扩展全部未暴露此缺陷。）

---

## 五、影响

- **无法创建"无实体 Web 扩展包"**：业务读面/查询面扩展（不需要自己的实体，只消费他扩展契约）被迫走 in-package Web 扩展（主包被迫 `FrameworkReference Microsoft.AspNetCore.App`）。
- 本仓 v0.8.0 的 `TKWF.Ext.AuditLogging.Web` 独立包即因此**放弃**，改为在 `TKWF.Ext.AuditLogging` 主包内实现 Web 扩展（主包新增 AspNetCore.App 依赖，非 Web 消费方也被动引用）。
- 同类形态（Permissions/Identity/Notifications 等只读 Web 面）未来同样受限。

---

## 六、请求修复

任选其一（建议 1）：

1. **SG1a fallback 不引用未生成类型**：无实体时 `_allMetadatas` 留空、`GetServiceRegistrations()` 返回空集合（不引用 `GeneratedControllerRegistrations`）。
2. **SG1b 对齐发射** `GeneratedControllerRegistrations`（实现 `GetMetadatas()` / `GetServiceRegistrations()`，与 SG1a 引用契约一致）。

修复验收：上述最小复现项目 `dotnet build` 0 错误；有实体项目不受影响（回归）。

---

## 七、当前绕过与回迁条件

- **绕过（已实施，v0.8.0）**：Web 扩展 in-package 到有实体的 `TKWF.Ext.AuditLogging` 主包（`AuditLoggingWebExtension` + `IBusinessAuditWebApiService`/`BusinessAuditWebApiService`），主包加 `FrameworkReference Microsoft.AspNetCore.App`。
- **回迁条件**：框架修复并发布新 `TKWF.CodeGeneration` 包后：
  1. 新建 `TKWF.Ext.AuditLogging.Web`，迁回 `IBusinessAuditWebApiService`/`BusinessAuditWebApiService`/`AuditLoggingWebExtension`（仅引 `AuditLogging.Abstractions`，L2 合规）；
  2. 移除主包 `FrameworkReference Microsoft.AspNetCore.App` 与 `TKWF.Domain.Web` 依赖；
  3. 打 `AuditLogging.Web/v0.1.0` + `AuditLogging/v0.9.0`（破坏性：移除主包 Web 依赖）。

---

## 八、附：本仓相关文件

- 绕过实现：`_Framework/AuditLogging/AuditLoggingWebExtension.cs`、`IBusinessAuditWebApiService.cs`、`BusinessAuditWebApiService.cs`
- 主包 csproj：`_Framework/AuditLogging/TKWF.Ext.AuditLogging.csproj`（`FrameworkReference` 行）
- 方案记录：`docs/AuditLogging/v0.7.0-审计契约收口与统一读模型-开发方案.md` §12
