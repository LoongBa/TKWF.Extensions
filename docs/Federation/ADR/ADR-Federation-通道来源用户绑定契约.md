# ADR-Federation-通道来源用户绑定契约

- 状态：已采纳（2026-10-30）
- 决策域：Federation（契约）/ WeChatOperations（实现）
- 取代：无
- 关联：D03 第 1 部分 §8.3 投影映射、§8.4 注册与降级；`IDomainService` 守卫工厂机制

## 一、目的与目标

让「需要读数据库的通道来源」（DB 权威源）在**不注册 `IDomainUser` 到 DI**、**不绕守卫直接取 DataService** 的前提下取得配置数据。目标状态：新增一个可选接口 `IUserBoundChannelSource`（Federation.Abstractions），由 `CompositeChannelRegistry` 在构造时把当前域用户绑定给声明该接口的来源；DB 源经 `IDomainUser.Use<TDataService>()` 取数。静态源不实现该接口，零改动。

## 二、问题

**现象**：D03 1.7 交付的 `WeChatChannelRegistrySource.GetChannels()` 在生产环境恒返回空——DB 中配置的微信通道不参与注册表组合，联邦/事件链路静默退化为仅静态源。

**触发场景**：任意消费方按生产形态装配（不注册 `IDomainUser`、不手工注册 DataService），调用事件/授权端点时 `CompositeChannelRegistry` 枚举到 DB 源。

**根因**：DB 源以 `IServiceProvider.GetRequiredService<WeChatChannelConfigEntityDataService>()` 延迟解析 DataService。框架对 DataService 采用**守卫注册**（throw-factory：`[领域架构守卫] 类型 X 不允许直接从 DI 容器获取。必须通过 User.Use<TInterface>() 上下文创建`），而 `IDomainUser` 永不注册 DI（tkwf-extension 铁律）。异常被来源侧 catch-all 吞掉 → 恒空。测试宿主手工注册 `IDomainUser` + DataService 掩盖了该缺陷。

**现有方案不足**（逐项评估）：

| 方案 | 否决理由 |
|---|---|
| 让 `IChannelSource : IDomainService` + `TryAddEnumerableConstructible` | 契约语义扩张波及 8 个平台库与全部实现；守卫工厂化后，`StaticChannelRegistry`（普通 DI scoped，ctor 注入 `IEnumerable<IChannelSource>`）在帧外枚举会抛，需连带重构 |
| 把 `IDomainUser` 注册进 DI | 违反框架铁律（IDomainUser 永不注册），且多帧并发下语义错误 |
| 源内 `IServiceProvider.GetRequiredService<DataService>()`（原实现） | 即本缺陷本身：守卫必抛 |
| 源内延迟到方法调用期读 `DomainUserContext.CurrentAopUser` | `Use<T>()` 的 `finally` 在**返回前**已恢复 ambient 帧，`GetChannels()` 调用时必为 null；且该上下文类型为 internal，扩展不可访问 |
| 经 `IDomainUserAccessor<TUserInfo>` | 仅后台线程的 `StandaloneDomainUserAccessor` 实现，AOP 帧内无注册 |

## 三、使用场景

**适用**：
- 组合型注册表（`IChannelRegistry` 及同型「门面 + 多来源集合」结构）中，某个来源的数据必须经 SG1 DataService 读取（DB 权威源）。
- 需要「来源在帧内获得用户、在帧外仍可合法取数」的场合：绑定发生在门面构造（守卫工厂帧内），消费发生在 `Use<门面>()` 返回之后。

**不适用（边界）**：
- **静态源**（Options/配置文件投影）——无用户上下文需求，不实现该接口（保持零改动）。
- **非集合型服务**——直接用 `DomainServiceBase` 派生类 + 基类 `User`，无需本契约。
- **不允许**以本契约把 `IDomainUser` 传播给非来源对象，或用于绕过守卫直取 DataService；实现方必须在未绑定时 fail-closed 降级并留痕。

**约束**：绑定时机唯一（`CompositeChannelRegistry` 构造），重复绑定以后者为准；实现方不得在绑定前访问数据。
