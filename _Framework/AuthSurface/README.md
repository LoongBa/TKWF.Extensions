# TKWF.Ext.AuthSurface

> TKWF 扩展：**授权面**——口令兑换体系（code/redemption/grant/batch + 管理端 v0.2.0）+ 我的应用聚合 + 授权快照候选。
> **数据属主** = 兑换/授权记录（非账号）——行级身份引用一律 `AuthAccount.UId`（前置裁定 P4 白名单）。
> **P2 FREEZE**（前置裁定 §三）：不实现 UserCenter 预留 `IRedemptionHistorySource`/`IUserAppsSource` 契约——自建跨扩展 VEntity（`TKWFV_UserRedemptionHistory`/`TKWFV_UserApps`）+ 授权面自有查询门面，消费方兑换/应用查询**不经 UserCenter 门面**。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.AuthSurface` |
| 版本 | V0.1.0（独立起点——2026-10-07 立项，Oracle4 评审 PASS WITH CONDITIONS） |
| 依赖 | `TKWF.Domain`（CPM）+ `TKWF.Utility`（v4.10.67 `IRateLimitCheck` 点检查原语——兑换频控）+ `TKWF.CodeGeneration`（SG1 纯 Analyzer）+ **ProjectReference `UserCenter.Abstractions`**（DTO 复用——`RedemptionRecordDto`/`UserAppDto` 页面零适配，L2 门控合规）+ FreeSql |
| 数据 | 表 `RedemptionCode` + `AuthApp`（本扩展）；视图 `TKWFV_UserRedemptionHistory`/`TKWFV_UserApps`（跨扩展 JOIN AuthCenter `TKWF_AuthAccount`/`AuthGrant`——视图归本扩展，P3） |
| 边界 | **零扩展间实现依赖**（ViewSql 字符串跨扩展引用 AuthCenter 表名，无代码引用——L2 零 TKWF0022） |

## 架构分层

```
IRedemptionCommandService / RedemptionCommandService    # 兑换写入门面（创建码/兑换——CAS 原子 + 频控）
IRedemptionQueryService / RedemptionQueryService        # 兑换历史查询门面（P2 FREEZE 自有门面）
IUserAppsQueryService / UserAppsQueryService            # 我的应用查询门面（跨扩展视图读模型）
IAuthAppService / AuthAppService                        # 应用目录管理门面（"我的应用" AppName 来源）
├── RedemptionCodeEntityDataService                     # SG1 DataService（生成/查询/CAS 原子兑换/惰性过期）
├── AuthAppEntityDataService                            # SG1 DataService（目录 AppId 唯一）
├── UserRedemptionHistoryViewDataService                # VEntity 只读 DataService（跨扩展视图）
└── UserAppsViewDataService                             # VEntity 只读 DataService（跨扩展视图）

IRateLimitCheck（v4.10.67 点检查原语——MemoryRateLimitCheck fallback）  # 兑换尝试频控（防无效码爆破）
RedemptionCodeGenerator                               # 兑换码生成器（CSPRNG + 脱敏 + SHA256 哈希——明文不落库）
RedemptionErrorCodes                                  # 兑换业务错误码（镜像 AuthCenter OAuthTicketErrorCodes 范式）
```

- **数据访问红线**：实体 SG1 + xCodeGen DataService（ADR61 自动注册）；VEntity 只读 DataService（`DomainReadOnlyDataServiceBase` + `IEntityReadOnlyDAC`——框架只读路径）；门面继承 `DomainServiceBase`（经基类 `User`）+ `AddConstructibleService` 注册 + DataService 经 `User.Use<T>()` NoAop 懒加载——**零 IFreeSql/IEntityDAC 直注入、零手写 Store**。
- **门面范式（ADR90）**：接口 `: IDomainService` + 实现 `internal sealed : DomainServiceBase` + `[DiContractIgnore]`；消费方 `User.Use<接口>()` 解析（守卫工厂帧内）。

## 核心能力

### 兑换（口令兑换体系——v0.1.0 兑换核心）

| API | 说明 |
|-----|------|
| `CreateCodeAsync(productName, targetAppId, validity?)` | 创建兑换码（单码——v0.1.0 无批次管理端）——CSPRNG 生成 `EDU-XXXX-XXXX-XXXX`（12 位 base32 去易混淆字符，熵约 30bit）；**明文一次性返回**（不落库不日志），库中仅存 `CodeHash`（SHA256 十六进制 64 唯一）+ `CodeMasked`（脱敏展示） |
| `RedeemAsync(userId, codeInput)` | 兑换——**必须已登录**（Oracle4 P1-1 裁定 A：userId 必填，频控按 userId 维度；未登录无法兑换，匿名兑换 v0.2.0 评估）；校验（无效/已兑/过期）→ **CAS 原子兑换**（`EntityUpdateWhereAsync` 单语句 WHERE 谓词 `Id==id && Status==0 && 未过期`——防并发双兑，败者重查判定）→ 返回 `RedemptionRecordDto`（已脱敏） |

失败业务异常（`AuthenticationException`，错误码见 `RedemptionErrorCodes`）：`REDEMPTION_CODE_INVALID` / `_USED` / `_EXPIRED` / `REDEMPTION_TOO_MANY_ATTEMPTS`（HTTP 映射归装配实例）。

### 我的应用 / 兑换历史（跨扩展读模型——P2 FREEZE 自有门面）

| API | 说明 |
|-----|------|
| `IUserAppsQueryService.GetAppsAsync(userId)` | 我的应用——`TKWFV_UserApps` 视图（JOIN `AuthGrant` + `TKWF_AuthAccount` + `AuthApp` LEFT JOIN 取 AppName）；**有效过滤在 DataService**（Status=Active 且 ValidUntil 未过——C# 侧 UtcNow 无方言风险）；返回 `UserAppDto`（`UsageSummary` 恒 null——儿童数据红线） |
| `IRedemptionQueryService.GetRedemptionsAsync(userId)` | 兑换历史——`TKWFV_UserRedemptionHistory` 视图（JOIN `RedemptionCode` + `TKWF_AuthAccount`——INNER JOIN 天然排除未兑换行）；返回 `RedemptionRecordDto`（code 已脱敏透传） |

> ⚠️ **v0.1.0 业务语义（Oracle4 P1-3 明示）**：兑换成功后应用**不会**出现在"我的应用"列表——v0.1.0 兑换不写 `AuthGrant`（`Source=redeem` 预留 v0.2.0）；兑换历史可见。消费方页面应区分"兑换记录"与"应用授权"两栏。

### 应用目录（"我的应用" AppName 来源）

`IAuthAppService.CreateAppAsync/UpdateAppAsync/SetEnabledAsync`——AppId 唯一目录（OAuth2 client 注册表语义）；软引用（无 FK——删除前检查引用，建议 `SetEnabledAsync(false)` 软禁用）。

## Options 配置（`TKWF:AuthSurface`）

| 键 | 默认 | 说明 |
|----|------|------|
| `RedemptionAttemptWindowMinutes` | `60` | 兑换尝试频控窗口（分钟——用户维度无效码尝试计数窗口） |
| `RedemptionAttemptMaxAttempts` | `10` | 窗口内无效码尝试上限（防批量枚举爆破——12 位 base32 码熵下 10 次爆破概率极低） |

频控实现：框架 `IRateLimitCheck` 点检查原语（v4.10.67——`MemoryRateLimitCheck` fallback 经 `TryAddSingleton`；R2 `SqlCountRateLimitCheck`（RateLimiting 扩展）落地后可替换，TryAdd 语义）。key 规范化 `redemption:redeem:{userId}`；流程 = 前置只读 `GetRemaining <= 0` 拦截（不计数）→ 无效路径 `TryAcquire` 计失败 → 成功兑换不计数。

## 约束与语义

- **P2 FREEZE（前置裁定 §三）**：`IRedemptionHistorySource`/`IUserAppsSource` 契约保留于 UserCenter.Abstractions（装配层适配器向后兼容）但**本扩展禁止实现**——`IUserCenterQueryService.GetRedemptionsAsync/GetAppsAsync` 保持缺省降级（空列表）；消费方查询经本扩展门面。
- **行级 FK = `AuthAccount.UId`**（P4 白名单）——`RedemptionCode.RedeemedByUId`；`FederationAnchorOpenId` **不作引用键**（P2/P4——N2 anchor 冗余快查列）。
- **兑换码安全**：明文不落库（SHA256 哈希唯一）——防库泄露即任意兑换；`CodeMasked` 契约脱敏透传（UserCenter 不存明文）。
- **并发双兑**：CAS 原子兑换（ADR89 单语句 WHERE 谓词）——败者重查判定（已兑/过期）。
- **视图前缀**：`TKWFV_` 暂用（前置裁定 P3 / ADR C.16——框架组前缀批次核查后视情况 rename，不影响实体/ViewSql 设计）。
- **表名快照**：ViewSql 引用 `AuthGrant`/`TKWF_AuthAccount`（AuthCenter V0.9.0 身份域重构——ADR100 表名别名落地；`AuthAccount` 已改 `TKWF_AuthAccount` 凭据白名单列，档案列迁 `UserProfileEntity`）。
- **SQLite 测试已知限制**（2026-10-07 实证）：FreeSql SQLite provider 对 UTC DateTime 列读回偏移（+7h）——过期场景门面 C# 判定受偏移影响不走进度翻转分支，但 **CAS 谓词（SQL 侧）兜底拒绝**——生产 PG/SQL Server 无偏移（门面判定 + CAS 一致）。
- **删除语义**：无软删除（`hasSoftDelete:false`——兑换码/应用目录物理操作，运营侧经门面）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.AuthSurface;

// ⚠️ 依赖认证中心（vm 视图 JOIN AuthGrant/TKWF_AuthAccount 基表）——同时白名单声明
[TKWFEnabledExtension(typeof(AuthSurfaceExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 消费（V0.1.0 门面范式——User.Use<接口>() AOP；禁构造注入 DI004）
public class RedeemService : DomainServiceBase
{
    public RedeemService(IDomainUser user) : base(user) { }

    public async Task<RedemptionRecordDto> RedeemAsync(string currentUserId, string code)
        => await User.Use<IRedemptionCommandService>().RedeemAsync(currentUserId, code);

    public async Task<IReadOnlyList<UserAppDto>> MyAppsAsync(string currentUserId)
        => await User.Use<IUserAppsQueryService>().GetAppsAsync(currentUserId);
}
```

三钩子自动接线。DI 注册形态：4 门面 `AddConstructibleService`（接口守卫工厂 + 实现 throw-factory——域作用域外解析抛）；实体/VEntity DataService SG1/ADR61 自动注册（Initializer 零手动）；`IRateLimitCheck` `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>`（v4.10.67 fallback——未启用 RateLimiting 扩展时频控不消失）。

## 数据模型

```sql
RedemptionCode(id BIGINT PK, code_hash VARCHAR(64) UNIQUE, code_masked VARCHAR(32), product_name VARCHAR(100),
               target_app_id VARCHAR(100), status INT,            -- 0 Available / 1 Redeemed / 2 Expired
               expire_at_utc DATETIME NULL, redeemed_by_uid VARCHAR(32) NULL,   -- 行级 FK=AuthAccount.UId
               redeemed_at_utc DATETIME NULL, create_time, update_time)
AuthApp(id BIGINT PK, app_id VARCHAR(100) UNIQUE, app_name VARCHAR(100), icon VARCHAR(512) NULL,
        is_enabled BOOLEAN, create_time, update_time)
-- 视图（跨扩展——生产 DBA 手动建，依赖 AuthCenter 表）
TKWFV_UserRedemptionHistory  = RedemptionCode INNER JOIN TKWF_AuthAccount (RedeemedByUId = UId)
TKWFV_UserApps               = AuthGrant INNER JOIN TKWF_AuthAccount (UserId = UId) LEFT JOIN AuthApp (AppId)
```

- 索引：`UX_RedemptionCode_CodeHash`（唯一——一码一兑）/ `UX_AuthApp_AppId`（唯一）。
- 生产建表：框架 `SyncTables` 托管本扩展表（ADR49）；**视图生产需 DBA 手动执行 ViewSql**（依赖扩展表清单见使用指南 §六）。

## 与认证中心 / 用户中心划界

| 扩展 | 边界 |
|------|------|
| `TKWF.Ext.AuthCenter`（V0.9.0 重构中） | 身份锚 `AuthAccount.UId` = 本扩展行级 FK；`AuthGrant`（login 源）为"我的应用"数据底座；`AuthGrantSources.Redeem` 预留 v0.2.0 兑换授权写入（v0.1.0 不写——评估 AuthCenter 门面 Source 参数化 or 授权面委托） |
| `TKWF.Ext.UserCenter` | **P2 FREEZE**：本扩展不实现两 Source 契约；UserCenter 两方法保持降级空列表；DTO 复用 UserCenter.Abstractions（页面零适配） |

## 后续演进（v0.2.0+ 候选）

批次管理端（批量生成/分销/导出/作废——`BatchId` 列 + 管理门面 + `[GenerateController]`）；**兑换授权写入 AuthGrant（Source=redeem）**；分布式频控（SqlCountRateLimitCheck/Redis）；分页查询；兑换尝试日志持久化（审计回溯）；授权快照/verify 聚合；匿名兑换（兑换创建账号）。

## 测试基线

`Extension.AuthSurface.Tests`——**20 用例全绿**（兑换链路端到端/负路径/并发 CAS 单胜/频控/跨扩展视图 JOIN/仅本人/目录 CRUD/守卫工厂装配断言；跨扩展基表原生 SQL 模拟——AuthCenter V0.9.0 完成后切 ProjectReference + 双白名单集成）。

<!-- EOF -->
