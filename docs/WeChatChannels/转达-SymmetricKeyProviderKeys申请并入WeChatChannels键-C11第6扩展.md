# 转达 · SymmetricKeyProviderKeys 申请并入 WeChatChannels 键（C11 第 6 扩展边界——原 WeChatOperations 键被拆分取代）

> **转达方**：TKWF 扩展模块组（WeChatChannels 通道底座）
> **接收方**：主框架（TKW.Framework——Core/KeyManagement）
> **日期**：2026-11-05
> **状态**：⏳ 待框架组并入 → 扩展侧 CPM lockstep 切换收口（当前扩展侧走过渡常量，已随 WeChatChannels v0.1.0 发布）
> **关联**：`docs/授权面/转达-SymmetricKeyProviderKeys申请并入AuthSurface键-C11第4扩展.md`（第 4 扩展先例）、`docs/TrustCenter/转告-SymmetricKeyProviderKeys申请并入TrustCenter键.md`（第 5 扩展先例）、`docs/WeChatChannels/微信通道底座扩展拆分-开发方案.md`（§5.1a / §四 S3）

## 背景

主框架 `SymmetricKeyProviderKeys`（`_TKWF/_Framework/Core/KeyManagement/SymmetricKeyProviderKeys.cs`，V4.10.61 E4 密钥管理配套）C11 取舍边界：扩展需 keyed 时由本类加 const（框架变更）。

当前常量类 5 键：`AuthCenter` / `Federation` / `Mfa` / `AuthSurface`（v4.10.69 并入）/ `TrustCenter`（v4.10.70 并入）。

**C11 第 6 扩展边界事项变更**：`WeChatOperations` 原以自建常量 `WeChatOperationsKeyProviderKeys`（值 `"WeChatOperations"`）提报并入第 6 项，但**该并入从未执行**（主框架无 `WeChatOperations` 键）。2026-11-05 `WeChatOperations` V0.9.0 拆分——通道面（凭证加解密）下沉新扩展 `WeChatChannels`，运营扩展不再持有密钥键。**因此第 6 项不再需要 `WeChatOperations` 键，改为申请 `WeChatChannels` 键**（键名随属主变更，避免留下双键）。

## 转达内容

**申请主框架**在 `SymmetricKeyProviderKeys` 类加：

```csharp
/// <summary>WeChatChannels 扩展注册键（值 "WeChatChannels"——微信通道底座，C11 第 6 扩展边界；
/// 替代原 WeChatOperations 并入事项——V0.9.0 拆分后运营扩展不再持有密钥键）。</summary>
public const string WeChatChannels = "WeChatChannels";
```

**扩展侧当前形态（已发布 v0.1.0，过渡路径）**：

- `WeChatChannelsKeyProviderKeys.WeChatChannels`（值 `"WeChatChannels"`）——位于 `WeChatChannels.Abstractions`（键常量单源，跨主包/运营扩展共享）
- keyed 注册：`AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(WeChatChannelsKeyProviderKeys.WeChatChannels, ...)`（`WeChatChannelsExtensionInitializer`）
- 5 处消费点统一该键：
  - 通道底座：`ChannelConfigService`（加密落库）/ `DbWeChatCredentialResolver`（解密出站）/ `WeChatChannelRegistrySource`（投影解密）
  - 运营扩展：`MessageGatewayService`（下游端点令牌加密）/ `WeChatMessageForwardJob`（出站解密）——**跨扩展消费同一键**（同一密钥文件、同一信任域；依赖方向：运营扩展依赖通道底座已注册 keyed provider，消费方双白名单保证）
- 原 `WeChatOperationsKeyProviderKeys.cs` **已删除**（全仓无代码引用）

**CPM 升级后扩展侧切换（框架并入 + 发布后执行）**：

1. CPM 13 包 lockstep 升级至含 `SymmetricKeyProviderKeys.WeChatChannels` 的框架版本
2. 删 `WeChatChannelsKeyProviderKeys.cs`（Abstractions 包）
3. 5 处代码点切 `SymmetricKeyProviderKeys.WeChatChannels`（keyed 注册 key 参数 + 4 处 `[FromKeyedServices]`；`WeChatChannels.Abstractions` 需按需引 `TKWF.Domain`——已引）
4. 同步 XML doc cref / 两包 README / 使用指南 / 变更记录

## 一致性约束

- **键值必须为 `"WeChatChannels"`**（框架并入值须与扩展过渡常量完全一致——零行为差异，不触发密钥重建/数据迁移，对齐 AuthSurface v4.10.69 先例）
- 不得保留 `WeChatOperations` 键（已废弃；运营扩展无密钥消费属主）
- `[FromKeyedServices]` 注入 + 守卫工厂 `ActivatorUtilities` 对 keyed 参数生效（框架 `GuardFactoryKeyedEndToEndTests` C4 已实证）

## 时序

1. 框架组并入 + 发版
2. 扩展侧 CPM lockstep + 删过渡常量切换
3. 验证：slnx 0 错误 + WeChatChannels 15 + WeChatOperations 206 全绿
4. 发布 WeChatChannels.Abstractions / WeChatChannels / WeChatOperations 补丁版本（或随下次迭代）

<!-- EOF -->
