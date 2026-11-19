# TKWF.Ext.AuthSurface

> TKWF 扩展：**授权面**——口令兑换体系（code/redemption/grant/batch + 批次管理端 v0.3.0）+ 我的应用聚合 + 授权快照候选。
> **数据属主** = 兑换/授权记录（非账号）——行级身份引用一律 `AuthAccount.UId`（前置裁定 P4 白名单）。
> **✅ UserCenter 退役（2026-10-08）**：UserCenter 独立扩展已完整删除——兑换/应用查询唯一通道 = 本扩展自有门面（跨扩展 VEntity `TKWFV_UserRedemptionHistory`/`TKWFV_UserApps`）；DTO（`RedemptionRecordDto`/`UserAppDto`）归本扩展主包（`Contracts/AuthSurfaceDtos.cs`）。原"P2 FREEZE（不实现 UserCenter Source 契约）"已随 UserCenter 删除完结——无契约可 Freeze。
> **分发形态：自包含型**（框架 A6 裁定，v4.10.122；G17A §7.8 / G17B §10.2）——零跨扩展契约消费，DTO 归主包 `Contracts/`；终端应用**单包引用**。本扩展**不存在也未创建** `*.Abstractions` 契约包（非分发缺口）；升格契约型须待真实跨扩展消费方出现后再抽契约包，禁止先建空 Abstractions。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.AuthSurface` |
| 版本 | V0.1.0（独立起点——2026-10-07 立项，Oracle4 评审 PASS WITH CONDITIONS）+ **v0.2.0（兑换码附加信息——核验场景，2026-10-10 已发布）** + **v0.1.1（UserCenter 退役——DTO 归主包）** + **v0.2.1（软禁用——视图 IsEnabled 投影 + NULL 视同启用，已发布 tag）** + **V0.3.0（批次管理端 + 码侧契约——T-0~T-8：`CodeBatch` 批次域 / BatchId 列 / Revoked 状态 / expireAt 参数 / 按明文读码 / 分页 / payload 解密；AuthCenter `RecordGrantAsync` 泛化协同；已含于 v0.3.1 发布）** + **v0.3.1（批次查询性能整改——分组统计 `GroupByAsync` 单查询 + 明细分页 SQL 下推；CPM lockstep 4.10.121（框架 GroupByAsync 整键排序 NRE 修复响应）；2026-11-17 已发布）** |
| 依赖 | `TKWF.Domain`（CPM）+ `TKWF.Utility`（v4.10.67 `IRateLimitCheck` 点检查原语——兑换频控）+ `TKWF.CodeGeneration`（SG1 纯 Analyzer）+ FreeSql——**DTO 归本扩展主包**（`Contracts/AuthSurfaceDtos.cs`：`RedemptionRecordDto`/`UserAppDto`，零跨扩展契约依赖） |
| 数据 | 表 `TKWF_RedemptionCode` + `TKWF_AuthApp`（本扩展）；视图 `TKWFV_UserRedemptionHistory`/`TKWFV_UserApps`（跨扩展 JOIN AuthCenter `TKWF_AuthAccount`/`TKWF_AuthGrant`——视图归本扩展，P3） |
| 边界 | **零扩展间实现依赖**（ViewSql 字符串跨扩展引用 AuthCenter 表名，无代码引用——L2 零 TKWF0022） |

## 架构分层

```
IRedemptionCommandService / RedemptionCommandService    # 兑换写入门面（创建码/兑换——CAS 原子 + 频控）
IRedemptionQueryService / RedemptionQueryService        # 兑换历史查询门面（P2 FREEZE 自有门面）+ V0.3.0 读码/payload/分页
ICodeBatchService / CodeBatchService                    # V0.3.0 批次管理门面（创建/生成回调/删除/列表统计/明细/整批与单码吊销）
IUserAppsQueryService / UserAppsQueryService            # 我的应用查询门面（跨扩展视图读模型）
IAuthAppService / AuthAppService                        # 应用目录管理门面（"我的应用" AppName 来源）
├── RedemptionCodeEntityDataService                     # SG1 DataService（生成/查询/CAS 原子兑换/惰性过期/吊销）
├── CodeBatchEntityDataService                          # V0.3.0 SG1 DataService（批次 CAS 抢占/落定/回滚/删除）
├── AuthAppEntityDataService                            # SG1 DataService（目录 AppId 唯一）
├── UserRedemptionHistoryViewDataService                # VEntity 只读 DataService（跨扩展视图）
└── UserAppsViewDataService                             # VEntity 只读 DataService（跨扩展视图）

IRateLimitCheck（v4.10.67 点检查原语——MemoryRateLimitCheck fallback）  # 兑换尝试频控（防无效码爆破）
RedemptionCodeGenerator                               # 兑换码生成器（CSPRNG + 脱敏 + SHA256 哈希——明文不落库）
RedemptionErrorCodes / BatchErrorCodes                # 兑换/批次业务错误码（镜像 AuthCenter OAuthTicketErrorCodes 范式）
```

- **数据访问红线**：实体 SG1 + xCodeGen DataService（ADR61 自动注册）；VEntity 只读 DataService（`DomainReadOnlyDataServiceBase` + `IEntityReadOnlyDAC`——框架只读路径）；门面继承 `DomainServiceBase`（经基类 `User`）+ `AddConstructibleService` 注册 + DataService 经 `User.Use<T>()` NoAop 懒加载——**零 IFreeSql/IEntityDAC 直注入、零手写 Store**。
- **门面范式（ADR90）**：接口 `: IDomainService` + 实现 `internal sealed : DomainServiceBase` + `[DiContractIgnore]`；消费方 `User.Use<接口>()` 解析（守卫工厂帧内）。

## 核心能力

### 兑换（口令兑换体系——v0.1.0 兑换核心）

| API | 说明 |
|-----|------|
| `CreateCodeAsync(productName, targetAppId, validity?, ct, payload?, expireAtUtc?)` | 创建兑换码（单码）——CSPRNG 生成 `EDU-XXXX-XXXX-XXXX`（12 位 base32 去易混淆字符，**熵 60bit**）；**SHA256 无需加盐**；**明文一次性返回**（不落库不日志），库中仅存 `CodeHash`（SHA256 十六进制 64 唯一）+ `CodeMasked`（脱敏展示）；**v0.2.0 核验场景**：`payload` 经 keyed `ISymmetricKeyProvider` AES-GCM 加密落 `PayloadEncrypted`（明文不落库）；**V0.3.0（T-4）**：`expireAtUtc`（末位可选，**绝对 UTC 瞬间**）与 `validity`（**券有效期相对时长**——`ExpireAtUtc = UtcNow + validity`）**互斥**（同传抛 `ArgumentException`）；优先级 `expireAtUtc ?? validity 映射` |
| `RedeemAsync(userId, codeInput)` | 兑换——**必须已登录**；校验（无效/已兑/过期/**已吊销**）→ **CAS 原子兑换**（`Status==0 && 未过期`）→ 返回 `RedemptionRecordDto`（已脱敏 + `Payload` 明文取回）；**V0.3.0（T-3）**：前置判定与 CAS 败者重查均含 `Status==3 → REDEMPTION_CODE_REVOKED`（403 语义，Revoked 路径同计频控） |

失败业务异常（`AuthenticationException`，错误码见 `RedemptionErrorCodes`）：`REDEMPTION_CODE_INVALID` / `_USED` / `_EXPIRED` / `REDEMPTION_CODE_REVOKED`（V0.3.0 吊销）/ `REDEMPTION_TOO_MANY_ATTEMPTS`（HTTP 映射归装配实例）。

### 批次管理（V0.3.0——批次域整体，转告 T-0~T-3 定稿）

| API | 说明 |
|-----|------|
| `ICodeBatchService.CreateBatchAsync(request)` | 创建批次（DRAFT）；通用校验（Name/ProductName/TargetAppId 非空、Quantity 1..`MaxBatchQuantity`、ExpireAtUtc 晚于 now、Channel≤100、ValidityDays>0）；BatchNo=`B{yyyyMMdd}{4 随机}`（`RandomNumberGenerator.GetInt32` 无偏，唯一冲突重试 ≤3 次） |
| `ICodeBatchService.GenerateBatchAsync(batchId, onPlainCode, ct)` | **生成即导出（回调主方案）**：CAS 抢占 DRAFT→GENERATING（谓词 `Status==0 && GeneratedCount==0`，败者 `BATCH_NOT_EDITABLE`）→ 逐码生成并经 `onPlainCode(明文, ct)` 一次性回调（门面不缓存/不落库/不落日志）→ 全量成功落 GENERATED + GeneratedCount + ExportedAtUtc；**回调异常/ct 取消 → 整体回滚 DRAFT**（已流出明文作废）；**同事务 CAS** ⇒ 进程崩溃 DB 回滚自然回 DRAFT 可重试 |
| `ICodeBatchService.DeleteBatchAsync(batchId)` | 仅 DRAFT 物理删除（DRAFT 无码）；非 DRAFT → `BATCH_NOT_EDITABLE`；不存在 → `BATCH_NOT_FOUND` |
| `ICodeBatchService.GetBatchesAsync(filter, page, pageSize)` | 批次列表 + **每批聚合统计单查询**（v0.3.1：`GroupByAsync (BatchId,Status,时间派生过期)` 复合键 SQL GROUP BY 全下推——替代 v0.3.0 N+1 逐批全量拉取；filter：状态/渠道/创建时间/名称模糊/Test；pageSize 截断 ≤100） |
| `ICodeBatchService.GetBatchAsync(batchId)` | 详情 + 统计 + 派生 `PartialRevoked`（`Status==GENERATED && RevokedCount>0 && 待兑+过期>0`） |
| `ICodeBatchService.GetBatchCodesAsync(batchId, page, pageSize, status?)` | 券明细分页（仅掩码/状态/归属/时间——**明文不出现**；越界=空页；v0.3.1：SQL 分页下推 `skip/take` + `CountAsync` 计数——替代 v0.3.0 全量取内存分页） |
| `ICodeBatchService.RevokeBatchAsync(batchId)` | 整批吊销：单语句条件更新（`BatchId==? && Status==0` → `Status=3`）+ 批次同事务 GENERATED→REVOKED；已兑不动；**吞没「已过期未翻转」码**（统计口径 expired→revoked）；REVOKED 幂等返回；DRAFT/GENERATING → `BATCH_NOT_EDITABLE` |
| `ICodeBatchService.RevokeCodeAsync(codeId)` | 单码吊销：`Id==? && Status==0` → `Status=3`，返回 bool（已兑/已吊销 → false 幂等不抛）；翻成功 → 同事务判定「该批 `Status==0` 计数==0」→ 批次升 REVOKED |

**统计口径契约（时间派生——禁按 `Status==2` 落库计数）**：`expired = Status==0 && ExpireAtUtc ≤ now`；`pending = Status==0 && (ExpireAtUtc is null || > now)`；`total = GeneratedCount`；不变式 `total = redeemed + pending + expired + revoked`（惰性翻转仅在兑换尝试时发生——未尝试的过期码恒 `Status=0`）。

**边界**：批次管理端点/CSV 呈现/审计归消费方表现层——扩展**不生成** `[GenerateController]` 管理端点（`CodeBatchEntityDataService` 不标 `FromDataService`）；消费方经 `User.Use<ICodeBatchService>()` 编排（`/api/edu/admin/code-batches` 族）。

### 我的应用 / 兑换历史（跨扩展读模型——P2 FREEZE 自有门面）

| API | 说明 |
|-----|------|
| `IUserAppsQueryService.GetAppsAsync(userId)` | 我的应用——`TKWFV_UserApps` 视图（JOIN `TKWF_AuthGrant` + `TKWF_AuthAccount` + `TKWF_AuthApp` LEFT JOIN 取 AppName）；**有效过滤在 DataService**（Status=Active 且 ValidUntil 未过，且**目录未软禁用**——IsEnabled=false 排除、NULL=目录未录入视同启用，v0.2.1 兑现）——C# 侧 UtcNow 无方言风险）；返回 `UserAppDto`（`UsageSummary` 恒 null——儿童数据红线） |
| `IRedemptionQueryService.GetRedemptionsAsync(userId, page?, pageSize?, ct)` | 兑换历史——`TKWFV_UserRedemptionHistory` 视图（JOIN `TKWF_RedemptionCode` + `TKWF_AuthAccount`）；返回 `RedemptionRecordDto`（code 已脱敏透传）；**V0.3.0（T-6）**：`page/pageSize` 可空（**null=全量**——向后兼容），分页=倒序窗口。⚠️ **`ct` 参数位次由第 2 位移至第 4 位**（具名调用兼容；位置传 `ct` 的调用需改具名） |
| `IRedemptionQueryService.GetByCodeAsync(codeInput, ct)` | **V0.3.0（T-5）** 按明文读码（CAS 前只读预检）——内部 SHA256 哈希，返回 `CodeBatchLookupDto`（Id/TargetAppId/Status/ExpireAtUtc/BatchId/BatchTest/Channel——头行两跳取批次 test/channel）；**不存在 → null**（消费方归 `REDEMPTION_CODE_INVALID` 防枚举）；**只读预检非兑换承诺**（CAS 仍唯一裁决）；**不计兑换频控**（消费方预检拒绝自计数） |
| `IRedemptionQueryService.GetPayloadAsync(codeId, ct)` | **V0.3.0（T-8）** 管理端 payload 解密——keyed `ISymmetricKeyProvider` 解密 `PayloadEncrypted`；**可重复只读**（每次解密返回，不消费不擦除）；无 payload/不存在 → null（消费方映射 `PAYLOAD_UNAVAILABLE`）；**明文不落日志**；鉴权归装配层（管理端二次鉴权） |

> ⚠️ **兑换授权语义（Oracle4 P1-3 明示；V0.3.0 起补全写入路径）**：扩展自身**不写** `AuthGrant`——兑换成功后应用默认**不出现**在"我的应用"列表；V0.3.0（T-7）起由**消费方兑换编排**调 AuthCenter `IAuthGrantCommandService.RecordGrantAsync(userId, appId, "redeem", validUntil, ct)` 写入（`ValidUntil` 按批次 `ValidityDays` 计算），编排后即聚合可见。兑换历史恒可见。消费方页面应区分"兑换记录"与"应用授权"两栏。

### 应用目录（"我的应用" AppName 来源）

`IAuthAppService.CreateAppAsync/UpdateAppAsync/SetEnabledAsync`——AppId 唯一目录（OAuth2 client 注册表语义）；软引用（无 FK——删除前检查引用，建议 `SetEnabledAsync(false)` 软禁用）。

## Options 配置（`TKWF:AuthSurface`）

| 键 | 默认 | 说明 |
|----|------|------|
| `RedemptionAttemptWindowMinutes` | `60` | 兑换尝试频控窗口（分钟——用户维度无效码尝试计数窗口） |
| `RedemptionAttemptMaxAttempts` | `10` | 窗口内无效码尝试上限（防批量枚举爆破——12 位 base32 码熵下 10 次爆破概率极低） |
| `SecretEncryptionKeyPath` | `null` | **v0.2.0 核验场景**——附加信息（核验业务信息）AES-GCM 密钥文件路径（keyed `ISymmetricKeyProvider`，键 AuthSurface；生产缺密钥 fail-fast / 开发随机兜底） |
| `IsProduction` | `true` | **v0.2.0 核验场景**——生产模式（true=缺密钥拒绝启动，禁 dev 随机兜底；开发置 false） |
| `MaxBatchQuantity` | `5000` | **V0.3.0**——单批计划数量上限（通用校验；业务白名单如 `{7,30,90,365}` 归消费方） |

频控实现：框架 `IRateLimitCheck` 点检查原语（v4.10.67——`MemoryRateLimitCheck` fallback 经 `TryAddSingleton`；R2 `SqlCountRateLimitCheck`（RateLimiting 扩展）落地后可替换，TryAdd 语义）。key 规范化 `redemption:redeem:{userId}`；流程 = 前置只读 `GetRemaining <= 0` 拦截（不计数）→ 无效路径 `TryAcquire` 计失败 → 成功兑换不计数。

> **v0.2.0 核验场景（兑换码附加信息）**：`CreateCodeAsync(payload:)` 传入核验业务信息明文 → 门面加密落库（`PayloadEncrypted` AES-GCM 单段 base64(nonce‖cipher‖tag)，`AeadEncryptionUtil` 规范格式）→ `RedeemAsync` 兑换时解密取回（`RedemptionRecordDto.Payload`）。核验方式（人工核验——核销人出示码比对附带信息 / 自动核验——系统校验附带信息匹配）由业务层决定；核验业务信息含 PII（购买人信息）必须走加密路径，明文不落库。兑换历史读模型（`TKWFV_UserRedemptionHistory`）**不含 payload 列**——核验信息仅在兑换时一次性取回（历史列表不重复展示核验信息）。**payload 容量上限 2800 字符**（`PayloadEncrypted` 密文列 VARCHAR(4000) 保障——ASCII 2800 字符加密后 base64 约 3740 ≤ 4000；超限 `CreateCodeAsync` 抛 `ArgumentException`）。

## 约束与语义

- **✅ UserCenter 退役完结（2026-10-08）**：原 `IRedemptionHistorySource`/`IUserAppsSource` 契约随 UserCenter 完整删除——本扩展不再受"不实现他扩展契约"约束（契约已不存在）；消费方兑换/应用查询唯一通道 = 本扩展门面。
- **行级 FK = `AuthAccount.UId`**（P4 白名单）——`RedemptionCode.RedeemedByUId`；`FederationAnchorOpenId` **不作引用键**（P2/P4——N2 anchor 冗余快查列）。
- **兑换码安全**：明文不落库（SHA256 哈希唯一）——防库泄露即任意兑换；`CodeMasked` 脱敏透传（本扩展 DTO `RedemptionRecordDto.CodeMasked`，明文不落库）；**v0.2.0 核验信息亦明文不落库**（`PayloadEncrypted` AES-GCM 密文，`ISymmetricKeyProvider` keyed 加解密——密钥键 `SymmetricKeyProviderKeys.AuthSurface`（v4.10.69 已并入主框架，C11 第 4 扩展边界兑现；原扩展侧自建 `AuthSurfaceKeyProviderKeys` 已删除））。
- **并发双兑**：CAS 原子兑换（ADR89 单语句 WHERE 谓词）——败者重查判定（已兑/吊销/过期）。
- **V0.3.0 批次域语义（T-0~T-3）**：批次头 `TKWF_CodeBatch` 与码行同扩展托管（头行同属主——SyncStructure 无双属主；批次仅 DRAFT 可物理删，DRAFT 无码）；状态机 `DRAFT→GENERATING(CAS 抢占)→GENERATED→REVOKED`，`PARTIAL_REVOKED` 派生不落库；**生成与吊销均单语句条件更新 + 显式事务**（`ITransactionManager`——生产 FreeSql 物理事务；生成失败/中断整体回滚 DRAFT，已流出明文作废）；**统计口径时间派生**（见上「批次管理」节——禁按 `Status==2` 落库计数）。
- **T-7 协同（AuthCenter `RecordGrantAsync`）**：redeem 源授权写入由**消费方兑换编排**调用 `IAuthGrantCommandService.RecordGrantAsync(userId, appId, source, validUntilUtc, ct)`（upsert 冲突路径以传入 `ValidUntil` 覆写——V0.3.0 修复现 `RefreshActiveAsync` 不动 ValidUntil 的缺口）；AuthSurface **不直写** AuthGrant（跨扩展实现依赖红线 TKWF0022）；覆写=last-writer-wins，**并发叠加防丢失（30+30）归消费方编排**（账号维度串行化）。
- **视图前缀**：`TKWFV_` 暂用（前置裁定 P3 / ADR C.16——框架组前缀批次核查后视情况 rename，不影响实体/ViewSql 设计）。
- **表名快照**：ViewSql 引用 `TKWF_RedemptionCode`/`TKWF_AuthApp`（本扩展）+ `TKWF_AuthGrant`/`TKWF_AuthAccount`（AuthCenter V0.9.0 身份域重构——ADR100 表名别名落地；`AuthAccount` 已改 `TKWF_AuthAccount` 凭据白名单列，档案列迁 `UserProfileEntity`）。
- **SQLite 测试已知限制**（2026-10-07 实证）：FreeSql SQLite provider 对 UTC DateTime 列读回偏移（+7h）——过期场景门面 C# 判定受偏移影响不走进度翻转分支，但 **CAS 谓词（SQL 侧）兜底拒绝**——生产 PG/SQL Server 无偏移（门面判定 + CAS 一致）。
- **删除语义**：无软删除（`hasSoftDelete:false`——兑换码/应用目录物理操作，运营侧经门面）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.AuthSurface;

// ⚠️ 依赖认证中心（视图 JOIN TKWF_AuthGrant/TKWF_AuthAccount 基表）——同时白名单声明
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

三钩子自动接线。DI 注册形态：5 门面 `AddConstructibleService`（接口守卫工厂 + 实现 throw-factory——域作用域外解析抛；V0.3.0 增 `ICodeBatchService`）；实体/VEntity DataService SG1/ADR61 自动注册（Initializer 零手动）；`IRateLimitCheck` `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>`（v4.10.67 fallback——未启用 RateLimiting 扩展时频控不消失）。

## 数据模型

```sql
TKWF_RedemptionCode(id BIGINT PK, code_hash VARCHAR(64) UNIQUE, code_masked VARCHAR(32), product_name VARCHAR(100),
                    target_app_id VARCHAR(100), status INT,        -- 0 Available / 1 Redeemed / 2 Expired / 3 Revoked（V0.3.0）
                    expire_at_utc DATETIME NULL, redeemed_by_uid VARCHAR(32) NULL,   -- 行级 FK=AuthAccount.UId
                    redeemed_at_utc DATETIME NULL, create_time, update_time,
                    payload_encrypted VARCHAR(4000) NULL,          -- v0.2.0 核验信息密文（AES-GCM 单段，明文不落库）
                    batch_id BIGINT NULL)                          -- V0.3.0 批次软引用（IX 非唯一，无 FK——对齐 ManagedFileVersion.FileId 先例）
TKWF_CodeBatch(id BIGINT PK, batch_no VARCHAR(32) UNIQUE, name VARCHAR(100), product_name VARCHAR(100),
               target_app_id VARCHAR(100), validity_days INT NULL, expire_at_utc DATETIME NULL,
               channel VARCHAR(100) NULL, quantity INT, generated_count INT,
               status INT,                                     -- 0 DRAFT / 1 GENERATING / 2 GENERATED / 3 REVOKED（PARTIAL_REVOKED 派生不落库）
               test BOOLEAN, exported_at_utc DATETIME NULL, created_by VARCHAR(64) NULL, create_time, update_time)   -- V0.3.0 批次头
TKWF_AuthApp(id BIGINT PK, app_id VARCHAR(100) UNIQUE, app_name VARCHAR(100), icon VARCHAR(512) NULL,
             is_enabled BOOLEAN, create_time, update_time)
-- 视图（跨扩展——生产 DBA 手动建，依赖 AuthCenter 表）
TKWFV_UserRedemptionHistory  = TKWF_RedemptionCode INNER JOIN TKWF_AuthAccount (RedeemedByUId = UId)
TKWFV_UserApps               = TKWF_AuthGrant INNER JOIN TKWF_AuthAccount (UserId = UId) LEFT JOIN TKWF_AuthApp (AppId)
                              -- v0.2.1：SELECT 末尾投影 a."IsEnabled"（LEFT JOIN 未命中 = NULL，软禁用排除在 DataService——NULL 视同启用）
```

- 索引：`TKWFIX_RedemptionCode_CodeHash`（唯一——一码一兑）/ `TKWFIX_RedemptionCode_BatchId`（非唯一——按批查码）/ `TKWFIX_CodeBatch_BatchNo`（唯一）/ `TKWFIX_AuthApp_AppId`（唯一）。
- 生产建表：框架 `SyncTables` 托管本扩展表（ADR49）；**视图生产需 DBA 手动执行 ViewSql**（依赖扩展表清单见使用指南 §六）。

## 与认证中心 / 用户中心划界

| 扩展 | 边界 |
|------|------|
| `TKWF.Ext.AuthCenter`（V0.9.0 重构中） | 身份锚 `AuthAccount.UId` = 本扩展行级 FK；`AuthGrant`（login 源）为"我的应用"数据底座；`AuthGrantSources.Redeem` 预留 v0.2.0 兑换授权写入（v0.1.0 不写——评估 AuthCenter 门面 Source 参数化 or 授权面委托） |
| ~~`TKWF.Ext.UserCenter`~~ | ~~**P2 FREEZE**：本扩展不实现两 Source 契约；UserCenter 两方法保持降级空列表；DTO 复用 UserCenter.Abstractions（页面零适配）~~——**✅ 已删除（2026-10-08，UserCenter 完整退役；DTO 归本扩展主包）** |

## 后续演进（v0.4.0+ 候选）

~~批次管理端（批量生成/分销/导出/作废——`BatchId` 列 + 管理门面）~~ → **✅ V0.3.0 已交付批次域（`ICodeBatchService` + `TKWF_CodeBatch`）**；~~兑换授权写入 AuthGrant（Source=redeem）~~ → **✅ V0.3.0 经 AuthCenter `RecordGrantAsync` 泛化协同交付**；~~分页查询~~ → **✅ V0.3.0 T-6**。**剩余候选**：`[GenerateController]` 原生管理端点（延后至有消费方要扩展原生管理面——本迭代管理端点/CSV 归消费方表现层）；`hasPayload` 批次列（延后至 `/import` 批次模板路径落地）；批次模板 payload 写入；CSV 格式化助手（呈现留消费方）；分布式频控（SqlCountRateLimitCheck/Redis）；兑换尝试日志持久化（审计回溯）；授权快照/verify 聚合；匿名兑换（兑换创建账号）。

> **✅ v0.2.0 已发布（2026-10-10——兑换码附加信息，核验场景，2026-10-09 转告落档待办）**：`PayloadEncrypted` 列 + `CreateCodeAsync(payload:)` 加密落库 + `RedeemAsync` 解密取回 + keyed 密钥基础设施。
>
> **✅ 已发布（2026-11-17，tag `AuthSurface/v0.3.1`——含 V0.3.0 批次域 + V0.3.1 SQL 下推整改）**：批次域（T-0~T-3）+ 码侧契约（T-4~T-6/T-8）+ AuthCenter `RecordGrantAsync` 泛化协同（T-7）+ 批次查询性能整改（分组统计/分页下推）——开发方案 `docs/授权面/v0.3.0-AuthSurface-批次管理端与码侧契约-开发方案.md`。

## 测试基线

`Extension.AuthSurface.Tests`——**51 用例全绿**（v0.3.0：+21——批次创建校验 4 / 生成回调与 CAS 4 / 删除 1 / 统计口径 1 / 明细分页 1 / 吊销 3 / 契约 6〔expireAt 互斥、Revoked 403、GetByCode、GetPayload、T-7 链路、分页〕；**v0.3.1：+1——`GetBatches_Stats_SingleGroupBySql_NotNPlusOne`（GROUP BY 单查询断言）**；既有 28：兑换链路端到端/负路径/并发 CAS 单胜/频控/跨扩展视图 JOIN/仅本人/目录 CRUD/守卫工厂装配断言/软禁用聚合语义；跨扩展基表由 AuthCenter 实体 SyncStructure 建——**双扩展集成**（ProjectReference + 双白名单））。⚠️ **事务边界**：SQLite 单写者锁限制——批次生成/吊销用例验证 Begin/Commit/Rollback **结构**（Recording 事务管理器），**物理原子性归 PostgreSQL**（仓库约定 `docs/测试约定-SQLite物理事务与Noop限制.md` §2.2）。

> ⚠️ **v0.2.0 包（2026-10-10）ViewSql 含 `ac."AuthLevel"` 投影**——与 AuthCenter V0.11.0+ 组合部署建视图存在列失配风险，请升级至含修复的后续版本。

<!-- EOF -->
