# TKWF.Federation.Abstractions — 联邦通道编排契约包（V0.1.0，2026-10-18 ④ 裁定落地）

> **定位**：联邦对外连接层的**通道编排契约抽象**（对齐 TrustCenter.Abstractions 先例——契约继承 `IDomainService` + 引主框架 `TKWF.Domain`，非"零框架引用"）。**零实体、零 SG1 分析器、零 Initializer**——纯契约 + DTO。

## 一、背景（裁定依据）

DMP-Lite 消费方反馈 §四（P2）：`TKWF.Ext.Federation` 主包承载通道编排契约（`IChannelRegistry`/`IChannelSource`/`ChannelConfig`/`IEventReplyHandler`/`IEventOrchestrator`）但**无 Abstractions 契约包收口**——WeChatOperations 扩展为拿契约被迫直接引用 Federation 扩展主包（`WeChatOperations.csproj` 自述），Merchant 域须白名单 Federation 引入 SSO 门面"死注册"。（消费者误称"TKWF.Ext.ChannelRegistry 命名不合规"——无该库，契约在 Federation 主包；判定走 Abstractions 契约包路线，非并入 WeChat 平台库。）

**裁定**：契约（5 接口）迁入本契约包；编排实现（`CompositeChannelRegistry`/`StaticChannelRegistry`/`EventOrchestrator`——依赖 `DomainServiceBase`/Options/AuthCenter）**留在 Federation 主包实现 Abstractions 接口**（契约在 Abstractions、实现在主包的标准分层）。

## 二、契约清单（均自 Federation 主包迁入，namespace 保持 `TKWF.Ext.Federation`——跨程序集同 namespace，实现/消费方零 using 迁移）

| 契约 | 说明 | 依赖 |
|------|------|------|
| `IChannelRegistry : IDomainService` | 通道注册表门面（`GetAsync`/`GetByAliasOrIdAsync`/`GetAllAsync`/`GetDefaultAsync`——选区内核） | `TKWF.Domain`（IDomainService） |
| `IChannelSource` | 静态通道来源（平台库 `XxxOptions.Channels` 投影统一 `ChannelConfig`——`PlatformType`/`GetChannels()`） | 纯 BCL |
| `ChannelConfig` | 通道配置统一模型（公共列 ChannelId/PlatformType/AppId/AppSecret/IsDefault/IsEnabled + 扩展字典 Extra） | 纯 BCL |
| `IEventReplyHandler : IDomainService` | 事件回复处理器契约（消费方实现 + `TryAddEnumerableConstructible` 注册——first-non-null-wins） | `TKWF.Domain` + **`TrustCenter.Abstractions`**（`SsoChannelEventContext`/`EventReplyResult`） |
| `IEventOrchestrator : IDomainService` | 事件编排门面（平台回复 → handler 迭代 → null 兜底） | `TKWF.Domain` + **`TrustCenter.Abstractions`** |

> **依赖核实（2026-10-18）**：`SsoChannelEventContext`/`EventReplyResult` 定义于 `TrustCenter.Abstractions\ISsoChannel.cs`（纯 BCL 契约包）——本契约包最小引用集 = **`TKWF.Domain` + `TKWF.Ext.TrustCenter.Abstractions`**（可行性优于裁定预估：不引 AuthCenter/主框架 Domain.Web）。

## 三、消费方迁移面（改引本包）

| 项目 | 改引 | using 变更 |
|------|------|-----------|
| 9 平台库（WeChat/QQ/DingTalk/WeCom/Oidc/Google/Microsoft/Alipay/Template） | csproj `ProjectReference ..\Federation\TKWF.Ext.Federation.csproj` → `..\Federation.Abstractions\TKWF.Federation.Abstractions.csproj` | **零变更**（namespace 保持 `TKWF.Ext.Federation`） |
| `TKWF.Ext.WeChatOperations` | 同上（为拿契约直引 Federation 主包 → 改引本包） | **零变更** |
| `TKWF.Ext.Federation` 主包 | ProjectReference 本包（实现 Abstractions 接口） | **零变更**（实现类仍在 namespace `TKWF.Ext.Federation`） |

## 四、版本与发布

- MinVerTagPrefix：`Federation.Abstractions/v`（独立版本，对齐 `Account.Abstractions`/`TrustCenter.Abstractions`/`AuthCenter.Abstractions` 先例）
- 发布：契约包新 tag + 9 平台库 + Federation 主包 + WeChatOperations 新版本联动（tag 须征得用户同意——AGENTS §5）

<!-- EOF -->