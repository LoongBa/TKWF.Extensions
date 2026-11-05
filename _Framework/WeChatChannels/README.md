# TKWF.Ext.WeChatChannels 微信通道底座扩展技术规范

**状态**: 业务扩展（实体型——通道底座，平台模式装配，零运营依赖） | **版本**: V0.1.0（2026-11-05 自 WeChatOperations 拆分下沉首发） | **框架**: .NET 10 | **依赖**: `TKWF.Ext.WeChatChannels.Abstractions`（契约包/L2）+ `TKWF.Federation.Abstractions`（通道编排契约）+ `TKWF.Federation.WeChat`（凭证解析契约）+ `TKWF.Ext.Permissions.Abstractions`（权限标注）+ `TKWF.Domain` + FreeSql

**核心约束**: 门面继承 `DomainServiceBase` + `AddConstructibleService` 注册（ADR90）/ DataService 经 `User.Use<T>()` 懒加载（DI004）/ 禁手写 Store / 凭证 AES-GCM 密文落库（keyed `ISymmetricKeyProvider`——明文不入库不进日志）/ **非 Web 扩展形态**（不引 AspNetCore——端点由消费方 SG2 按 ADR47 生成控制器，对齐 Identity/Notifications/AuditLogging 先例）

---

## 一、定位

微信公众号**通道底座**——平台侧（Platform）多通道接入管理的轻量链，**零运营依赖**：

| 能力 | 说明 |
|------|------|
| **通道配置** | 表 `TKWF_WeChatChannelConfig`（四唯一键：ChannelId / AppId / (ChannelForm,ChannelId) / Alias）；凭证 AppSecret/Token/EncodingAESKey 均 AES-GCM 单段密文 |
| **门面** | `IChannelConfigService`（契约在 `WeChatChannels.Abstractions`）：`GetAsync(appId)` / `GetByChannelIdAsync(channelId)` / `ListChannelsAsync()` / `SaveChannelAsync(request)` / `DeleteChannelAsync(id)` |
| **凭证两源并存** | `DbWeChatCredentialResolver`（`IWeChatCredentialResolver` 实现——DB 命中解密返回，未命中回退 `WeChatOptions.Channels`；非 AOP 帧自动降级 Options + Warning） |
| **通道注册源** | `WeChatChannelRegistrySource`（`IChannelSource` + `IUserBoundChannelSource`，Priority=100 恒先于静态源；含 disabled 全行投影；Token/EncodingAESKey 投影期解密进 Extra，AppSecret 不进公共列） |
| **能力派生** | `WeChatChannelCapabilityResolver`（ChannelForm × Verification → `ChannelCapabilities`，未知形态 fail-closed None） |
| **密钥** | keyed `ISymmetricKeyProvider`（键 **`SymmetricKeyProviderKeys.WeChatChannels`**——主框架常量（v4.10.104 并入，C11 第 6 扩展边界），跨扩展同信任域；过渡常量已删除收口） |
| **权限** | `WeChatChannels.Query`（读）/ `WeChatChannels.Manage`（写）——与运营扩展 `WeChatOperations.*` 独立授权 |

## 二、安装与接线

```xml
<PackageReference Include="TKWF.Ext.WeChatChannels" Version="0.1.0" />
```

```csharp
[TKWFEnabledExtension(typeof(WeChatChannelsExtensionInitializer<>))]
public class PlatformDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

配置：

```jsonc
{
  "TKWF": {
    "WeChatChannels": {
      "SecretEncryptionKeyPath": "<AES-GCM 密钥文件路径>",  // 生产必配（缺钥 fail-fast）；开发随机兜底
      "IsProduction": true
    }
  }
}
```

⚠️ **运营模式（Merchant）须双白名单**：`WeChatChannelsExtensionInitializer<>` + `WeChatOperationsExtensionInitializer<>`（白名单无传递语义——漏声明 WeChatChannels → keyed provider/门面缺失 → 运行时解析失败）。

## 三、REST 端点（非 Web 扩展形态）

| 端点 | 门面方法 |
|------|---------|
| `GET /api/wechat-channels/channels` | `ListChannelsAsync` |
| `POST /api/wechat-channels/channels` | `SaveChannelAsync`（`SaveChannelRequest` [FromBody]，凭证明文 body 传输） |

RestRoute 为**全段**（含模块段，不含 RestBasePath）——URL = `RestBasePath`（缺省 `/api`）+ RestRoute。控制器由消费方 SG2 按 `GeneratedControllerCatalog`（`ModuleKey=WeChatChannels` / `InterfaceNames=IChannelConfigService` / `ImplementationTypes=ChannelConfigService`）生成（ADR47 能力清单机制）。

## 四、安全边界

- 凭证密文落库（AES-GCM 单段）；DTO 投影不含凭证（密文不回显，`HasCredentials` 仅布尔指示）
- 解密仅出站瞬时（`DbWeChatCredentialResolver`）；解密失败异常消息不含明文
- 注册表投影：Token/EncodingAESKey 解密进 Extra（事件验签信任根需明文）；AppSecret 不进公共列
- 权限标注在契约接口（`WeChatChannels.Query/Manage`）

## 五、与既有资产边界

| 资产 | 归属 |
|------|------|
| `IChannelConfigService` 契约 + wire/投影 DTO + 键常量 | `WeChatChannels.Abstractions` |
| 通道实体 + 门面实现 + 凭证解析 + 注册源 + 能力派生 | **本扩展** |
| 运营 7 门面 + 网关 + 回复 handler | `TKWF.Ext.WeChatOperations`（依赖本扩展契约包） |
| 通讯/协议（WeChatApiClient / IWeChatCredentialResolver 契约 / 事件回复基类） | `TKWF.Federation.WeChat` |

## 六、版本记录

| 版本 | 内容 |
|------|------|
| V0.1.0（2026-11-05 发布） | 拆分下沉首发：通道实体 + `IChannelConfigService` + 凭证两源并存 + 注册源（Priority=100）+ 能力派生 + keyed 密钥 + 非 Web 形态端点（`/api/wechat-channels/channels`）+ 权限键 `WeChatChannels.Query/Manage`；测试 15 用例全绿（契约 8 + Initializer 形态 6 + 特性 1） |

<!-- EOF -->
