# HttpAuth 匿名端点排除机制——开发方案

> **日期**: 2026-10-14 | **类型**: 框架机制（框架组功能点——非扩展打补丁） | **状态**: 方案定稿（Oracle 评审 APPROVE）
> **触发**: 框架组转达——`/feberation` 不在 HttpAuthentication 默认排除列表，Federation/AuthCenter 匿名端点会被 HttpAuth 401 拦截
> **对齐原则**: tkwf-design-principles P4「唯一权威事实源下游纯消费」——AllowAnonymous 元数据即事实源，中间件纯消费
> **定位**: 服务所有 Web 扩展的**根因修复**（非逐个扩展打补丁）

---

## 一、背景与问题

### 1.1 现状

主框架 `HttpAuthenticationMiddleware<TUserInfo>`（`TKW.Framework.Domain.Web` 程序集）在管线 **ContextExtraction 之后、路由之前**挂载（`WebAppBuilder.cs` L244 `_PipelineActions.Add(UseMiddleware<HttpAuth>)`）。其认证判定 `RequiresAuthentication(PathString)` **仅**基于 `HttpAuthenticationOptions.ExcludedPathPrefixes`（默认 5 项）：

```csharp
private bool RequiresAuthentication(PathString path)
{
    foreach (var prefix in options.Value.ExcludedPathPrefixes)
        if (pathStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
    if (pathStr.Contains('.')) return false;   // 静态文件启发式
    return true;
}
```

默认排除列表（`HttpAuthenticationOptions.cs` L25-32 + `_LifelinePrefixes`）：`/api/auth/login`、`/api/auth/logout`、`/health`、`/healthz`、`/Setup`。

### 1.2 缺口根因

扩展 WebExtensions（IWebExtension 实现）在 `ConfigureEndpoints` 用 `.AllowAnonymous()` 声明匿名端点，但：

1. **HttpAuthenticationMiddleware 不读端点 AllowAnonymous 元数据**（中间件在路由前，`context.GetEndpoint()` 不可用）
2. 声明的匿名面（AllowAnonymous）与 HttpAuth 排除列表是**同一事实的两次表达且分离**
3. 后果：扩展声明的匿名端点（如 `/feberation/*`、`/api/auth/sms/*`）仍会被 HttpAuth 401 拦截，消费方必须手动 `cfg.HttpAuth.ExcludedPathPrefixes.Add(...)`

### 1.3 受影响扩展（Oracle 盘点）

| 扩展 | WebExtension | RoutePrefix 默认 | 匿名端点 | 受影响? |
|------|-------------|-----------------|---------|---------|
| Federation | `FederationWebExtension` | `/feberation` | 9/10（仅 `trust/issue` 已认证） | ✅ 全部匿名端点在默认排除外 |
| AuthCenter | `AuthCenterWebExtension` | `/api/auth` | 7/10（`logout`/`verify`/`grants` 已认证） | ✅ `/api/auth/sms/*`、`refresh`、`ticket/exchange` 在默认排除外 |
| HealthCheck | `HealthCheckWebExtension` | `/health` | 1（开关式 AllowAnonymous=true） | ❌ `/health` 已在生命线前缀 |
| Notifications.SignalR | `NotificationsHubWebExtension` | `/hubs/notifications` | 0（默认 RequireAuthorization） | ❌ 默认非匿名 |
| RateLimiting | `RateLimitingWebExtension` | 无端点 | 0（纯中间件） | ❌ |
| Identity（SG1b） | —（`[ApiExpose(AllowAnonymous=true)]`） | `/auth/*` | 2（register/login） | ✅ 不经 IWebExtension 载体 |

**关键判据**：AuthCenter 与 Federation 都是**部分匿名**（同前缀混有已认证端点）——`/api/auth` 下 logout/verify/grants 需认证、`/feberation` 下 trust/issue 需认证——**整前缀排除不可行**（会失守已认证端点）。这是方案选型的决定性约束。

---

## 二、方案选型（Oracle 评审结论）

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. IWebExtension 声明前缀** | 扩展声明 `AnonymousPathPrefixes`（默认属性），WebAppBuilder 统一 Union 进排除列表 | ⚠️ 止损可用，但同前缀混认证不可解（AuthCenter 需枚举子前缀）、与 AllowAnonymous 双源漂移 |
| **B. 自动扫描端点元数据推导前缀** | 装配时遍历 AllowAnonymous 端点 → 前缀 | ❌ 不可行——前缀推导在同前缀混认证场景必然误排除 |
| **C. 中间件后移读端点元数据（推荐）** | HttpAuth 锚点后移 **AfterRouting**，`RequiresAuthentication` 升级读 `context.GetEndpoint()` 的 `IAllowAnonymous` 元数据 | ✅ **根因修复**——AllowAnonymous 单一事实源，声明即放行 |

**Oracle 裁决**：方案 C 是**架构正解**（对齐 ASP.NET Core`AuthorizationMiddleware` 语义，消除双源表达）；方案 A 仅作止损；方案 B 不可行。**C 与 A 均需框架组改**（IWebExtension 与 HttpAuth 都在主框架 `_TKWF` 内），C 工作量 Medium（1-2d）、A 为 Quick（<1h）。

**本项目决策**：用户明确要求"作为框架组功能点设计评审开发，而非打补丁" → **直接方案 C**（不做 A 止损过渡，避免双源声明漂移）。

---

## 三、方案 C 详细设计

### 3.1 中间件挂载位置变更

**现状**（`WebAppBuilder.cs` L244，路由前）：

```csharp
// UseWebSession() 内
_PipelineActions.Add(app => app.UseMiddleware<HttpAuthenticationMiddleware<TUserInfo>>(
    Microsoft.Extensions.Options.Options.Create(Options.HttpAuth)));
```

**改为**（AfterRouting 锚点——`UseRouting` 之后、`UseEndpoints` 之前）：

```csharp
// UseWebSession() 内——改用 AfterRouting 锚点桶（IStartupFilter 构建管道时排空）
_WebAdapter.PipelineContext.AddMiddleware(MiddlewareAnchor.AfterRouting,
    (app, opts) => app.UseMiddleware<HttpAuthenticationMiddleware<TUserInfo>>(
        Microsoft.Extensions.Options.Options.Create(opts.HttpAuth)));
```

**锚点可行性**（管线拓扑已证实）：`BeforeRoutingBuilder.L37 UseRouting()` → `L43 排空 AfterRouting 桶` → `RoutingBuilder.AfterRouting.L21 UseEndpoints`——AfterRouting 桶恰在路由命中之后、端点执行之前，`context.GetEndpoint()` **可用**。

**JwtAuthentication 不动**：仍留 BeforeAuthentication 桶（路由前验签恢复 DomainUser 到 `Items`，供 HttpAuth 判定）。

### 3.2 `RequiresAuthentication` 判定升级

```csharp
private bool RequiresAuthentication(HttpContext context)
{
    // ① 端点 AllowAnonymous 元数据——单一权威事实源（扩展声明即放行）
    var endpoint = context.GetEndpoint();
    if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        return false;

    // ② 无端点匹配（未路由命中）——透传让框架返 404（不泄露端点存在性，比 401 更安全）
    if (endpoint is null)
        return false;

    // ③ ExcludedPathPrefixes 兜底（非端点场景——/Setup 静态页、GraphQL 子路径等）
    var pathStr = context.Request.Path.Value ?? "";
    foreach (var prefix in options.Value.ExcludedPathPrefixes)
        if (pathStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

    return true;
}
```

- **签名变更**：`RequiresAuthentication(PathString)` → `RequiresAuthentication(HttpContext)`（调用点同步改）
- **移除** `pathStr.Contains('.')` 静态文件启发式（静态文件经 `UseStaticFiles` 路由前已处理，或无端点透传 404）——语义收敛到真实端点判定
- **ExcludedPathPrefixes 降为兜底**：保留 `_LifelinePrefixes` 强制追加、保留消费方自定义前缀（`SetExcludedPathPrefixes` 语义不变）——**非端点场景向后兼容**

### 3.3 `InvokeAsync` 主体不变（三段逻辑保留）

| 段 | 逻辑 | 处置 |
|----|------|------|
| OPTIONS 预检透传 | `HttpAuthCheckPerformed=false` | 保留（CORS 中间件已处理 200，本分支冗余无害） |
| `user == null \|\| !Enabled` 透传 | 无会话/已禁用 | 保留 |
| `user.IsAuthenticated` 设标记透传 | HttpAuthCheckPerformed=true | 保留 |

仅 `RequiresAuthentication` 调用点改签名（`InvokeAsync` 内 `if (RequiresAuthentication(...))` → `if (RequiresAuthentication(context))`）。

### 3.4 HttpAuthCheckPerformed 标记时序保持

AfterRouting 桶设标记 → UseEndpoints 内端点执行 → `AuthorityFilterAttribute`（AOP 层）读标记——**时序严格保持**（AfterRouting 严格早于 UseEndpoints）。401 早短路防 AOP 层进入的性质不变。JwtAuthenticationMiddleware 仍先于 HttpAuth 恢复 DomainUser（BeforeAuthentication 桶）。

### 3.5 幂等与安全边界

- **幂等**：HttpAuth 仅 `UseWebSession` 注册一次，AfterRouting 桶内无重复
- **响应体不变**：SessionExpiredMessage / NotAuthenticatedMessage / ContentType 保持——前端错误处理不受影响
- **安全边界**：AllowAnonymous 由扩展**编译期声明**（`[AllowAnonymous]`/`.AllowAnonymous()`），非表现层配置——消费方无法通过 Web 配置关闭扩展匿名面（防绕过）
- **GraphQL 兼容**：`/api/graphql` 当前靠 `UseApiService` 自动追加排除——若 GraphQL 端点有 `[AllowAnonymous]` 则自动放行；无则走 ③ 兜底——**向后兼容**

---

## 四、受益扩展适配清单（方案 C 下零代码适配）

方案 C 的核心价值：**扩展无需任何改动**——AllowAnonymous 声明（已在端点映射时声明）即自动生效。适配清单仅作**验证面**：

| 扩展 | 验证端点 | 期望行为 |
|------|---------|---------|
| Federation | `POST /feberation/wechat/login`、`GET /feberation/jwks`、`POST /feberation/wechat/event` 等 9 匿名 | 无 Bearer → 放行到 handler |
| Federation | `POST /feberation/trust/issue` | 无 Bearer → 401（handler 自检兜底 + 元数据非匿名） |
| AuthCenter | `POST /api/auth/sms/send-code`、`login/sms`、`refresh`、`ticket/exchange` 等 7 匿名 | 无 Bearer → 放行 |
| AuthCenter | `POST /api/auth/logout`、`GET /api/auth/verify`、`GET /api/auth/grants` | 无 Bearer → 401 |
| Identity（SG1b） | `POST /auth/register`、`POST /auth/login` | 无 Bearer → 放行 |
| HealthCheck | `GET /health` | 放行（保持，生命线前缀仍兜底） |
| Notifications.SignalR | `negotiate`（RequireAuthorization 默认） | 401（保持） |

---

## 五、风险清单与缓解

| # | 风险 | 影响 | 缓解 |
|---|------|------|------|
| 1 | **未路由命中行为变更**（非排除+未认证路径 401 → 404） | 安全语义变化；测试断言可能锁旧行为 | 404 更安全（不泄露端点存在性）；框架测试补充未路由路径断言；变更记录显式标注 |
| 2 | **JwtAuth/HttpAuth 时序拉开**（中间隔 UseRouting） | 无功能影响 | DomainUser 已写入 Items（ContextExtraction+JwtAuth 在路由前完成） |
| 3 | **OPTIONS 预检** | CORS 中间件已返回 200 | HttpAuth OPTIONS 分支保留（冗余无害） |
| 4 | **AuthorityFilter 时序** | 标记在 AfterRouting 设、端点执行内读 | 时序正确（AfterRouting 早于 UseEndpoints） |
| 5 | **测试宿主适配** | 黑盒哨兵冒烟锚点分桶断言需更新 | 更新测试断言：HttpAuth 锚点断言改 AfterRouting；新增端点级匿名放行 + 非匿名 401 测试 |
| 6 | **GraphQL 路径** | `/api/graphql` 依赖排除列表追加 | 有 AllowAnonymous 自动放行；无则 ③ 兜底——向后兼容 |

---

## 六、验收标准

1. **框架测试**（`_TKWF/_Tests/Domain.Web.Tests`）：
   - HttpAuthenticationMiddleware 锚点 = AfterRouting（装配断言）
   - 端点级匿名放行：`[AllowAnonymous]` 端点无 Bearer → 200（非 401）
   - 端点级非匿名 401：无 `[AllowAnonymous]` 端点无 Bearer → 401（早短路防 AOP）
   - 未路由路径 → 404（透传）
   - ExcludedPathPrefixes 兜底：`/Setup` 等非端点路径仍放行
   - 既有用例全绿（锚点分桶断言更新后）
2. **扩展侧验证**：Federation/AuthCenter/Identity SG1b 匿名端点从容带全链测试通过
3. **全量回归**：主框架 slnx + 扩展 slnx 构建 0 错误；受影响面测试全绿
4. **文档同步**：框架 HttpAuthentication 使用指南 + 扩展指南 + tkwf-extension skill 更新

---

## 七、变更记录

| 日期 | 版本 | 内容 |
|------|------|------|
| 2026-10-14 | v0.1 | 方案定稿：背景/根因/选型（Oracle APPROVE 方案 C）/详细设计（锚点后移 + 判定升级 + 兜底保留）/适配清单/风险/验收 |