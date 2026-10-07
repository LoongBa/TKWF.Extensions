# TKWF.Ext.RateLimiting 限流扩展技术规范

**状态**: Web 层接线扩展 (Web Wired Extension) + 点检查原语 DB Provider | **版本**: V0.1.0 (Web 层限流接线) + **V0.3.0（点检查原语 DB Provider——`SqlCountRateLimitCheck`：独立计数表 `TKWF_RateLimitCounter` + ADR89 `UpdateWhereAsync` CAS 原子递增，跨实例正确；抽象 + 内存默认 `IRateLimitCheck`/`MemoryRateLimitCheck` 在主框架 `TKW.Framework.Utility.RateLimitChecks` v4.10.67）** | **框架**: .NET 10 | **依赖**: 零第三方 NuGet（net10 内置 `System.Threading.RateLimiting` / `Microsoft.AspNetCore.RateLimiting`）+ FreeSql（计数表实体）+ TKWF.Utility（v4.10.67 IRateLimitCheck）

**核心定位**: **不重建 Domain 层**——主框架限流基座（D10D：`AddRateLimitPolicy` + `IRateLimitPolicyRegistry` + `[RateLimit]` AOP + `PartitionedRateLimiter` + `EnforceAsync`）已有；本扩展做两件事：① **ASP.NET Core `AddRateLimiter` 中间件接线**（Web 层——全局/端点级策略 + IP/用户分区 + 429 + `Retry-After` + `TKWF:RateLimiting` 配置节）；② **点检查原语 DB Provider**（v0.3.0——`SqlCountRateLimitCheck` 实现主框架 `IRateLimitCheck`，独立计数表 + CAS，跨实例正确）。

---

## 一、定位（与 Domain 层双层防护，Oracle C2）

| 维度 | Web 层（本扩展 `RateLimitingWebExtension`，v4.10.45 收敛迁移） | Domain 层（主框架 `[RateLimit]` AOP） |
|------|----------------------------------------|---------------------------------------|
| 管辖 | HTTP 入口（端点/路径/IP/用户分区） | 领域服务方法（`FilterBuilder.AddRateLimit()` + `[RateLimit("policy")]`） |
| 粒度 | 粗粒度兜底（IP 防破解、端点级、全局限流） | 细粒度用户级频控（服务方法级） |
| 分区 | `HttpContext.Connection.RemoteIpAddress` / `HttpContext.User`（匿名 fallback IP） | `IRateLimitPolicyRegistry`（用户键，Domain 层上下文） |
| 接线 | 消费方 `UseWebExtensions(e => e.Add<RateLimitingWebExtension>(...))`（锚点 BeforeAuthentication——Route 前） | `services.AddRateLimitPolicy(name, opts => ...)` + 服务方法标注 |
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

// 2. Web 装配钩子一次声明（v4.10.45 收敛迁移——旧 AddTkfwRateLimiting + app.UseRateLimiter() 静态接线已删除，
//    CHANGELOG 破坏性变更；AddRateLimiter 展开 + Options 绑定 + 中间件挂载全部内聚进扩展）
builder.ConfigWebAppDomain<MyUserInfo, MyDomainInitializer, DomainWebOptions>(...)
    .UseWebSession()
    .UseWebExtensions(e => e.Add<RateLimitingWebExtension>(x =>
    {
        x.ConfigureOptions = o =>
        {
            o.Global = new() { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 100, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.Ip;
            o.EndpointPolicies["/api/auth/login"] = new() { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 5, WindowSeconds = 60 };
        };
    }))
    .BeforeRouting(...)
    .AfterRouting(...)
    .Build(...);
```

> **标注式限流（v4.10.45 现状）**：本扩展默认锚点 `BeforeAuthentication`（Route 之前）——自动路径感知模式
> （全局分区器按 `Request.Path` 精确命中 `EndpointPolicies`）无碍；**标注式 `RequireRateLimiting` 需端点
> metadata（UseRouting 后）**——当前仍需消费方在 `BeforeRouting` 显式 `app.UseRateLimiter()`（AfterRouting
> 锚点为框架机制预留，见 G18 §5 说明；待扩展提供 AfterRouting 锚点变体后收敛）。

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
- **优先级**：默认值 < 配置节绑定 < `RateLimitingWebExtension.ConfigureOptions` 编程式覆盖
- 配置绑定通道：SG1 `[Options("TKWF:RateLimiting")]`（消费方自动）+ `RateLimitingWebExtension.ConfigureServices` 内部 `AddOptions().BindConfiguration`（幂等）

## 四、端点级策略（Oracle P2-2 精确匹配）

**两条生效路径**（互补不叠加）：
1. **自动**：`RateLimitingWebExtension` 注册的全局分区器按 `Request.Path` 精确命中 `EndpointPolicies` 自动换用端点策略（key 带 `ep:{path}:` 前缀独立配额）；未命中回退 `Global`。v0.1.0 **不支持通配符前缀**（v0.2.0 评估）。
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

## 七、点检查原语 DB Provider（v0.3.0，SqlCountRateLimitCheck）

> **三层限流分工**（详见使用指南 §7）：Web 层（本扩展中间件——IP/端点粗粒度，HTTP 429 + Retry-After）/ Domain AOP（主框架 `[RateLimit]`——方法级，`RateLimitException`）/ **点检查原语**（`IRateLimitCheck`——服务内任意位置窗口计数，`bool + RetryAfter + Remaining` 返回值）。

### 7.1 启用（消费方确定性路径，RC1 排序裁定）

```csharp
// 消费方 ConfigureServices / DomainHostInitializerBase 内——显式注册（确定性；依赖扩展自动注册序不可靠）
services.AddSingleton<IRateLimitCheck, SqlCountRateLimitCheck>();
```

- **DI 形态（接线型）**：`SqlCountRateLimitCheck` 非 `DomainServiceBase`（`IRateLimitCheck` 非 `IDomainService`，AddConstructibleService 约束不满足）——ctor 仅 DI 可解析参数；**ctor 内解析 `IRateLimitCounterDataService`（AddConstructibleService 守卫工厂）并持有**（构造时机 = 消费门面经 `User.Use<T>()` 守卫工厂解析依赖链内，CurrentAopUser 非空）；门面方法执行（帧外）用持有实例（其内部 DataService 经 `User.Use<具体类>()` NoAop 直建，不依赖帧）。⚠️ 消费方**帧外**首次解析（如启动期裸 `GetRequiredService`）→ 守卫工厂抛领域架构守卫（正确 fail）。
- **数据访问门面** `IRateLimitCounterDataService`（`AddConstructibleService` 注册于 `RateLimitingExtensionInitializer`）——委托 `RateLimitCounterEntityDataService`（SG1 生成基座 + 手写分部业务方法），红线合规（零 IFreeSql/IEntityDAC 直注入）。

### 7.2 语义

- **fixed window** 单窗口计数器（`WindowEndUtc` 过期重置）——与 Memory 默认的 sliding window 行为差异（窗口边界双倍突发），MFA 验证等低频场景可接受（Oracle7 RC2）。
- **TryAcquire 原子语义**（ADR89 CAS）：首插（Key 唯一约束捕获并发冲突）→ 窗口内 `SET Count=Count+1 WHERE Key=@key AND WindowEndUtc>=@now AND Count<@max`（affected==1 成功 / ==0 读行分辩：窗口过期重置 / Count>=max 拒绝）——**非读-改-写 TOCTOU，跨实例 DB 行锁正确**。
- **惰性淘汰**：过期行随下一次命中重置（无后台清理）；⚠️ **孤儿键（不再命中的 key）无自然回收、表增长无界**——v0.1.0 已知限制（清理任务 v0.2.0），消费方限制键空间为有界标识（如 userId）。
- **key 约定**：`{policy}:{partitionBy}:{subject}`（各段非空；Provider 视为不透明字符串）。
- **审计表 COUNT vs 计数表（决策规则）**：需审计轨迹（登录尝试/短信发送）→ 审计表 COUNT（AuthCenter `AuthLoginAttempt`/`SmsRecord` 保留）；仅需限流、单次尝试无审计价值（MFA 验证、改密/找回频控）→ `TKWF_RateLimitCounter`。

### 7.3 数据模型

```sql
TKWF_RateLimitCounter(id BIGINT PK, key VARCHAR(256) UNIQUE,   -- TKWFIX_RateLimitCounter_Key 唯一
                      count INT, window_start_utc DATETIME, window_end_utc DATETIME,
                      create_time DATETIME, update_time DATETIME)
```

**文档信息**: V0.1.0 | 2026-09-09 | V0.3.0 | 2026-10-07 | 关联：ADR-RateLimiting-扩展边界与Web层接线.md、ADR-RateLimiting-点检查原语与三层限流分工.md、IRateLimitCheck抽象与三层限流分工-开发方案.md（v0.3.0）、D10D 限流架构（主框架）、[限流扩展-使用指南](../../docs/RateLimiting/限流扩展-使用指南.md)