# TKWF.Ext.SecurityLog 安全日志扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (安全事件记录与查询) | **框架**: .NET 10

**核心约束**: 认证/授权安全事件自动记录（AOP 过滤器采集，不改主框架）+ 只增不改（append-only）+ 数据访问红线合规（全走 SG1 DataService）+ 异常静默 + 查询 API（列表 DTO 不含 Detail）

---

## 一、定位

**安全事件记录**——登录成功/失败、登出、改密、密码重置、账户锁定、注册、挑战等认证事件自动落库（含 IP/UA/结果/详情/关联 ID），并提供分页/过滤查询 API。**安全日志是登录历史的唯一数据源**（Account V0.3.0 登录历史消费本扩展查询，不重复建表）。

### 与 AuditLogging 划界

| 维度 | AuditLogging | SecurityLog（本扩展） |
|------|-------------|----------------------|
| 记录对象 | 方法调用事件（ServiceName/MethodName/ArgumentsJson/DurationMs） | 安全事件（EventType/IpAddress/UserAgent/Result） |
| 采集点 | `FilterBuilder.AddAuditLog()` 全局（声明式排除） | `FilterBuilder.AddSecurityLog()` 全局（**CanWeGo 正向白名单**，只拦安全方法） |
| 匿名事件 | 默认不记（IsEnabledForAnonymous=false） | **默认记录**（登录失败匿名事件是核心场景） |
| 数据语义 | 业务操作追踪 | 认证/授权留痕（暴力破解/合规审计） |

两者并存：方法调用审计归 AuditLog，安全事件归 SecurityLog。

---

## 二、安装接线

### 1. 消费方引用 + 白名单启用（V4.9.85 必需）

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\..\_Framework\SecurityLog\TKWF.Ext.SecurityLog.csproj" />
```

```csharp
using TKWF.Ext.SecurityLog;

[TKWFEnabledExtension(typeof(SecurityLogExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // 启用后自动注册：ISecurityLogStore + ISecurityLogQueryService + ISecurityLogAnalyticsService + SecurityLogEntityDataService + Options
}
```

### 2. 启用采集过滤器（opt-in，对齐 AddAuditLog 先例）

```csharp
protected override void ConfigureGlobalFilters(FilterBuilder<MyUserInfo> builder)
{
    builder.AddSecurityLog();   // 全局注册——CanWeGo 白名单只拦截 AuthController/IPasswordResetFlow 安全方法
}
```

采集路径（C1 实测确认）：**Domain AOP 过滤器**——`AuthController` 经 `User.Use<IAuthController>()` 调用时由装饰器路由进 `StaticDomainInterceptor`，全局过滤器随管线执行；`IPasswordResetFlow` 实现作为 AOP 目标或方法名命中时同样拦截。

### 3. 查询安全事件

```csharp
public class SecurityAuditService(ISecurityLogQueryService queryService)
{
    // 按 IP + 事件类型分页查询（暴力破解分析）
    var result = await queryService.GetListAsync(new SecurityLogQueryInput
    {
        IpAddress = "203.0.113.7",
        EventType = "Login",
        Result = "Failed",
        Take = 50
    });

    // 详情（含 Detail 全文）
    var detail = await queryService.GetDetailAsync(result.Items[0].Id);

    // 统计失败次数
    var failedCount = await queryService.CountAsync(new SecurityLogQueryInput
    {
        UserName = "alice",
        Result = "Failed"
    });
}
```

### 4. 配置选项

`appsettings.json`：

```json
{
  "TKWF": {
    "SecurityLog": {
      "Enabled": true,
      "EventTypes": ["Login", "Logout", "Lockout"]   // 空 = 全部；false 时过滤器零开销
    }
  }
}
```

---

## 三、事件类型与采集点

| EventType | 拦截方法 | Result 判定 |
|-----------|---------|------------|
| Login | `LoginByPasswordAsync` / `LoginByContextAsync` | 正常返回 = Success；`AuthenticationException` = Failed + Detail（异常消息，脱敏） |
| Logout | `LogoutAsync` | 正常返回 = Success |
| PasswordChange | `ChangePasswordSecureAsync` | 返回值 `RegisterResult.Success` |
| PasswordReset | `InitiateResetAsync` / `CompleteResetAsync`（`IPasswordResetFlow` 实现） | 返回值 `bool` / `ResetResult.Success` |
| Lockout | `LoginByContextAsync` 锁定分支 | **判定（C4）**：`AuthenticationException` 消息含"锁定"关键字 |
| Register | `RegisterSecureAsync` | 返回值 `RegisterResult.Success` |
| Challenge | `RequestChallengeAsync` | 正常返回 = Success |

**Result 判定细则**：
- 异常路径仅 `AuthenticationException` 记 Failed；**其他异常不记录**（保持原异常语义，不吞不遮蔽）。
- 成功路径按返回值承载的业务失败判定（`RegisterResult`/`ResetResult`/`bool` 为 false → Failed）。
- `UserName` = **尝试用户名**（请求输入优先，防枚举语义下保留审计来源）；登录成功后回填 `UserId`。

---

## 四、只增不改语义（Oracle C2）

- `SecurityLogEntity` / `SecurityLogEntityDataService` **无 Update/Delete 业务方法**、**不标 `[GenerateController(FromDataService=true)]`**——不生成任何管理端点。
- 唯一写路径：`SecurityLogFilterAttribute` → `ISecurityLogStore.SaveAsync` → `SecurityLogEntityDataService.EntityCreateAsync`（追加写）。
- `CreateTime` 列 `[Column(CanUpdate = false)]` 列级兜底。
- 数据访问红线合规：Store/QueryService **不注入 IFreeSql / IEntityDAC**，全走 SG1 DataService。

## 五、实体表结构

`SecurityLogEntity` → `SecurityLog` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| EventType | NVARCHAR(32) | Login/Logout/PasswordChange/PasswordReset/Lockout/Register/Challenge |
| EventCategory | NVARCHAR(32) | Authentication（v0.1.0 均为 Authentication） |
| UserName | NVARCHAR(128) | 尝试用户名（登录失败 = 请求输入） |
| UserId | BIGINT? | 认证成功后回填 |
| IpAddress | NVARCHAR(64) | 客户端 IP（IAmbientContext["ClientIp"]） |
| UserAgent | NVARCHAR(512) | 客户端 UA（IAmbientContext["UserAgent"]，无则 null） |
| Result | NVARCHAR(16) | Success/Failed |
| Detail | NVARCHAR(MAX) | 详情（异常消息，脱敏——不含密码/令牌） |
| CorrelationId | NVARCHAR(64) | 关联 ID（链路追踪） |
| CreateTime | DATETIME (UTC) | 记录创建时间 |

**索引**：`IX_SecurityLog_UserEventTime`（UserName+EventType+CreateTime）/ `IX_SecurityLog_IpTime`（IpAddress+CreateTime）

> 注：Detail 用 `StringLength = -1`（nvarchar(max) 等价，跨 Provider 映射），对齐 JobResultEntity 先例。

## 六、查询 API（列表 DTO 不含 Detail）

- `GetListAsync(SecurityLogQueryInput)`：过滤 UserName（LIKE）/IpAddress（LIKE）/EventType（精确）/Result（精确）/FromUtc/ToUtc（CreateTime UTC）+ Skip/Take（默认 50 上限 200）；**`SecurityLogListItemDto` 不含 Detail**（安全决策 D5：Detail 含异常消息，列表不拉大字段/敏感信息）。
- `GetDetailAsync(long id)`：含 Detail 全文。
- `CountAsync(SecurityLogQueryInput)`：同条件计数。

## 七、边界

- ✅ **异常检测聚合**（失败次数 TopN by 用户名/来源 IP）——v0.2.0 已实施（`ISecurityLogAnalyticsService`：GetTopFailedUsersAsync / GetTopFailedIpsAsync，内存 GroupBy 范式，对齐 Tagging GetFrequencyAsync）
- ✅ **保留天数清理**——v0.2.0 已实施（`SecurityLoggingOptions.RetentionDays` 默认 90 + `CleanupBatchSize` 默认 500；`ISecurityLogAnalyticsService.CleanupExpiredAsync` 分批清完）
  > **决策记录（打破"只增不改" Oracle C2）**：保留清理引入**物理删除**路径——合规留存窗口（默认 90 天）下过期记录必须可回收，否则日志无限膨胀。限定：① 仅 `DeleteExpiredAsync` 单点物理删（`hasSoftDelete:false`，绝不走 `EntitySoftDeleteAsync`——对未启用软删实体抛 InvalidOperationException）；② 无管理端点/单条删除；③ `CreateTime` 仍 `CanUpdate=false`（列级不可改）、聚合仍只读。**主框架 ADR 待用户许可后补录**，本次以代码注释 + 本 README 承担决策记录职责。
- ❌ 安全事件实时推送（WebSocket/SignalR）——v0.2.0 之后（超出后端扩展范围）
- ❌ Account"登录历史"UI/接口——Account V0.3.0 消费本扩展查询
- ❌ 修改主框架（AuthController/IAccountLockoutPolicy 不动）

## 八、与主框架的关系

- `SecurityLogFilterAttribute<TUserInfo>` 派生 `DomainFilterAttribute<TUserInfo>` + 实现 `IExceptionAwareFilter`（异常路径触发 PostProceed）——对齐 `AuditLogFilterAttribute` 模式。
- IP 经 `IAmbientContext["ClientIp"]`（对齐 `AuthController.GetClientIp`）；`CanWeGo` 白名单判定 `Target is IAuthController` / `Target is IPasswordResetFlow` / 方法名集合。
- `ISecurityLogStore` / `ISecurityLogQueryService` / `SecurityLogEntry` 均为扩展侧自建接口（不修改主框架）。

## 九、组件清单（Component List）

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`SecurityLogEntity`** | 安全日志表实体（SG1 声明式，11 列，只增不改 + CreateTime UTC） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`SecurityLogFilterAttribute<TUserInfo>`** | 安全事件采集过滤器（Domain AOP + CanWeGo 白名单 + Result/Lockout 判定） | 内置（消费方 opt-in：`builder.AddSecurityLog()`） |
| **`ISecurityLogStore`** | 安全事件写入存储（追加写） | `SecurityLogStore`（internal sealed，委托 DataService） |
| **`ISecurityLogQueryService`** | 分页/过滤查询 + 详情 + 计数（列表 DTO 不含 Detail） | `SecurityLogQueryService`（internal sealed，委托 DataService） |
| **`ISecurityLogAnalyticsService`** | v0.2.0 异常检测聚合（失败次数 TopN by 用户名/来源 IP）+ 保留天数清理 | `SecurityLogAnalyticsService`（internal sealed，委托 DataService + IOptions + ILogger，异常静默） |
| **`SecurityLogEntityDataService`** | SG1 DataService——CRUD 转发 + v0.2.0 聚合（GetTopFailedByUserAsync/GetTopFailedByIpAsync）+ 清理（DeleteExpiredAsync） | xCodeGen 生成（.g.cs + 手写分部业务方法） |
| **`SecurityLogEventTypes`** | v0.2.0 事件类型/结果/分类字符串常量（收敛字面量） | 内置静态类 |
| **`SecurityLoggingOptions`** | 配置（`TKWF:SecurityLog`）：Enabled / EventTypes / v0.2.0 RetentionDays(90) + CleanupBatchSize(500) | 内置，`[Options]` + SG1 绑定 |
| **`SecurityLogExtensionInitializer`** | 扩展初始化器（三钩子：注册 Options + DataService + Store + QueryService + AnalyticsService） | 内置，`[TKWFExtension]` SG1 发现 |

<!-- EOF -->
