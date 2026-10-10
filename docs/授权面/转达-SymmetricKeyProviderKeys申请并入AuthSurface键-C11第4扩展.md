# 转达 · SymmetricKeyProviderKeys 申请并入 AuthSurface 键（C11 第 4 扩展）

> **转达方**：TKWF 扩展模块组（AuthSurface 授权面）
> **接收方**：主框架（TKW.Framework——Core/KeyManagement）
> **日期**：2026-10-09
> **状态**：✅ **已闭环（2026-10-10——v4.10.69 并入 + 扩展侧适配完成）**；框架组回复见主框架 `docs/03_扩展模块/转达/转告回复-SymmetricKeyProviderKeys已并入AuthSurface键-v4.10.69.md`

## 背景

主框架 `SymmetricKeyProviderKeys`（`_TKWF/_Framework/Core/KeyManagement/SymmetricKeyProviderKeys.cs`，V4.10.61 E4 密钥管理配套）**C11 取舍边界**明示：

> 主框架 Core 承载扩展名常量是主框架首次扩展业务名常量……**第 4 个扩展需 keyed 时 → 本类加 const（框架变更）**。

当前常量类仅 3 键：`AuthCenter` / `Federation` / `Mfa`。

## 转达内容

AuthSurface（授权面）V0.2.0 **核验场景**（兑换码附加信息 `RedemptionCodeEntity.PayloadEncrypted`——AES-GCM 单段密文落库）需 keyed `ISymmetricKeyProvider`：

- 扩展侧已**先行自建常量** `AuthSurfaceKeyProviderKeys.AuthSurface`（值 `"AuthSurface"`，见 `_Framework/AuthSurface/AuthSurfaceKeyProviderKeys.cs`）注册 `AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>`（Initializer）——本地自洽、已发布（nuget.org 0.2.0）。
- **申请主框架**在 `SymmetricKeyProviderKeys` 类加：
  ```csharp
  /// <summary>AuthSurface 扩展注册键（值 "AuthSurface"，与转达严格一致）。</summary>
  public const string AuthSurface = "AuthSurface";
  ```
- **CPM 升级后扩展侧切换**：删 `AuthSurfaceKeyProviderKeys` 自建常量 → `SymmetricKeyProviderKeys.AuthSurface`（单一事实源，防 typo 静默解析错 key）。

## ✅ 适配闭环（2026-10-10）

- 框架组已并入（v4.10.69，`SymmetricKeyProviderKeys.AuthSurface`，密钥值 `"AuthSurface"` 与扩展侧先行常量完全一致——零行为差异）
- 扩展侧已完成：**CPM 13 包 lockstep 4.10.68 → 4.10.69**；**删 `AuthSurfaceKeyProviderKeys.cs`**；**2 处代码点切 `SymmetricKeyProviderKeys.AuthSurface`**（`AuthSurfaceExtensionInitializer` AddKeyedSingleton key 参数 + `RedemptionCommandService` `[FromKeyedServices]`）+ XML doc cref + README 同步
- **零破坏性**：常量值不变，不触发密钥重建/数据迁移（v4.10.69 发布物已上架，消费方零中断）

## 一致性约束

- 键值必须为 `"AuthSurface"`（与 E4 转告语义严格一致，防跨扩展同容器 last-wins 错键）。
- 对齐先例：`[FromKeyedServices]` 注入（`RedemptionCommandService` ctor）+ 守卫工厂 `ActivatorUtilities` 对 keyed 参数生效（框架 `GuardFactoryKeyedEndToEndTests` 已实证 C4）。

<!-- EOF -->
