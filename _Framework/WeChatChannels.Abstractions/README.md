# TKWF.Ext.WeChatChannels.Abstractions 微信通道底座契约包技术规范

**状态**: 契约抽象包（零实体、零 SG1、零 Initializer——对齐 Federation.Abstractions 先例） | **版本**: V0.1.1（2026-11-05 密钥键收口；首发 V0.1.0） | **框架**: .NET 10 | **依赖**: `TKWF.Domain`（IDomainService）+ `TKWF.CodeGeneration.Abstractions`（[ApiExpose]）+ `TKWF.Ext.Permissions.Abstractions`（[RequirePermission]）

---

## 一、定位

微信通道底座的**跨扩展消费契约**（L2 门控：扩展间只引 Abstractions，不引实现主包——`TKWF0022`）：

| 类型 | 说明 |
|------|------|
| `IChannelConfigService : IDomainService` | 通道配置门面契约——`GetAsync(appId)` / `GetByChannelIdAsync(channelId)` / `ListChannelsAsync()` / `SaveChannelAsync(SaveChannelRequest)` / `DeleteChannelAsync(id)`；`[RequirePermission("WeChatChannels.Query/Manage")]` + `[ApiExpose]`（RestRoute 全段 `/wechat-channels/channels`） |
| `SaveChannelRequest` / `SaveChannelResponse` | wire DTO（AppSecret/Token/EncodingAESKey 明文可选，null = 保持既有密文；经 body 传输） |
| `WeChatChannelConfigDto` | 投影 DTO（**不含凭证**——密文不回显；`HasCredentials` 布尔） |

namespace：`TKWF.Ext.WeChatChannels`（跨程序集同 namespace——契约包与主包共享，消费方零歧义）。

## 二、消费方式

```xml
<PackageReference Include="TKWF.Ext.WeChatChannels.Abstractions" Version="0.1.0" />
```

```csharp
// 运营扩展（WeChatOperations）或消费方经 L2 契约消费（不引主包）
var channels = user.Use<IChannelConfigService>().ListChannelsAsync(ct);
```

## 三、版本与发布

- MinVerTagPrefix：`WeChatChannels.Abstractions/v`（独立版本线）
- 首发 V0.1.0（2026-11-05，run 41931855000 publish success）
- **V0.1.1（2026-11-05，密钥键收口——契约面破坏项）**：删除过渡常量 `WeChatChannelsKeyProviderKeys`（首发时占位，主框架 v4.10.104 已并入 `SymmetricKeyProviderKeys.WeChatChannels`）。**迁移**：消费方若引用该常量，改引 `TKWF.Domain` 的 `SymmetricKeyProviderKeys.WeChatChannels`（键名/派生路径不变，零行为差异）；其余契约（`IChannelConfigService`/wire DTO/投影 DTO）零变更。

<!-- EOF -->
