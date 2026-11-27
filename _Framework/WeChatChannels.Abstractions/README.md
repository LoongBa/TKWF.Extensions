# TKWF.Ext.WeChatChannels.Abstractions 微信通道底座契约包技术规范

**状态**: 契约抽象包（零实体、零 SG1、零 Initializer——对齐 Federation.Abstractions 先例） | **版本**: V0.1.1（2026-11-05 密钥键收口；首发 V0.1.0） | **框架**: .NET 10 | **依赖**: `TKWF.Domain`（IDomainService）+ `TKWF.CodeGeneration.Abstractions`（[ApiExpose]）+ `TKWF.Ext.Permissions.Abstractions`（[RequirePermission]）

---

## 一、定位

微信通道底座的**跨扩展消费契约**（L2 门控：扩展间只引 Abstractions，不引实现主包——`TKWF0022`）：

| 类型 | 说明 |
|------|------|
| `IChannelConfigService : IDomainService` | 通道配置门面契约——`GetAsync(appId)` / `GetByChannelIdAsync(channelId)` / `ListChannelsAsync()` / `SaveChannelAsync(SaveChannelRequest)` / `DeleteChannelAsync(id)`；`[RequirePermission("WeChatChannels.Query/Manage")]` + `[ApiExpose]`（**5 方法全部暴露 REST**：`GET /wechat-channels/channels` 列表 / `POST /wechat-channels/channels` 保存 / `GET /wechat-channels/channels/{channelId}` 单查 / `GET /wechat-channels/channels/by-app/{appId}` 按 AppId 单查 / `DELETE /wechat-channels/channels/{id}` 删除；后三者 `ExposeGraphQL=false` 只开 REST） |
| `SaveChannelRequest` / `SaveChannelResponse` | wire DTO（AppSecret/Token/EncodingAESKey 明文可选，null = 保持既有密文；经 body 传输） |
| `WeChatChannelConfigDto` | 投影 DTO（**不含凭证**——密文不回显；`HasCredentials` 布尔） |
| `IChannelUsageGuard` / `ChannelUsageCheck` | 通道在用守卫 SPI（可选）——`DeleteChannelAsync` 枚举全部实现，任一 `InUse` 即拒删（`CHANNEL_IN_USE`）；运营数据属主实现（如 `WeChatOperationsChannelUsageGuard`），零实现 = 放行 |

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
- **待发布（2026-11-27，通道治理面三端点补暴露）**：`GetAsync`/`GetByChannelIdAsync`/`DeleteChannelAsync` 由 `ExposeRest=false` 改为显式 `[ApiExpose(RestRoute=..., RestMethod=..., ExposeGraphQL=false)]`——供通道治理面 UI（`WeChatChannels.UI`）消费；Gate 纪律：`ExposeRest=false ≠ 不暴露（GraphQL 默认开）`，故补 REST 时同时显式关 GraphQL。
  - **REST 面 = 加法**（新增 3 端点，不破坏存量 REST 消费方）；**GraphQL 面 = 收口**——这 3 方法原 `ExposeRest=false` 但 `ExposeGraphQL` 默认 true，即原本 GraphQL 可查；显式关 GraphQL 会移除其 GraphQL resolver。若存量 HotChocolate 消费方引用了这 3 方法，属 GraphQL schema 破坏性变更（仓内无 GraphQL 消费实证；外部消费方须自查）。**勿笼统标"不破坏存量"**。
  - **发布前测试义务（契约包变更，按 Agents §8 测试纪律矩阵）**：受影响面 = 契约实现方（`WeChatChannels`）+ 编排消费方（`WeChatOperations`，经 `User.Use<IChannelConfigService>`）+ 契约实现库链路；建议 L2 全量回归随发布执行。
  - 随下次发布 bump 版本；消费方 SG2 按能力清单自动生成对应控制器端点。
- **同步新增**：`IChannelUsageGuard`（在用守卫 SPI，`: IDomainService`）+ `ChannelUsageCheck` 记录——`DeleteChannelAsync` 枚举门禁；与上述三端点同属「待发布」批次（加法变更，不破坏存量）。

<!-- EOF -->
