# TKWF.Extensions

> TKWF 扩展模块仓库——权限、导航、身份、审计、设置、邮件、存储、账户、数据字典等。
>
> 扩展模块与主框架 **[TKW.Framework](https://github.com/LoongBa/TKW.Framework)** 解耦：扩展代码、测试、使用指南独立演进，不进入主框架 `TKW.Framework.slnx`。

---

## 扩展模块一览

> 各扩展模块使用说明

| 扩展                         | 版本            | 说明                                                                     | Tag                     | README                                   | 指南                                     |
| ---------------------------- | --------------- | ------------------------------------------------------------------------ | ----------------------- | ---------------------------------------- | ---------------------------------------- |
| **Permissions**              | V0.9.3 | 细粒度权限定义 / fail-closed 检查 / 编译期权限名校验（PERM001）/ **多用户批量权限检查（V0.9.0：`IPermissionBatchChecker`——单权限×多用户，按 ProviderKey 分组归因）** | $(System.Collections.Specialized.OrderedDictionary[Permissions]) | [README](./_Framework/Permissions/README.md) | [指南](./docs/Permissions/权限扩展-使用指南.md) |
| **Permissions.Abstractions** | V0.1.0 | 权限契约抽象（`IPermissionChecker`/`RequirePermission`/`IRoleProvider`） | —                       | —                                        | —（并入 Permissions）                    |
| **Permissions.Validation**   | V0.8.0 | 扩展侧 PERM001 DiagnosticAnalyzer（从内核移除耦合）                      | —                       | —                                        | —（并入 Permissions）                    |
| **Identity**                 | V0.5.0 | 用户 / 角色 / 用户角色分配 + PasswordHasher 凭据验证；`GetRolesAsync` VEntity 跨表 JOIN（V0.3.0）+ **REST 直接暴露（V0.4.0：`UserRoleViewQueryService`——`GET /api/identity/user-roles` 仅本人，VEntity DTO 一等公民含 DisplayName/IsSystemRole）**| $(System.Collections.Specialized.OrderedDictionary[Identity]) | [README](./_Framework/Identity/README.md)      | [指南](./docs/Identity/身份管理扩展-使用指南.md) |
| **Account**                  | V0.5.0 | 账户锁定 + 密码重置流程（主框架 V4.9.45 缺口补齐）+ **登录历史与异常检测（V0.3.0，消费 SecurityLog 查询）+ 依赖倒置（V0.4.0，SecurityLog 契约改引 Abstractions）** | $(System.Collections.Specialized.OrderedDictionary[Account]) | [README](./_Framework/Account/README.md)       | [指南](./docs/Account/账户管理扩展-使用指南.md) |
| **Account.Abstractions**     | V0.1.2 | 账户契约抽象（`IAccountPasswordManager` 等——ADR48 D7 依赖倒置，零框架引用纯 BCL）            | —                       | —                                        | —（并入 Account）                    |
| **Authentication**           | V0.5.2 | 认证中心（令牌体系手写 RS256 JWT + kid 轮换 + 黑名单落库 + Refresh rotation + TokenVersion 闭环 / 认证矩阵 Provider（短信 + 微信）/ 登录保护 / 票据换令牌（PKCE）/ 身份适配层（JwtDomainUserParser/ITokenVerifier/IAuthorizationMapper/UserHelperBase/中间件）/ 跨系统映射 / 平台凭证 / 账号查询写契约 + UserCenter 档案承接）+ **Web 装配钩子（v4.10.45：`JwtAuthenticationWebExtension<TUserInfo>`——RestoreUser 委托直传 + BeforeAuthentication 锚点，旧 `AddJwtAuthentication`/`UseTkfwJwtAuthentication` 删除；短路判定修正为 IsAuthenticated 门控 + 黑盒哨兵冒烟锁定顺序）** + **ITokenVerifier 无帧消费 500 修复（V0.5.2：中间件游客帧 `guest.Use` + UserHelper Use 帧内，fcbffd1 受理 Oracle 裁决；V0.5.1 System 作用域过渡形态未发布）** | $(System.Collections.Specialized.OrderedDictionary[Authentication]) | [README](./_Framework/Authentication/README.md) | [指南](./docs/Authentication/认证中心-使用指南.md) |
| **UserCenter**               | V0.1.0 | 用户中心档案面（从认证中心 v0.2.0 拆分独立立项）——契约归中立 `UserCenter.Abstractions`（数据属主扩展实现）、领域逻辑（脱敏/降级/聚合）进主包门面、**零实体零存储**聚合读取层、仅本人防护（门面显式 userId 参数）；`IUserCenterQueryService` 门面组合三 Source（IUserProfileSource/IRedemptionHistorySource/IUserAppsSource）+ PhoneMasker 强制脱敏 + 降级矩阵 | $(System.Collections.Specialized.OrderedDictionary[UserCenter]) | [README](./_Framework/UserCenter/README.md) | [指南](./docs/UserCenter/用户中心扩展-使用指南.md) |
| **Federation**               | V0.1.0 | 认证中心联邦层（跨应用 Federated SSO，外交部——2026-10-05 归层后命名，原 SSO）——应用注册（origin 白名单+scope+AES-GCM credential+per-channel HMAC）/ 授权码 accesscode（120s 原子 CAS+SHA256+PKCE）/ **token2 手写 ES256 独立密钥域 + kid 轮换 + JWKS**（与 Authentication RS256 完全独立）/ profile API（scope 强制+审计）/ `ISsoChannel` IdP 适配器契约；经 `Authentication.Abstractions` 契约包组合式消费认证内核（L2 门控，零全量传递）；前置拆包 `Authentication.Abstractions`（SSO 消费面契约+DTO，零实体零 SG1）+ 联盟锚点数据模型（`FederationAnchorOpenId` 列）；不引主包、零 Store（SG1 DataService 直连） | $(System.Collections.Specialized.OrderedDictionary[Federation]) | [README](./_Framework/Federation/README.md) | [指南](./docs/Federation/（规划中）) |
| **Federation.WeChat** | V0.1.0 | 微信公众平台连接器（大使馆/平台网关库——纯库无装配无持久化，方向对外接微信 IdP）：`WeChatApiClient` 出站（access_token L1 缓存+并发锁+提前 5min 刷新+code→openid+sns/userinfo 降级）+ `WeChatOptions`（`TKWF:Federation:WeChat` 节，凭证自持 Oracle P2-4）+ 双通道（`WeChatOauthChannel` wechat_oauth / `WeChatEventChannel` wechat_event + `WeChatEventCrypto` 事件验签——信任根 `msg_signature`+AES 一票否决；T4/T5 实施中）+ `AddWeChatFederationChannels()` 注册（T6 实施中）；不引 AspNetCore（端点归装配层）、零持久化零 Store | $(System.Collections.Specialized.OrderedDictionary[Federation.WeChat]) | [README](./_Framework/Federation.WeChat/README.md) | [指南](./docs/Federation.WeChat/微信公众平台连接器-使用指南.md) |
| **UserCenter.Abstractions**  | V0.1.0 | 用户中心契约包（三 Source + 门面 + 三 DTO——ADR48 D7 第 6 次落地，零框架引用纯 BCL；命名空间保持 `TKWF.Ext.UserCenter` 对齐 Emailing.Abstractions 先例）| — | — | —（并入 UserCenter）|
| **MFA**                      | V0.1.3 | 多因素认证（TOTP RFC 6238 自研零第三方 + 短信验证码双方法：绑定/解绑 + 挑战-验证流（一次性 TTL 票据 + 单次消费防重放）+ 尝试频控（内存滑动窗口）+ 恢复码；独立扩展零依赖——短信渠道经消费方 `IMfaSmsSender` 注入，登录编排归消费方；secret AES-GCM 密文落库）| $(System.Collections.Specialized.OrderedDictionary[MFA]) | [README](./_Framework/MFA/README.md) | [指南](./docs/MFA/MFA多因素认证-使用指南.md) |
| **Navigation**               | V0.1.2 | 菜单数据模型 / 贡献机制 / 权限过滤（从主框架迁出）                       | $(System.Collections.Specialized.OrderedDictionary[Navigation]) | [README](./_Framework/Navigation/README.md) | [指南](./docs/Navigation/导航扩展-使用指南.md)  |
| **Navigation.Abstractions**  | V0.1.0 | 导航契约抽象（`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext`——ADR48 D7 依赖倒置，零框架引用纯 BCL）| — | — | —（并入 Navigation）|
| **AuditLogging**             | V0.5.0 | 审计日志 FreeSql 存储 + SG1 实体 + 查询 API + 统计聚合与清理（V0.3.0）+ **管理 API（V0.4.0：`[GenerateController]` + ExcludeMethods 排除含 ArgumentsJson 标准 CRUD + 5 REST 端点）** + **聚合 SQL 下推（V0.4.2：TopN 经 `GroupCountAsync`——V4.10.39 分组聚合 API）** | $(System.Collections.Specialized.OrderedDictionary[AuditLogging]) | [README](./_Framework/AuditLogging/README.md)  | [指南](./docs/AuditLogging/审计日志扩展-使用指南.md) |
| **Settings**                 | V0.3.0 | 全局/用户级配置持久化 + 分层读取                                         | $(System.Collections.Specialized.OrderedDictionary[Settings]) | [README](./_Framework/Settings/README.md)      | [指南](./docs/Settings/设置管理扩展-使用指南.md) |
| **BlobStoring**              | V0.3.0 | 二进制大对象本地文件系统存储 + FreeSql 记录 + **FileStream 流式下载（V0.2.0）** | $(System.Collections.Specialized.OrderedDictionary[BlobStoring]) | [README](./_Framework/BlobStoring/README.md)   | [指南](./docs/BlobStoring/二进制存储扩展-使用指南.md) |
| **Emailing**                 | V0.3.0 | SMTP/MailKit 邮件发送 + FreeSql 发送记录 + **指数退避重试（V0.2.0）**；契约抽取至 `Emailing.Abstractions`（ADR48 D7）| $(System.Collections.Specialized.OrderedDictionary[Emailing]) | [README](./_Framework/Emailing/README.md)      | [指南](./docs/Emailing/邮件发送扩展-使用指南.md) |
| **Emailing.Abstractions**    | V0.1.0 | 邮件发送契约抽取（`IEmailSender`/`EmailMessage`——ADR48 D7 依赖倒置，Notifications 等消费方复用）| — | — | —（并入 Emailing）|
| **DataDictionary**           | V0.3.0 | 数据字典集中管理（定义 + 项 + 按编码查询）+ **VEntity 读模型联邦（V0.2.0：`vw_DictionaryItemView` JOIN Definition→Item 单查询下推——`GetOrLoadAggregateAsync` 两步骤一，"定义存在无项"语义保留，树语义保护经门面）**| $(System.Collections.Specialized.OrderedDictionary[DataDictionary]) | [README](./_Framework/DataDictionary/README.md) | [指南](./docs/DataDictionary/数据字典扩展-使用指南.md) |
| **Tagging**                  | V0.4.5 | 标签存储扩展（标签算法已回归 `TKW.Framework.Utility.Tags`，ADR52 瘦身；V0.4.0 AC 自动机 `DictMatch` 批量匹配 + Options 配置接入）；+ **聚合 SQL 下推（V0.4.3：`GetFrequencyAsync`/`GetDimensionDistributionAsync` 经 `GroupCountAsync`/`GroupByAsync`——V4.10.39；`GetTrendAsync` 保留内存分桶）**| $(System.Collections.Specialized.OrderedDictionary[Tagging]) | [README](./_Framework/Tagging/README.md)       | [指南](./docs/Tagging/标签服务扩展-使用指南.md)  |
| **PrintTemplates**           | V0.3.0 | 打印模板引擎与版本化（Scriban 沙箱渲染 + Draft/Active/Archived 生命周期）| $(System.Collections.Specialized.OrderedDictionary[PrintTemplates]) | [README](./_Framework/PrintTemplates/README.md) | [指南](./docs/PrintTemplates/打印模板扩展-使用指南.md) |
| **Metrics**                  | V0.2.1 | 业务指标计算引擎（规格文档驱动复合指标计算——复购率/留存/同期群/漏斗/时段桶/比率；核心计算在 `TKW.Framework.Utility.Metrics`）+ **指标结果持久化（V0.2.0：`IMetricResultStore` 契约 + `MetricResultMapper` 标准化行映射——实体归消费方 SG1 接线）**| $(System.Collections.Specialized.OrderedDictionary[Metrics]) | [README](./_Framework/Metrics/README.md) | [指南](./docs/Metrics/指标扩展-使用指南.md) |
| **Analytics**                | V0.1.0 | 业务分析服务（spec 文件驱动 + flint 单一真相 JsonDocument 透传——核心 `TKW.Framework.Utility.Analytics` 零依赖 + 本包 `IAnalyticsQueryService` 门面 4 方法；manifest 状态校验 + domain/specKey 路径安全正则）| $(System.Collections.Specialized.OrderedDictionary[Analytics]) | [README](./_Framework/Analytics/README.md) | [指南](./docs/Analytics/分析服务扩展-使用指南.md) |
| **Dashboard**                | V0.1.2 | 仪表盘数据服务（Metrics 展示层——JSON 描述符 + Widget 数据查询；不引入图表库）| $(System.Collections.Specialized.OrderedDictionary[Dashboard]) | [README](./_Framework/Dashboard/README.md) | [指南](./docs/Dashboard/仪表盘扩展-使用指南.md) |
| **DataPort**                 | V0.1.4 | 数据导入导出（三层架构——核心运行库+MiniExcel Provider+SG1 持久化；FileHash 幂等）| $(System.Collections.Specialized.OrderedDictionary[DataPort]) | [README](./_Framework/DataPort/README.md) | [指南](./docs/DataPort/数据导入导出扩展-使用指南.md) |
| **Notifications**            | V0.6.1 | 通知中心（站内通知收件箱+订阅+事件驱动通知+多通道抽象；第一个事件总线消费者）；`GetListAsync(name)` VEntity 跨表 JOIN（V0.1.0）；+ 多通道路由 UseChannels/Email（V0.2.0）；+ 用户偏好路由 + 逐用户权限门控（V0.3.0，委托 Permissions v0.9.0 `IPermissionBatchChecker`）；+ **SignalR 实时推送通道（V0.4.0，独立包 `TKWF.Ext.Notifications.SignalR`，服务端非 UI）**；+ **REST 直接暴露（V0.5.0：`UserNotificationViewQueryService`——`GET /api/notifications/inbox` 仅本人 + name 可空，VEntity DTO 一等公民含 Name/Severity/DisplayName）**| $(System.Collections.Specialized.OrderedDictionary[Notifications]) | [README](./_Framework/Notifications/README.md) | [指南](./docs/Notifications/通知中心扩展-使用指南.md) |
| **Notifications.SignalR**    | V0.2.0 | 通知中心 SignalR 实时推送通道（独立包——Hub 类型锚 + SignalRNotifier best-effort 推送 + 端点映射；`FrameworkReference` 共享框架零 NuGet；服务端非 UI，前端归消费方）+ **Web 装配钩子（v4.10.45：`NotificationsHubWebExtension`——AddSignalR D6 内聚 + MapHub，旧 `MapTkfwNotificationsHub` 删除）**| $(System.Collections.Specialized.OrderedDictionary[Notifications.SignalR]) | —（并入 Notifications） | —（并入 Notifications 指南） |
| **BackgroundJobs**          | V0.4.1 | 后台任务持久化增强（执行历史 `JobExecution` + 业务结果 `JobResult` 追踪 + **历史清理任务 V0.2.0（RetentionDays 启用）**）| $(System.Collections.Specialized.OrderedDictionary[BackgroundJobs]) | [README](./_Framework/BackgroundJobs/README.md) | [指南](./docs/BackgroundJobs/后台任务持久化扩展-使用指南.md) |
| **BackgroundJobs.Quartz**   | V0.1.0 | Quartz AdoJobStore 一键封装（`UseTkfwAdoJobStore` 12 表自动建表/集群配置）| $(System.Collections.Specialized.OrderedDictionary[BackgroundJobs.Quartz]) | [README](./_Framework/BackgroundJobs.Quartz/README.md) | —（并入 BackgroundJobs） |
| **HealthCheck**             | V0.3.0 | 系统健康探测（net10 内置 HealthChecks + `/health` 端点 + **内置 DB 探针** `AddDatabaseHealthCheck<T>`（V0.2.0，`IEntityReadOnlyDAC` 红线合规路径））+ **Web 装配钩子（v4.10.45：`HealthCheckWebExtension`——fluent 探针收集 + MapHealthChecks，旧 `AddTkfwHealthChecks`/`MapTkfwHealthChecks` 删除）**| $(System.Collections.Specialized.OrderedDictionary[HealthCheck]) | [README](./_Framework/HealthCheck/README.md) | [指南](./docs/HealthCheck/健康检查扩展-使用指南.md) |
| **RateLimiting**            | V0.2.0 | Web 层限流接线（ASP.NET Core AddRateLimiter + IP/用户分区 + 429/Retry-After；与 Domain `[RateLimit]` 双层防护）+ **Web 装配钩子（v4.10.45：`RateLimitingWebExtension`——AddRateLimiter 展开 + UseRateLimiter，旧 `AddTkfwRateLimiting` 删除；标注式 RequireRateLimiting 仍需消费方 BeforeRouting 显式 UseRateLimiter）**| $(System.Collections.Specialized.OrderedDictionary[RateLimiting]) | [README](./_Framework/RateLimiting/README.md) | [指南](./docs/RateLimiting/限流扩展-使用指南.md) |
| **SecurityLog**             | V0.4.0 | 安全日志（登录/登出/改密/重置/锁定/注册/挑战事件 + IP/UA/结果 + **异常检测聚合 + 保留天数清理（V0.2.0）** + **契约拆包（V0.3.0——公开契约迁 SecurityLog.Abstractions，命名空间不变）** + **聚合 SQL 下推（V0.3.1：TopN 经 `GroupCountAsync`——V4.10.39 分组聚合 API）**）| $(System.Collections.Specialized.OrderedDictionary[SecurityLog]) | [README](./_Framework/SecurityLog/README.md) | [指南](./docs/SecurityLog/安全日志扩展-使用指南.md) |
| **SecurityLog.Abstractions** | V0.1.0 | 安全日志契约抽象（ISecurityLogStore/ISecurityLogQueryService/ISecurityLogAnalyticsService + DTO/Options——ADR48 D7 依赖倒置，Account 消费方复用）| — | —（并入 SecurityLog）|
| **Approval**                | V0.4.0 | 轻量审批引擎（流程定义/审批实例/审批任务三实体 + 状态机 + 或签/会签 + 委派/加签/抄送/超时自动处理（v0.2.0）+ 完成事件回调；不依赖外部工作流引擎）| $(System.Collections.Specialized.OrderedDictionary[Approval]) | [README](./_Framework/Approval/README.md) | [指南](./docs/Approval/审批流扩展-使用指南.md) |
| **OrganizationUnit**        | V0.5.0 | 组织单元（树形部门/团队/分组 + 物化路径 Level/Path + 循环防护/删除保护 + 用户关联 + 事务包裹）+ **VEntity 化/下推（V0.2.0：`vw_UserOrganizationUnitView` JOIN OU→OUUser 单查询 + `GetSubTreeAsync`/`GetAncestorsAsync` 单表 SQL 下推（精确前缀/Code IN）+ 用户归属双模式）**| $(System.Collections.Specialized.OrderedDictionary[OrganizationUnit]) | [README](./_Framework/OrganizationUnit/README.md) | [指南](./docs/OrganizationUnit/组织单元扩展-使用指南.md) |
| **Calendar**                | V0.4.0 | 日历/排程（日历+事件 CRUD + 重复规则子集（Utility 收纳：DAILY/WEEKLY/MONTHLY/YEARLY + 月末钳制 + 绝对索引）+ occurrence 查询/合并 + UTC 契约）| $(System.Collections.Specialized.OrderedDictionary[Calendar]) | [README](./_Framework/Calendar/README.md) | [指南](./docs/Calendar/日历排程扩展-使用指南.md) |
| **FileManagement**          | V0.4.0 | 文件管理（目录树 + 文件元数据 SHA256/去重 + **文件版本化 + 配额（v0.2.0）** + **用户级配额（v0.3.0：OwnerId 归属维度 + 所有权保留语义）** + **并发上传竞态加固（v0.3.0：主表 UX 约束败者重查按内容决策——同内容幂等 / 不同内容新版本）** + 上传 10 步安全链（防穿越/白名单/大小/ContentType 服务端推导）+ 依赖倒置 BlobStoring.Abstractions）| $(System.Collections.Specialized.OrderedDictionary[FileManagement]) | [README](./_Framework/FileManagement/README.md) | [指南](./docs/FileManagement/文件管理扩展-使用指南.md) |
| **BlobStoring.Abstractions**| V0.1.1 | Blob 存储契约抽取（`IBlobStorageService`/`BlobInfo`/`BlobStoringOptions`——ADR50 L2 依赖倒置，FileManagement 消费）| — | — | —（并入 BlobStoring）|
| **FeatureManagement**      | V0.4.4 | 功能管理/特性开关（接口判定编译期定义（v4.10.31 A+ 阶段 3）+ Provider 链分层值（v0.2.0 扩展点）+ IFeatureChecker 实现（接入 [RequireFeature]）+ 复杂 ValueType 类型化读写（v0.3.0）+ 变更事件分布式广播（v0.3.0）+ 管理 API）| $(System.Collections.Specialized.OrderedDictionary[FeatureManagement]) | [README](./_Framework/FeatureManagement/README.md) | [指南](./docs/FeatureManagement/功能管理扩展-使用指南.md) |

> 列说明：**README** = 扩展技术规范（随 NuGet 发布，位于 `_Framework/{扩展名}/`）；**指南** = 使用指南（公开文档，位于 `docs/{扩展名}/`）。Permissions.Abstractions/Validation、SecurityLog.Abstractions、Account.Abstractions、Navigation.Abstractions、UserCenter.Abstractions 无独立文档，详见对应主扩展的 README 与指南。

> 全量 **1668 测试全绿**（33 测试项目——含 Identity/Notifications REST 直接暴露 8 用例 + v4.10.39 聚合 SQL 下推 3 用例 + **04 方案 OU/DataDictionary VEntity 下推 N1-N6 用例** + **FileManagement v0.3.0 并发上传竞态加固用例（并发同名不同内容双成功）** + **v4.10.45 Web 装配钩子收敛迁移（4 先例 → IWebExtension + JwtAuth TestServer 黑盒哨兵冒烟——顺序串 ContextExtraction→JwtAuth验签→HttpAuthentication 锁定锚点分桶 P0-1）** + **v4.10.46 lockstep（BUG005 纯修复复验）** + **v4.10.53 领域自治根治（ADR90——批次 0-7 门面化整改 + 注册形态断言 + 测试宿主生产路径 + 批次间交互遗留修复 ab5f5ee）** + **v4.10.55 多实现集合守卫工厂（ADR92/T3 闭环——4 扩展 TryAddEnumerableConstructible 迁移：Authentication 登录编排门面 3 用例 + Provider 守卫形态断言 / FeatureManagement 4 Provider 守卫形态 / MFA 2 Method / Notifications InboxNotifier；BackgroundJobs JobExecutionRecorder 泛型化）** + **Authentication V0.6.0 SSO 消费面契约（5 用例：ISsoAccountQueryService DTO 返回/联邦映射 upsert/联盟锚点列唯一）** + **Federation V0.1.0（19 用例：token2 ES256 签发验签负路径轮转/JWKS + accesscode 原子 CAS 重放/PKCE + client 注册校验/origin 白名单 + 生产路径全链跨扩展；2026-10-05 归层：SSO→Federation 命名归层）**）——`dotnet test` 零失败。（Approval v0.2.0 + FeatureManagement v0.3.0 + FileManagement v0.2.0 + Calendar + OrganizationUnit + 三件套基础设施 + BlobStoring 安全修复 + Tagging v0.4.0 + HealthCheck v0.2.0 + Notifications v0.2.0 多通道路由 + AuditLogging v0.3.0/v0.4.0 统计聚合与管理 API + Permissions v0.9.0 多用户批量权限检查 + Notifications v0.3.0 偏好路由与权限门控 + Notifications v0.4.0 SignalR 通道（12 用例，独立包） + **Metrics v0.2.0 指标结果持久化（15 用例：Mapper 6 + Store 9）** + SecurityLog v0.3.0/Account v0.4.0 契约拆包回归 + **Authentication v0.2.0 查询契约 + UserCenter v0.1.0 档案面** + **Identity v0.4.0/Notifications v0.5.0 VEntity REST 直接暴露（8 用例）** + **v4.10.39 聚合 SQL 下推（AuditLogging v0.4.2/SecurityLog v0.3.1/Tagging v0.4.3——GroupCountAsync/GroupByAsync 下推 + 空白键 SQL 过滤，3 用例）** 后）

---

## 仓库定位

| 项       | 说明                                                                                                               |
| -------- | ------------------------------------------------------------------------------------------------------------------ |
| 主框架   | [`_TKWF/`](https://github.com/LoongBa/TKW.Framework)（TKW.Framework 领域框架）                                     |
| 本仓库   | TKWF 业务扩展包（`TKWF.Ext.*`）——独立版本，与主框架版本无关                                                        |
| 引用模式 | 扩展经 **PackageReference** 引用主框架**发布的 NuGet 包**（`TKWF.Domain` 等，CPM 集中 `Directory.Packages.props`；2026-09-15 迁移——独立构建，消费方视角与 NuGet 模式一致） |
| 版本管理 | MinVer 自动管理（git tag 即版本）；各扩展独立版本（各打各的 tag，命名空间前缀如 `Identity/v0.1.0`）                |

---

## 目录结构

```
_TKWF.Extensions/
├── _Framework/                     # 扩展源码（每个扩展一个项目）
│   ├── Permissions/                 # 权限扩展（V0.7.0：定义/检查/存储/管理 API/Admin.All）
│   ├── Permissions.Abstractions/    # 权限契约抽象（ADR48 D7 依赖倒置）
│   ├── Permissions.Validation/       # PERM001 编译期校验 Analyzer（V0.8.0，扩展侧）
│   ├── Identity/                     # 用户 + 角色 + 用户角色分配 + 凭据验证
│   ├── Account/                      # 账户锁定 + 密码重置流程（主框架缺口补齐）
│   ├── Navigation/                  # 菜单数据模型 + 贡献机制 + 权限过滤
│   ├── AuditLogging/                # 审计日志 FreeSql 存储
│   ├── Settings/                    # 设置管理 FreeSql 存储 + 分层读取
│   ├── BlobStoring/                 # 二进制存储（本地文件系统 + FreeSql 记录）
│   ├── Emailing/                    # SMTP/MailKit 邮件发送（V0.2.0：指数退避重试）
│   ├── Emailing.Abstractions/       # 邮件发送契约（IEmailSender——ADR48 D7 依赖倒置）
│   ├── DataDictionary/              # 数据字典集中管理
│   ├── Tagging/                     # 标签存储扩展（算法已回归 TKW.Framework.Utility.Tags）
│   ├── Notifications/              # 通知中心（发布/收件箱/订阅 + 多通道路由 + 偏好路由/权限门控）
│   ├── Notifications.SignalR/      # 通知中心 SignalR 实时推送通道（v0.4.0 独立包，服务端非 UI）
│   ├── BackgroundJobs/              # 后台任务持久化增强（执行历史 + 业务结果追踪）
│   ├── BackgroundJobs.Quartz/       # Quartz AdoJobStore 一键封装
│   ├── HealthCheck/                 # 系统健康探测（/health 端点接线 + 内置 DB 探针 v0.2.0）
│   ├── RateLimiting/                # Web 层限流接线（IP/用户分区 + 429）
│   ├── SecurityLog/                 # 安全日志（登录/改密/锁定等安全事件）
│   ├── Approval/                    # 轻量审批引擎（流程定义/实例/任务 + 状态机）
│   ├── OrganizationUnit/            # 组织单元（树形部门/团队/分组 + 用户归属）
│   ├── Calendar/                    # 日历/排程（日历+事件 + 重复规则子集）
│   ├── BlobStoring.Abstractions/    # Blob 存储契约（IBlobStorageService——ADR50 L2 依赖倒置）
│   ├── FileManagement/              # 文件管理（目录树 + 文件元数据 + 版本化/配额 + 上传安全链）
│   ├── Federation/                  # 认证中心联邦层（外交部——归层后命名，原 SSO）
│   └── FeatureManagement/           # 功能管理（特性开关——定义收集 + 分层值 + IFeatureChecker）
├── _Tests/                          # 测试（一组扩展一个测试项目）
│   ├── Extension.Permissions.Tests/
│   ├── Extension.Permissions.Consumer/    # 消费方集成验证
│   ├── Extension.Permissions.Validation.Tests/  # Analyzer 单测
│   ├── Extension.Identity.Tests/
│   ├── Extension.Account.Tests/
│   ├── Extension.Navigation.Tests/
│   ├── Extension.AuditLogging.Tests/
│   ├── Extension.Settings.Tests/
│   ├── Extension.BlobStoring.Tests/
│   ├── Extension.Emailing.Tests/
│   ├── Extension.DataDictionary.Tests/
│   ├── Extension.Tagging.Tests/
│   ├── Extension.OrganizationUnit.Tests/
│   ├── Extension.Calendar.Tests/
│   ├── Extension.FileManagement.Tests/
│   ├── Extension.Federation.Tests/
│   └── Extension.FeatureManagement.Tests/
├── docs/                           # 公开使用指南（每个扩展一份）
│   ├── Permissions/权限扩展-使用指南.md
│   ├── Identity/身份管理扩展-使用指南.md
│   ├── Account/账户管理扩展-使用指南.md
│   ├── Navigation/导航扩展-使用指南.md
│   ├── AuditLogging/审计日志扩展-使用指南.md
│   ├── Settings/设置管理扩展-使用指南.md
│   ├── BlobStoring/二进制存储扩展-使用指南.md
│   ├── Emailing/邮件发送扩展-使用指南.md
│   ├── DataDictionary/数据字典扩展-使用指南.md
│   ├── Tagging/标签服务扩展-使用指南.md
│   ├── OrganizationUnit/组织单元扩展-使用指南.md
│   ├── Calendar/日历排程扩展-使用指南.md
│   ├── FileManagement/文件管理扩展-使用指南.md
│   └── FeatureManagement/功能管理扩展-使用指南.md
├── Directory.Build.props           # TKWFSourceRoot + MinVer + 打包属性
├── Directory.Packages.props         # CPM 集中包版本
├── AGENTS.md                        # 扩展仓库开发规则（AI Agent 与人工开发者必读）
└── TKWF.Extensions.slnx            # 扩展解决方案
```

> 扩展**设计文档**（开发方案/ADR/设计思路）+ 使用指南存放于本公开仓库 `docs/`（2026-09-15 裁定后新方案落公开仓库；历史方案/审核报告/总览跟踪在**主框架私有仓库** `_TKWF/docs/03_扩展模块/`，渐进迁移）。

---

## 架构模式

所有扩展遵循统一架构模式（异常静默 + TryAddScoped + SG1 声明式实体）：

```
扩展项目（net10.0）
├── Entity（partial class + [Table] + [DomainGenerateCode] + FreeSql [Column]）
├── Store 抽象 + FreeSql 实现（internal sealed + 异常静默）
├── Manager 门面（internal sealed + 聚合查询）
├── ExtensionInitializer（[TKWFExtension] + TryAddScoped 三钩子）
└── README.md（技术规范）

测试项目（xunit.v3 + FreeSql SQLite 内存）
├── ConsumerHostInitializer（[TKWFEnabledExtension] 白名单样板）
└── 测试类（Store CRUD + Manager 聚合 + Initializer DI + 异常静默）
```

> 测试事务约定：SQLite 物理事务边界 + Noop 事务限制 + PG 测试容器评估见 [`docs/测试约定-SQLite物理事务与Noop限制.md`](./docs/测试约定-SQLite物理事务与Noop限制.md)（2026-10-02，转达登记落地）。

### 扩展启用（v4.9.85+ 必需）

扩展不再"发现即启用"——消费方须在领域初始化器上显式声明白名单：

```csharp
using TKWF.Ext.Identity;

[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

声明后扩展三钩子（`ConfigureServices`/`ConfigureFilters`/`InitializeAsync`）自动接线。

### 依赖倒置（ADR48 D7）

扩展间依赖走 `.Abstractions`（接口/契约），不引用实现项目——L2 门控硬约束（`TKWF0022` Error）。

```
Navigation → Permissions.Abstractions（✅ 合法）
Navigation → Permissions（❌ TKWF0022 Error）
```

---

## 快速开始

### 消费方引用扩展

**NuGet 包模式**（消费方推荐——扩展已发布 `TKWF.Ext.*` NuGet 包）：

```xml
<!-- 消费方 .csproj -->
<PackageReference Include="TKWF.Ext.Identity" Version="0.3.3" />
<!-- 扩展经 PackageReference 传递引用主框架包（TKWF.Domain 4.10.24 等）；
     依赖扩展（如 Identity → Permissions.Abstractions）自动解析 -->
```

**源码模式**（本仓库开发/联调主框架新 API 时）：

```xml
<!-- 消费方 .csproj -->
<ProjectReference Include="..\_TKWF.Extensions\_Framework\Identity\TKWF.Ext.Identity.csproj" />
```

> 双模式并存：扩展自身构建用 PackageReference（主框架包）；需调试主框架源码新 API 时可临时切源码引用——消费方视角与 NuGet 模式一致。

### 启用 + 使用

```csharp
// 1. 白名单声明（v4.9.85+）
[TKWFEnabledExtension(typeof(IdentityExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 2. 注入 + 使用
public class AuthService(IUserManager userManager)
{
    public async Task<UserEntity?> LoginAsync(string name, string password)
        => await userManager.VerifyCredentialsAsync(name, password);
}
```

---

## 版本管理

```
MinVer：各扩展包独立版本（git tag 命名空间前缀 + csproj MinVerTagPrefix 对齐，如 Identity/v0.3.2 → 0.3.2）
公共内容（构建基建/无扩展归属）：无前缀 v tag（如 v0.8.2）

机制：
- 每个扩展 csproj 设 <MinVerTagPrefix>{扩展名}/v</MinVerTagPrefix>——MinVer 只匹配该扩展自己的前缀 tag，
  各扩展版本完全独立（Permissions 0.9.x 与 Identity 0.3.x 互不影响）
- 扩展 tag 命名：{扩展名}/v{major}.{minor}.{patch}（如 Permissions/v0.9.0、Notifications/v0.3.0）
- 公共内容（Directory.Build.props/构建脚本等无扩展归属的变更）：打无前缀 v tag（既有 v0.1.0~v0.8.2）
- 契约包（*.Abstractions）独立 tag：Account.Abstractions/v0.1.0 等，版本与主扩展独立演进
- 与主框架 _TKWF 版本完全独立
Tag 纪律：必须有开发方案 + 审核报告，且征得用户同意
```

---

## 扩展规划

- **P0（必须）**：**11/11 全部完成** ✅——Identity / Account / Navigation / AuditLogging / Settings / BlobStoring / Emailing / DataDictionary / Tagging / PrintTemplates + Permissions（V0.9.0）。注：Tagging 标签算法已按 ADR52 回归主框架 `TKW.Framework.Utility.Tags`，扩展保留存储层。
- **P1（推荐）**：**20 扩展已实施**（后台任务/功能管理/通知+SignalR/限流/健康检查/安全日志/组织单元/文件管理/审批/导入导出/日历/仪表盘/打印模板/指标/标签存储/数据字典/用户中心/多因素认证/分析服务）；剩余 14 项按需推进（OpenIddict/SSO/LDAP/后台服务/安全防护/媒体库/搜索/动态表单/动态字段/工作流/文本模板/报表/文档管理/ApiDocs）。
- **P2（待定）**：20 项全部按需启用（CMS/支付/订阅/聊天/GraphQL/可观测性/数据分析BI 等）。

## NuGet 发布（2026-09-15）

- **30 包已发布 nuget.org**（`TKWF.Ext.*`，v0.1.x–v0.9.x，依赖主框架稳定包 4.10.24）——经 GitHub Actions + **Trusted Publishing**（OIDC 免 API key：`NuGet/login@v1` + nuget.org policy + `NUGET_USER` secret）自动发布，CI run 34897984278 30/30 pushed 确认。
- **后续版本发布**：各扩展打 `{扩展名}/v{x.y.z}` tag（patch bump 指向目标 commit）→ push tag 即触发 CI 全量重建 + 发布（--skip-duplicate 幂等）。

> 设计思路与 ABP 兼容策略（最优设计为默认、兼容 ABP 为特殊需求、碰巧兼容只记录）+ 各扩展设计分类见 [`docs/扩展模块设计思路与ABP兼容策略.md`](./docs/扩展模块设计思路与ABP兼容策略.md)；状态跟踪见主框架私有 [`_TKWF/docs/03_扩展模块/总览和跟踪.md`](https://github.com/LoongBa/TKW.Framework/blob/master/docs/03_扩展模块/总览和跟踪.md)。

---

## 许可证

Copyright © 2026 LoongBa · [Apache-2.0](./LICENSE)

> 开源、允许商用与闭源衍生，但必须保留版权与归属声明（Attribution）。

## 相关仓库

- [TKW.Framework（主框架）](https://github.com/LoongBa/TKW.Framework) — 领域框架 + 扩展机制（`TKWFExtensionAttribute` + `ExtensionInitializer` 三钩子 + SG1 发现 + `[TKWFEnabledExtension]` 白名单启用 + 三层门控 ADR50）
- [LoongBa-Scaffold](https://github.com/LoongBa/LoongBa-Scaffold) — 文档体系脚手架来源

