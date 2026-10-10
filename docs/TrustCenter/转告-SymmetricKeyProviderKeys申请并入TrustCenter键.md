# 转告 · SymmetricKeyProviderKeys 申请并入 TrustCenter 键（C11 第 5 扩展）

> **转告方**：TKWF 扩展模块组（TrustCenter 信任中心）
> **接收方**：TKW.Framework 框架组（Core/KeyManagement）
> **日期**：2026-10-10
> **状态**：✅ **已闭环（v4.10.70）**——框架组已并入 + 本地部署根验证；扩展侧 CPM 升级 4.10.70 + 删过渡常量 + 3 处代码点切换主框架常量完成（2026-10-10）
> **先例**：AuthSurface v4.10.69 已闭环（C11 第 4 扩展边界）——框架组回复见主框架 `docs/03_扩展模块/转达/转告回复-SymmetricKeyProviderKeys已并入AuthSurface键-v4.10.69.md`

## 背景

主框架 `SymmetricKeyProviderKeys`（`_TKWF/_Framework/Core/KeyManagement/SymmetricKeyProviderKeys.cs`，V4.10.61 E4 密钥管理配套）**C11 取舍边界**明示：

> 主框架 Core 承载扩展名常量是主框架首次扩展业务名常量……**第 4 个扩展需 keyed 时 → 本类加 const（框架变更）**。

当前常量类 4 键：`AuthCenter` / `Federation` / `Mfa` / `AuthSurface`（v4.10.69 并入，第 4 扩展边界兑现）。**TrustCenter 为第 5 个扩展**——C11 边界明文预授权，走 AuthSurface 同款并入路径。

## 转告内容

TrustCenter（信任中心，2026-10-09 自 Federation 剥离）需 keyed `ISymmetricKeyProvider`（AccessCode Payload 加密 / SsoClient credential / HMAC 加密）：

- 扩展侧已**先行自建过渡常量** `TrustCenterKeyProviderKeys.TrustCenter`（值 `"TrustCenter"`，见 `_Framework/TrustCenter/TrustCenterKeyProviderKeys.cs`）——已用于：
  - `TrustCenterExtensionInitializer.cs`：`AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(TrustCenterKeyProviderKeys.TrustCenter, ...)`
  - `SsoClientService.cs`：ctor `[FromKeyedServices(TrustCenterKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider keys`
  - `AccessCodeService.cs`：ctor `[FromKeyedServices(TrustCenterKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider keys`
- **申请主框架**在 `SymmetricKeyProviderKeys` 类加（第 5 扩展边界）：
  ```csharp
  /// <summary>TrustCenter 扩展注册键（值 "TrustCenter"，与转告严格一致）。</summary>
  public const string TrustCenter = "TrustCenter";
  ```
  ✅ **已并入（v4.10.70）**——部署根 `TKWF.Core.dll`（2026-10-10 11:38 构建）反射确认含 `TrustCenter` 字符串。
- **✅ CPM 升级后扩展侧切换（2026-10-10 完成）**：删 `TrustCenterKeyProviderKeys` 过渡常量类 → 全量 `SymmetricKeyProviderKeys.TrustCenter`（单一事实源，防 typo 静默解析错 key）；3 处代码点字面量替换（`TrustCenterExtensionInitializer` keyed 注册 / `SsoClientService` / `AccessCodeService` `[FromKeyedServices]` 注入）+ 5 处注释同步清理——常量值不变（`"TrustCenter"`），零行为差异（全量 1913 用例 0 失败验证）。

## 适配清单（供框架组，对齐 AuthSurface v4.10.69 回复格式）

| # | 事项 | 依据/落点 | 状态 |
|---|------|----------|------|
| 1 | `SymmetricKeyProviderKeys` 追加 `public const string TrustCenter = "TrustCenter"` | C11 第 5 扩展边界明文预授权；值严格对齐扩展侧过渡常量（无 Mfa 式名/值大小写差异） | ✅ v4.10.70 已并入 |
| 2 | KeyedIsolationTests 扩 key 数（4 → 5） | C6 隔离语义闭环（AuthSurface 先例：`ThreeKeys_…` → `FourKeys_…` 全绿） | ✅ 框架组完成 |
| 3 | ADR96 §三「SymmetricKeyProviderKeys 扩展名耦合取舍」追加「✅ 边界兑现（v4.10.70）」记录 | 不新开 ADR——同 AuthSurface 先例 | ✅ 框架组完成 |
| 4 | 落点 `TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys`（Core 目录） | 与扩展侧 XML `<see cref>` 引用一致，零新增 using | ✅ 已落点 |

## ⚠️ 破坏性变更/迁移

**无**。常量值 `"TrustCenter"` 与扩展侧过渡常量**完全一致**——CPM 升级只影响编译期引用，**不触发密钥重建、无运行时数据迁移**。已发布/本地注册键值不变，消费方零中断。

## 发布时序（用户裁定 2026-10-10，✅ 已执行）

**等框架组适配发布后**（本地 refs 同步 → 扩展侧 CPM 升级 + 3 处字面量切换 + 删过渡常量）→ **统一发布**（TrustCenter V0.1.0 新线 + 相关扩展联动）。

**✅ 执行记录（2026-10-10）**：框架组 v4.10.70 发布 + 部署根同步 → 扩展侧 CPM 4.10.69→4.10.70 全量 lockstep → 删 `TrustCenterKeyProviderKeys` 过渡类 → 3 处代码点 + 5 处注释切 `SymmetricKeyProviderKeys.TrustCenter` → slnx 构建 0 错误 + 全量 1913 用例 0 失败 → 本地全量部署刷新（`_PushToRefs`）→ **统一发布待用户同意打 tag**（TrustCenter V0.1.0 / Federation V0.5.0 / AuthCenter V0.9.x + unlist `TKWF.Ext.Federation` 旧包线）。

---

## 附：TKWF.Ext.Federation 旧包 unlist 清单（✅ 已执行 2026-10-10）

> **背景**：TrustCenter 剥离（2026-10-09）后，`TKWF.Ext.Federation` 主包（联邦互联）已被 `TKWF.Ext.TrustCenter` 取代——Phase 0 用户裁定旧包版本线 unlist；**2026-10-10 用户裁定"全部 10 版本 unlist（含 0.5.0 连接层壳）——不留历史包袱，不必考虑兼容和迁移"**。

**✅ 已全部 unlist（2026-10-10，经 `dotnet nuget delete` = nuget.org unlist 语义）**：

| # | 版本 | 状态 |
|---|------|------|
| 1 | 0.2.1-preview.0.12 | ✅ unlist |
| 2 | 0.2.1-preview.0.15 | ✅ unlist |
| 3 | 0.2.1 | ✅ unlist |
| 4 | 0.3.0 | ✅ unlist |
| 5 | 0.3.1-preview.0.4 | ✅ unlist |
| 6 | 0.3.1-preview.0.7 | ✅ unlist |
| 7 | 0.4.0 | ✅ unlist |
| 8 | 0.4.1-preview.0.1 | ✅ unlist |
| 9 | 0.4.1-preview.0.7 | ✅ unlist |
| 10 | 0.5.0（连接层壳） | ✅ unlist |

- **验证**：nuget.org 搜索 `TKWF.Ext.Federation` 已不可见（新安装不再选）；flatcontainer 版本仍可列举（unlist 不删包——既有依赖解析兼容，消费方零破坏）
- **8 平台库 `TKWF.Federation.{平台}` 保留不 unlist**：`WeChat` / `QQ` / `DingTalk` / `WeCom` / `Oidc` / `Google` / `Microsoft` / `Alipay`（平台网关库独立大使馆资产，`ISsoChannel` 集合经平台库扩展方法装配，持续演进）——2026-10-10 已打 tag 发布新版（v0.2.2 ×7 + Alipay v0.1.1，引目标 TrustCenter.Abstractions 收口）
- 新线 `TKWF.Ext.TrustCenter` V0.1.0 已发布（nuget.org 实证上架）——消费方改引新线

<!-- EOF -->
