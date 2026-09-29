# TKWF.Ext.SecurityLog.Abstractions 契约抽象

**状态**: 契约抽象（ADR48 D7 依赖倒置） | **框架**: .NET 10 | **依赖**: TKWF.Domain（SecurityLoggingOptions 的 `[Options("TKWF:SecurityLog")]` 特性）

## 定位

安全日志公开契约（3 接口 + 8 类型）——`ISecurityLogStore`（写入）/ `ISecurityLogQueryService`（分页/过滤查询 + 详情 + 计数）/ `ISecurityLogAnalyticsService`（v0.2.0 异常检测聚合 + 保留天数清理）及其 DTO/Options。Account V0.3.0 登录历史消费查询契约（**唯一编译期引用方**）：消费方同时启用 Account + SecurityLog 场景与现状完全一致（SecurityLog 是登录历史唯一数据源，不重复建表）。属 SecurityLog 扩展（V0.3.0 提取）侧拆出的契约抽象。

## 内容

| 类型 | 说明 |
|------|------|
| `ISecurityLogStore` | 写入存储抽象——仅写路径（`SaveAsync`，异常静默：落库失败记 Warning，不阻断认证流程） |
| `ISecurityLogQueryService` | 查询服务接口——`GetListAsync`（分页/过滤，列表 DTO 不含 Detail）/ `GetDetailAsync`（含 Detail 全文）/ `CountAsync` |
| `ISecurityLogAnalyticsService` | 分析服务接口——`GetTopFailedUsersAsync` / `GetTopFailedIpsAsync`（窗口内失败次数 TopN）+ `CleanupExpiredAsync`（保留天数清理） |
| `SecurityLogEntry` | 安全日志事件模型（record：EventType/UserName/IpAddress/Result/Detail 等 9 字段，只增不改） |
| `SecurityLogQueryInput` | 查询输入参数（过滤字段全可选：UserName/IP/EventType/Result/FromUtc/ToUtc + Skip/Take） |
| `SecurityLogPagedResult` | 分页查询结果（Total + Items 列表 DTO） |
| `SecurityLogListItemDto` | 列表项 DTO（**不含 Detail 全文**，安全决策 D5） |
| `SecurityLogDetailDto` | 详情 DTO（含 Detail/UserAgent/CorrelationId 全量） |
| `SecurityLogFailureStat` | 失败次数聚合统计（Dimension + Count，异常检测输出项） |
| `SecurityLogEventTypes` | 事件/结果/分类字符串常量（收敛字面量，避免魔法字符串散落） |
| `SecurityLoggingOptions` | 配置选项（`TKWF:SecurityLog`）：Enabled / EventTypes / RetentionDays(90) + CleanupBatchSize(500) |

## 零破坏铁律

- **命名空间保持 `TKWF.Ext.SecurityLog`**（不追加 `.Abstractions` 后缀）——既有消费方 `using TKWF.Ext.SecurityLog;` 不变；本体内部实现类（Store/QueryService/AnalyticsService/FilterAttribute/Initializer）同命名空间，跨项目后编译器自动从 Abstractions 解析，无需加 using（Account 侧 `LoginHistoryService.cs` 零代码改动）。
- 仅程序集从 `TKWF.Ext.SecurityLog` 拆出为 `TKWF.Ext.SecurityLog.Abstractions`；接口/DTO/Options 签名与拆分前完全一致（签名/属性/逻辑一字未改）。
- **依赖 TKWF.Domain**：`SecurityLoggingOptions` 使用 `[Options("TKWF:SecurityLog")]` 特性（`TKW.Framework.Domain`）——区别于 Account.Abstractions 纯 BCL；双模式 DLL 定案下 UseLocalFw=true 走集中 DLL 引用、false 走 PackageReference。

## 引用关系

```
SecurityLog（实现，Store/QueryService/AnalyticsService/FilterAttribute/Initializer）──ProjectReference──▶ SecurityLog.Abstractions（契约）
Account（消费方，ILoginHistoryService → ISecurityLogQueryService/AnalyticsService）──ProjectReference──▶ SecurityLog.Abstractions（契约）
```

**注意**：本体不再直接暴露契约类型——Account 只引契约不引实现（ADR50 L2 门控合规）。未启用 SecurityLog 扩展时，Account 登录历史调用抛 `InvalidOperationException` 明确提示（现状不变）。安全日志完整用法见 **SecurityLog 扩展使用指南**（`docs/SecurityLog/安全日志扩展-使用指南.md`）。
