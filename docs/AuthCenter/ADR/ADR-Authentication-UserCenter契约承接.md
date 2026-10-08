# ADR-Authentication-UserCenter契约承接

## 状态

**已废弃（2026-10-08——UserCenter 基础功能并入 AuthCenter，本承接形态终止）**

> 本 ADR 为永久架构决策记录，不可删除。如后续决策被推翻（如授权面/组合视图 `vm_UserCenterProfile` 方案替换本承接形态），须在本 ADR 标注「已废弃」并引用新 ADR，而非删除本文件。
>
> **已废弃说明（2026-10-08）**：本 ADR 的决策（选项 A——认证中心实现 `IUserProfileSource` 承接 UserCenter 契约）曾实施（`AuthAccountUserProfileSource`，2026-10-07 AuthCenter V0.9.0 起 C.14 退役过渡）。**2026-10-08 落地**：UserCenter 基础功能并入 AuthCenter——`UserProfile` 1:1 档案表 + `IAuthAccountQueryService.GetProfileByUIdAsync` 档案查询门面已完全取代契约承接形态；`AuthAccountUserProfileSource` **已删除**，AuthCenter **不再引用** `UserCenter.Abstractions`。替代路径：`ADR-AuthCenter-身份域数据模型与密码能力边界`（C.14 UserCenter 退役）+ 消费方档案读经 `IAuthAccountQueryService.GetProfileByUIdAsync` 或业务扩展 VEntity。`IAuthAccountQueryService`（本 ADR 补强的对外查询契约）**继续活跃**（非废弃部分）。

## 一、目的与目标

认证中心 v0.2.0 承接 UserCenter 终态路径——**数据属主扩展实现他扩展读取契约**：认证中心（`AuthAccountEntity` 数据属主）在自身 Initializer 内实现 UserCenter 契约 `IUserProfileSource` 并以 `TryAddScoped` 注册，消费方白名单声明后**零桥接**获得真实档案；同时补齐对外查询契约 `IAuthAccountQueryService`（4 只读方法）。读者应在 3 句话内明白：认证中心实现 `IUserProfileSource`（`AuthAccountUserProfileSource`，public sealed）+ `TryAddScoped` 注册，新装配实例无需写桥接类；补 `IAuthAccountQueryService` 只读查询契约（`AuthAccountQueryService` internal sealed 委托 DataService）；本决策是 ADR48 D7 依赖倒置下**「主扩展实现他扩展读取契约」新形态首例**（区别于既往「契约拆包」）。

## 二、问题

### 问题现象

- v0.1.0 认证中心 10 接口（`ITokenService`/`IAuthLoginAttemptService`/`IOAuthTicketService`/`IPlatformAccountMapService`/`IPlatformCredentialService`/`ISmsVerificationService`/`IAuthenticationProvider`/`ITokenVerifier`/`IAuthorizationMapper`/`IWeChatApiClient`）**均无账号查询契约**——`AuthAccountEntityDataService` 已有 4 个查询方法（`GetByUIdAsync`/`GetByPhoneAsync`/`GetByWechatMpOpenIdAsync`/`GetByWechatWebOpenIdAsync`）但无对外服务接口；UserCenter 过渡期装配实例只能**绕契约**自行查 `AuthAccountEntity`（UserCenter 方案 P2 注记缺口）。
- UserCenter 便携档案契约 `IUserProfileSource` **由谁实现**悬而未决——「读取契约实现归属」无裁定。

### 触发场景

- 消费方同时白名单声明认证中心 + UserCenter：`IUserCenterQueryService.GetProfileAsync` 需要 AuthAccount 真实档案——契约由谁实现？
- 新装配实例（DMP-Lite / 教育线）装配档案面：是否仍需手写 `AuthAccountProfileSource` 桥接类样板？
- 认证中心内部（TokenService/Provider）与外部（装配桥接）都需要按 UId/Phone/WechatOpenId 查账号——查询面走契约还是裸露 DataService？

### 现有方案不足

- **传统「主项目/装配层实现接口」（Provider 样板）**：契约由消费方装配实例实现（UserCenter §5.2 文档示例 `AuthAccountProfileSource` 派生自 `UserProfileSourceBase`，oracle3 C1 澄清为文档示例模式、非存量代码）——每个装配实例重复样板；装配层跳过数据属主直查底层实体，破坏数据归属主边界。
- **零查询契约**：装配桥接只能直查 `AuthAccountEntity`（或注入 DataService）——绕过对外契约，无法复用与 TokenService 一致的查询语义，跨扩展耦合实体内部形态。
- **引 UserCenter 主包**：若认证中心为实现契约而引 UserCenter 主包（含门面/写逻辑）——实现方反向依赖实现包，且违反扩展间 L2 门控（`TKWF0022` Error：跨扩展依赖应引 `.Abstractions` 契约包）。

## 三、使用场景

### 适用场景

- 消费方同时白名单声明认证中心 + UserCenter 后**自动获得真实档案**：`IUserCenterQueryService.GetProfileAsync` 返回 AuthAccount 数据（Phone 传原始值、脱敏归 UserCenter 门面）；**新装配实例无需再写桥接类**（已写过者亦可删除——UserCenter 门面注入契约不变，零代码变更）。
- 装配层/内部消费 `IAuthAccountQueryService` 只读查询（按 UId/Phone/MpOpenId/WebOpenId）；TokenService 等内部消费查询或 DataService 的**写路径不变**（v0.2.0 零改动）。
- 认证中心测试宿主 / ProfileSource 实现注入只读契约（防误用写方法——完整 `AuthAccountEntity` 仅服务内部）。

### 不适用边界

- **持久化契约**：`IRedemptionHistorySource`/`IUserAppsSource`（兑换历史/我的应用）归授权面扩展（另行立项）——本 ADR 仅承接 `IUserProfileSource` 档案契约，无其他契约承接。
- **两契约不可合并**（oracle3 C6）：`IAuthAccountQueryService`（返回完整 `AuthAccountEntity`，含 `TokenVersion`/`PasswordHash`/`IsEnabled`——TokenService 需要）与 `IUserProfileSource`（返回 `UserProfileDto` 公共档案子集，无敏感字段，Phone 门面脱敏）——消费者不同、数据形态不同、合并即暴露 AuthAccount 内部形态给 UserCenter 或强制 TokenService 消费档案 DTO，均破坏数据归属主边界。
- 认证中心自有查询契约**不拆独立 `.Abstractions` 包**（YAGNI——跨扩展消费方需求不存在，UserCenter 消费经其契约包即可）。

## 四、选项

### 选项 A：数据属主扩展实现契约（选定）

- 描述：认证中心（AuthAccount 数据属主）在自身 Initializer 内实现 `IUserProfileSource`（`AuthAccountUserProfileSource`，public sealed）+ `TryAddScoped` 注册；同时自建 `IAuthAccountQueryService` 只读查询契约；csproj 引 `TKWF.Ext.UserCenter.Abstractions` 契约包。
- 优点：消费方零桥接样板；数据属主单点实现、查询语义唯一；引契约包不引主包（单向无循环 + L2 门控合规）；有直接先例（`IdentityPasswordManager` public sealed + TryAddScoped）。
- 缺点：认证中心新增对 UserCenter.Abstractions 契约包的编译期依赖（契约演进需双方同步）；须持续保持两契约边界（不可合并）。

### 选项 B：装配层/主项目实现接口（Provider 样板）

- 描述：契约由消费方装配实例实现（UserCenter §5.2 示例 `AuthAccountProfileSource` 派生 `UserProfileSourceBase`）。
- 优点：认证中心零依赖、不引入契约包。
- 缺点：每个装配实例重复样板代码；装配层绕过数据属主直查底层实体，破坏数据归属主边界；无法复用认证中心查询语义——文档示例模式非终态（oracle3 C1 澄清）。

### 选项 C：绕契约自行查 `AuthAccountEntity`（过渡期缺口方式）

- 描述：UserCenter 方案 P2 注记的过渡方式——装配实例不经契约直接查 `AuthAccountEntity`。
- 缺点：跨扩展耦合实体内部形态；无查询面复用；维持缺口而非补强（本 ADR 即为消除该缺口而立）。

## 五、决策

选定：**选项 A——数据属主扩展实现契约**。

1. **承接 `IUserProfileSource`**：实现类命名 `AuthAccountUserProfileSource`（**public sealed**——区别于 UserCenter 文档示例装配桥接类同名的 `AuthAccountProfileSource`，消除检索/跳转歧义，oracle3 C2）+ `TryAddScoped<IUserProfileSource, AuthAccountUserProfileSource>()` 注册（先例：`IdentityPasswordManager` public sealed + TryAddScoped；`IdentityRoleProvider` public 作辅证）。映射以账号 `UId` 数据源为准（不信任传入的 `userId`，P3 注释增强）；Phone 传原始值，脱敏归 UserCenter 门面（实现方不得自行 Mask）。
2. **自建查询契约 `IAuthAccountQueryService`**：只读 4 方法（`GetByUIdAsync`/`GetByPhoneAsync`/`GetByWechatMpOpenIdAsync`/`GetByWechatWebOpenIdAsync`，均含 `CancellationToken` 参数）；实现 `AuthAccountQueryService` **internal sealed**，委托 `AuthAccountEntityDataService`（红线合规——零 IFreeSql/零 `IEntityDAC` 直注入）；Initializer 补 `TryAddScoped<IAuthAccountQueryService, AuthAccountQueryService>()`。
3. **依赖方向裁定**：认证中心 csproj 引 **`TKWF.Ext.UserCenter.Abstractions`（契约包）**而非 UserCenter 主包——实现方引契约、不引实现，**单向无循环、L2 门控合规**；UserCenter 主包**零引用认证中心**（保持独立）。
4. **微信绑定推导**：`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` **任一非空 = 已绑定**（AuthAccount 无显式 `IsWechatBound` 字段，推导为唯一准确来源；oracle3 C4 措辞精确化——微信登录必然写 Mp OpenId `snsapi_base` 或 Web OpenId `snsapi_login` 之一，`UnionId` 由装配层可选补充非必然；三字段全空 = 未绑定）。

## 六、后果

### 正面影响

- 消费方白名单声明认证中心 + UserCenter 后自动获得真实档案，**新装配实例零桥接样板**；已写过桥接类者可删除（UserCenter 门面注入契约不变，零代码变更）。
- 数据归属主边界保持：UserCenter 门面只经 `IUserProfileSource` 拿公共档案子集，不触 `AuthAccountEntity` 内部形态（`TokenVersion`/`PasswordHash` 不出扩展）。
- ADR48 D7 依赖倒置翻开**新形态先例**：主扩展实现他扩展读取契约（数据属主扩展实现契约 + 引契约包 + 单向无循环）。
- 查询语义唯一、红线合规（查询契约与档案源实现均委托 DataService）。

### 负面影响 / 风险

- 认证中心新增编译期依赖 `UserCenter.Abstractions` 契约包——契约演进（如 `UserProfileDto` 字段变更）时认证中心需同步适配。
- 两契并存易被误读为重复（查询服务 vs 档案源）——使用指南需明确边界（oracle3 C6：消费者/数据形态/合并代价三差异）。
- 微信绑定推导依赖 openid 写入时序（微信登录先于绑定）——装配层若仅补 `UnionId` 而缺 OpenId，仍可经 `UnionId` 判定（推导条件已覆盖）。

### 后续待办

- 认证中心 README/使用指南补「查询契约 + UserCenter 承接」章节；UserCenter 使用指南终态说明更新（新装配无需桥接类，已写过者可删）。
- **原子条件 UPDATE 安全迭代（oracle3 C3，独立 v0.2.x 跟踪）**：`IncrementTokenVersionAsync` read-modify-write 非原子（并发改密/绑定变更丢失更新 → 旧 Refresh 不失效，safety 级）——另行迭代，不在本 ADR 范围。
- 黑名单清理 / 按 UserId 批量撤销为 capability 级能力完善，另行规划（不纳入本决策）。

## 七、关联文档

- **ADR48 D7**（扩展间依赖倒置——本 ADR 为其「主扩展实现他扩展读取契约」新形态**首例**，区别于既往「契约拆包」先例如 `Emailing.Abstractions`/`SecurityLog.Abstractions`）。
- oracle3 评审（`bg_6682dabd` **PASS WITH CONDITIONS**——C1-C6 全修订：C1 桥接类文档示例澄清 / C2 命名防碰撞 / C3 原子 UPDATE 分层 / C4 微信推导措辞 / C5 先例显式引用 / C6 两契约边界）。
- 开发方案：《框架实战教学/05-倒推优化-认证中心-查询契约与UserCenter承接-开发方案.md》（v0.1.1，oracle3 修订版）。
- 既有 ADR：`ADR-Authentication-认证中心命名与边界.md`（用户中心独立立项裁定）、`ADR-Authentication-令牌契约与密钥管理.md`。

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-09-30 | 初始版本——认证中心 v0.2.0 承接 UserCenter 终态路径：数据属主扩展实现 `IUserProfileSource`（`AuthAccountUserProfileSource` public sealed + TryAddScoped，先例 `IdentityPasswordManager`）+ 自建 `IAuthAccountQueryService` 只读查询契约（internal sealed 委托 DataService，红线合规）；依赖方向裁定（引 `UserCenter.Abstractions` 契约包非主包，单向无循环，L2 门控合规）；微信绑定推导措辞精确化（Mp/Web OpenId + UnionId 任一非空，UnionId 可选非必然）；ADR48 D7「主扩展实现他扩展读取契约」新形态首例 |