# TKWF.Ext.RateLimiting Web 层限流扩展技术规范

**状态**: Web 层接线扩展 (Web Wired Extension) | **版本**: V0.1.0 (Web 层限流接线) | **框架**: .NET 10 | **依赖**: 零第三方 NuGet（net10 内置 `System.Threading.RateLimiting` / `Microsoft.AspNetCore.RateLimiting`）

**核心定位**: **不重建 Domain 层**——主框架限流基座（D10D：`AddRateLimitPolicy` + `IRateLimitPolicyRegistry` + `[RateLimit]` AOP + `PartitionedRateLimiter` + `EnforceAsync`）已有；本扩展仅做 **ASP.NET Core `AddRateLimiter` 中间件接线**——全局/端点级策略（固定窗口/滑动窗口/令牌桶）+ IP/用户分区 + 429 + `Retry-After` + `TKWF:RateLimiting` 配置节。纯内存扩展，**无 SG1/无持久化**。

---

## 一、定位（与 Domain 层双层防护，Oracle C2）

| 维度 | Web 层（本扩展 `AddTkfwRateLimiting`） | Domain 层（主框架 `[RateLimit]` AOP） |
|------|----------------------------------------|---------------------------------------|
| 管辖 | HTTP 入口（端点/路径/IP/用户分区） | 领域服务方法（`FilterBuilder.AddRateLimit()` + `[RateLimit("policy")]`） |
| 粒度 | 粗粒度兜底（IP 防破解、端点级、全局限流） | 细粒度用户级频控（服务方法级） |
| 分区 | `HttpContext.Connection.RemoteIpAddress` / `HttpContext.User`（匿名 fallback IP） | `IRateLimitPolicyRegistry`（用户键，Domain 层上下文） |
| 接线 | `services.AddTkfwRateLimiting(...)` + `app.UseRateLimiter()` | `services.AddRateLimitPolicy(name, opts => ...)` + 服务方法标注 |
| 拒绝语义 | `RejectionStatusCode`（默认 429）+ `Retry-After` 头 | `EnforceAsync` 抛 `AuthenticationException`（429 语义） |

**组合用法**（登录接口示例）：Web 层 IP 限流（`/api/auth/login` 每分钟 N 次/IP——Domain 层 `RateLimitPartitionBy.Ip` 明确留白抛异常）+ Domain 层 `[RateLimit]` 用户级频控——双层互补不替代。

## 二、安装与接线

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\..\_Framework\RateLimiting\TKWF.Ext.RateLimiting.csproj" />
```

```csharp
// 1. 白名单启用扩展（v4.9.85+ 必需；仅 Options 绑定，中间件接线不自动）
[TKWFEnabledExtension(typeof(RateLimitingExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 2. 注册 Web 层限流（接线点由消费方决定——不自动）
builder.Services.AddTkfwRateLimiting(o =>
{
    o.Global = new() { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 100, WindowSeconds = 60 };
    o.Partition = RateLimitPartition.Ip;
    o.EndpointPolicies["/api/auth/login"] = new() { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 5, WindowSeconds = 60 };
});
// 3. 启用中间件（Route 之前）
var app = builder.Build();
app.UseRateLimiter();
```

## 三、Options（`TKWF:RateLimiting` 节）

```jsonc
{
  "TKWF": {
    "RateLimiting": {
      "Global": { "Algorithm": "FixedWindow", "PermitLimit": 5, "WindowSeconds": 60, "SegmentsPerWindow": 6, "ReplenishmentTokensPerSecond": 1, "QueueLimit": 0 },
      "Partition": "Ip",                        // None | Ip | User（默认 Ip）
      "EndpointPolicies": {                     // 精确路径匹配；未命中回退 Global
        "/api/auth/login": { "Algorithm": "FixedWindow", "PermitLimit": 5, "WindowSeconds": 60 }
      },
      "RejectionStatusCode": 429,
      "RetryAfter": true
    }
  }
}
```

- **算法**：`RateLimitAlgorithm.FixedWindow | SlidingWindow | TokenBucket`
- **令牌桶语义**（Oracle P2-1）：`PermitLimit` = `TokenLimit` 最大容量（突发许可上限）；补充走 `ReplenishmentTokensPerSecond`（每秒 TokensPerPeriod，`ReplenishmentPeriod`=1s）
- **默认值对齐**主框架 Domain 层 `RateLimitPolicyOptions`（PermitLimit=5 / Window=1min / SegmentsPerWindow=6 / QueueLimit=0）
- **优先级**：默认值 < 配置节绑定 < `AddTkfwRateLimiting(configure)` 编程式覆盖
- 配置绑定通道：SG1 `[Options("TKWF:RateLimiting")]`（消费方自动）+ `AddTkfwRateLimiting` 内部 `AddOptions().BindConfiguration`（幂等）

## 四、端点级策略（Oracle P2-2 精确匹配）

**两条生效路径**（互补不叠加）：
1. **自动**：`AddTkfwRateLimiting` 注册的全局分区器按 `Request.Path` 精确命中 `EndpointPolicies` 自动换用端点策略（key 带 `ep:{path}:` 前缀独立配额）；未命中回退 `Global`。v0.1.0 **不支持通配符前缀**（v0.2.0 评估）。
2. **标注式**：端点定义时 `.MapTkfwRateLimiter("/api/auth/login")`（等价 `RequireRateLimiting`）——显式挂命名策略；命中时中间件只执行端点策略不执行全局策略。

## 五、分区器（Oracle C1）

- **IP**：`HttpContext.Connection.RemoteIpAddress` → `ip:{ip}`；TestServer/无 IP 上下文 fallback `ip:unknown`
- **User**：`HttpContext.User` ClaimsPrincipal 解析——`NameIdentifier` 优先、回退 `Name`；**匿名 fallback IP 分区**。注释：Web 中间件管线早于 Domain 层，DomainUser/ISessionManager 在此不可用
- **None**：全局限流器共享同一配额（不分区）

## 六、断言说明/边界

- **进程内限流**：限流状态存于进程内存——多实例部署不共享（v0.2.0 Redis 分区器候选）
- **IP 解析**：经反向代理/负载均衡时 `RemoteIpAddress` 为代理 IP——需配置 `ForwardedHeaders`（`app.UseForwardedHeaders`）
- **429 对齐**：`RejectionStatusCode` 默认 429 对齐 Domain 层 `RateLimitException`（429/RATE_LIMITED 语义）；`Retry-After` 默认 true 写响应头
- **不包含**：Redis 分布式限流（v0.2.0）、策略管理 API（运行时动态改策略，v0.2.0）、Domain 层改动（主框架 `EnforceAsync` AuthenticationException 留白不动）
- **不改主框架、不做 slnx 接线**：本扩展独立构建（经项目根 `Directory.Build.props` `TKWFSourceRoot` 跨仓库引用）

---

**文档信息**: V0.1.0 | 2026-09-09 | 关联：ADR-RateLimiting-扩展边界与Web层接线.md、v0.1.0-RateLimiting-Web层限流-开发方案.md（主框架私有）、D10D 限流架构（主框架）、[限流扩展-使用指南](../../docs/RateLimiting/限流扩展-使用指南.md)