# 05-倒推优化开发方案：认证中心 v0.2.0 查询契约与 UserCenter 承接

> **系列**：框架实战教学（开篇见 [`01-扩展模块最优解探索-Schema级数据组合-开篇.md`](./01-扩展模块最优解探索-Schema级数据组合-开篇.md)）
> **案例定位**：认证中心（Authentication）v0.1.0 实施后**首个能力补强**——补对外查询契约缺口 + 承接 UserCenter 终态路径（实现 `IUserProfileSource`）
> **涉及扩展**：`TKWF.Ext.Authentication`（当前 v0.1.0）→ 目标 v0.2.0；消费 `TKWF.Ext.UserCenter.Abstractions`（契约）
> **状态**：📋 待 Oracle 评审
> **版本**：v0.1.0-draft

---

## 一、目的与目标

对认证中心（v0.1.0 刚实施完成）执行**查询契约补强 + UserCenter 终态承接**：

1. **补 `IAuthAccountQueryService` 查询契约**（v0.1.0 缺口，UserCenter 方案 P2 注记"若认证中心 v0.1.0 未暴露查询服务，装配实例自行查 AuthAccountEntity"）——`AuthAccountEntityDataService` 已有完整查询方法（`GetByUIdAsync`/`GetByPhoneAsync`/`GetByWechatMpOpenIdAsync`/`GetByWechatWebOpenIdAsync`）但**无对外服务接口**；补契约 + 实现类委托 DataService（对齐 `IUserManager`/`IdentityAuthService` 门面先例）。
2. **承接 UserCenter 终态路径**（核心——UserCenter 方案 §5.9 终态：认证中心扩展内实现 `IUserProfileSource`）——认证中心实现 `IUserProfileSource`（引 `UserCenter.Abstractions`）+ `TryAddScoped` 注册 → **装配实例删除过渡桥接类（`AuthAccountProfileSource`），消费方零代码变更**；`IUserCenterQueryService` 自动获得真实档案数据。
3. **红线合规**：查询契约 + `IUserProfileSource` 实现均经 DataService 委托（零 IFreeSql/零 IEntityDAC 直注入）；UserCenter 主包零引用认证中心（L2 门控：认证中心引 **Abstractions** 而非 UserCenter 主包）。

**验收**：`IAuthAccountQueryService` 公开（4 查询方法）+ `IUserProfileSource` 实现装配（消费方白名单声明后自动接线）；UserCenter 过渡桥接类可从装配实例删除；既有测试断言全绿（锚点见 §六）；新增契约/映射用例。

---

## 二、现状分析（精确到文件:行）

### 2.1 查询缺口——DataService 方法齐备但无对外契约

**`AuthAccountEntityDataService.cs` 现有方法**（v0.1.0 实施，全部已存在）：

| 方法 | 用途 | 消费方 |
|------|------|--------|
| `GetByUIdAsync(string uid, ct)`（L18） | 按平台内部 id 查账号 | `TokenService` L83/L196（内部） |
| `GetByPhoneAsync(string phone, ct)` | 按手机号查 | SmsAuthenticationProvider（内部） |
| `GetByWechatMpOpenIdAsync(string openId, ct)` | 按公众号 openid 查 | WeChatAuthenticationProvider（内部） |
| `GetByWechatWebOpenIdAsync(string openId, ct)` | 按扫码 openid 查 | WeChatAuthenticationProvider（内部） |

- **无 `IAuthAccountQueryService` 对外接口**——认证中心 10 个接口（`ITokenService`/`IAuthLoginAttemptService`/`IOAuthTicketService`/`IPlatformAccountMapService`/`IPlatformCredentialService`/`ISmsVerificationService`/`IAuthenticationProvider`/`ITokenVerifier`/`IAuthorizationMapper`/`IWeChatApiClient`）均**无账号查询契约**。
- **影响**：UserCenter 过渡期装配实例桥接（`AuthAccountProfileSource`）无法经契约查 AuthAccount——UserCenter 方案 P2 已注记此缺口（装配实例自行查 AuthAccountEntity，绕过契约）。

### 2.2 UserCenter 契约承接——字段映射确认

**UserCenter `UserProfileDto`**（`UserCenter.Abstractions`）：

| UserProfileDto 字段 | AuthAccountEntity 源 | 映射 |
|--------------------|--------------------|------|
| `UserId` | `UId`（string） | 直接 |
| `Phone` | `Phone`（string?） | 直接（**原始值**——UserCenter 门面负责脱敏） |
| `IsWechatBound` | `WechatMpOpenId ?? WechatWebOpenId ?? UnionId 非空` | **推导**（无显式 bool 字段——openid/unionid 任一非空 = 已绑定） |
| `Nickname` | `Nickname`（string?） | 直接 |
| `AvatarUrl` | `Avatar`（string?） | 直接 |
| `IsTeacherVerified` | `TeacherVerified`（bool） | 直接 |
| `AuthLevel` | `AuthLevel`（int） | 直接 |

映射完全可行——微信绑定需推导（`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` 任一非空）。

### 2.3 依赖关系（L2 门控合规）

- 认证中心 v0.1.0 **零引用 UserCenter**（v0.1.0 已确认）
- v0.2.0 引入 `UserCenter.Abstractions`（**契约包**——非 UserCenter 主包）——ADR48 D7 合规（实现方引契约，不引实现）
- UserCenter 主包**不引认证中心**（保持独立）——契约实现方反向依赖 Abstractions

---

## 三、优化设计

### 3.1 `IAuthAccountQueryService` 查询契约（认证中心 v0.2.0）

> **两契约边界（oracle3 C6）**：`IAuthAccountQueryService`（认证中心自有）与 `IUserProfileSource`（UserCenter 契约）**不可合并**——① 消费者不同：查询服务 → TokenService（内部）/装配桥接（过渡）/ProfileSource（注入只读契约防误用写方法）；档案源 → UserCenter 门面；② 数据形态不同：查询服务返回 `AuthAccountEntity`（完整实体含 TokenVersion/IsEnabled/PasswordHash——TokenService 需要）；档案源返回 `UserProfileDto`（公共档案子集，无敏感字段，Phone 门面脱敏）；③ 合并代价：暴露 AuthAccountEntity 给 UserCenter = 破坏数据归属主边界（UserCenter 不应知 AuthAccount 内部形态），或强制 TokenService 消费 UserProfileDto（错误——需 TokenVersion/IsEnabled 非档案字段）。

```csharp
namespace TKWF.Ext.Authentication;

/// <summary>账号查询服务——对外只读查询契约（UserCenter 桥接 / 装配实例 / 内部复用）。</summary>
public interface IAuthAccountQueryService
{
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default);
}

/// <summary>账号查询服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 IdentityPasswordManager 先例一致（public sealed + TryAddScoped 为注册方）；
/// 本类仅门面委托无外部扩展需求，internal 即可（oracle3 核验：AuthAccountEntityDataService 亦 internal，注入合法）。</para></summary>
internal sealed class AuthAccountQueryService(AuthAccountEntityDataService dataService) : IAuthAccountQueryService
{
    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => dataService.GetByUIdAsync(uid, ct);
    public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => dataService.GetByPhoneAsync(phone, ct);
    public Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
        => dataService.GetByWechatMpOpenIdAsync(openId, ct);
    public Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
        => dataService.GetByWechatWebOpenIdAsync(openId, ct);
}
```

- **注册**：`AuthCenterExtensionInitializer.ConfigureServices` 补 `TryAddScoped<IAuthAccountQueryService, AuthAccountQueryService>()`（对齐 `IUserManager` 门面 TryAdd 语义）。
- **先例（oracle3 C5 精确化）**：`IdentityPasswordManager`（`IdentityExtensionInitializer` L47 `TryAddScoped<IAccountPasswordManager, IdentityPasswordManager>()` + `public sealed class`）——跨扩展契约实现最直接先例（public sealed + TryAddScoped + 数据属主扩展实现他扩展契约）。

### 3.2 `IUserProfileSource` 实现（认证中心承接 UserCenter 终态路径）

```csharp
using TKWF.Ext.UserCenter;   // Abstractions 契约

namespace TKWF.Ext.Authentication;

/// <summary>公共档案源实现——认证中心作为 AuthAccount 属主实现 UserCenter 契约（终态路径）。
/// ⚠️ Phone 返回原始值——UserCenter 门面强制脱敏（实现方不得自行 Mask）。
/// ⚠️ 命名（oracle3 C2）：区别于 UserCenter 文档示例的装配层桥接类（同名 AuthAccountProfileSource——
/// 新实现类命名 AuthAccountUserProfileSource 消除碰撞）。</summary>
public sealed class AuthAccountUserProfileSource(IAuthAccountQueryService accounts)
    : IUserProfileSource
{
    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        // ⚠️ 以账号 UId 为准，不信任传入 userId（P3 注释增强——数据源为准防误读）
        var account = await accounts.GetByUIdAsync(userId, ct);
        if (account is null) return null;
        return new UserProfileDto(
            UserId: account.UId,               // 数据源为准（account.UId）——非透传 userId
            Phone: account.Phone,              // 原始值——门面脱敏
            IsWechatBound: !string.IsNullOrEmpty(account.WechatMpOpenId)
                || !string.IsNullOrEmpty(account.WechatWebOpenId)
                || !string.IsNullOrEmpty(account.UnionId),
            Nickname: account.Nickname,
            AvatarUrl: account.Avatar,
            IsTeacherVerified: account.TeacherVerified,
            AuthLevel: account.AuthLevel);
    }
}
```

- **注册**：`AuthCenterExtensionInitializer.ConfigureServices` 补 `TryAddScoped<IUserProfileSource, AuthAccountUserProfileSource>()`——**扩展内实现契约 + TryAdd 注册，装配层零桥接**（UserCenter 机制优势终态落地）。
- **依赖**：认证中心 csproj 加 `ProjectReference`（或 PackageReference）`UserCenter.Abstractions`——**L2 门控合规**（引契约包非实现包）。
- **先例（oracle3 C5）**：`IdentityPasswordManager` 为**主先例**（跨扩展契约实现 + public sealed + TryAddScoped）；`IdentityRoleProvider`（public + AddScoped 覆盖默认）作 public 辅证（C2 论据）。
- **消费方效果**：白名单声明认证中心 + UserCenter 后，`IUserCenterQueryService.GetProfileAsync` 自动获得真实档案；**过渡桥接类说明（oracle3 C1 澄清）**：`AuthAccountProfileSource`（UserProfileSourceBase 派生）为 UserCenter §5.2 文档示例模式（非存量代码）——本方案落地后**新装配实例无需再写桥接类**（已写过者可删除——零代码变更，指 UserCenter 门面注入契约不变）。

### 3.3 边界与划界

| 面 | 变更 | 说明 |
|----|------|------|
| 认证中心新增 | `IAuthAccountQueryService` + `IUserProfileSource` 实现 | 契约 + 终态承接 |
| 认证中心写路径 | **零改动** | `TokenService`/`AuthLoginAttemptService`/`OAuthTicketService` 等内部逻辑不变（查询方法复用经契约） |
| UserCenter 主包 | **零改动** | 契约已冻结（v0.1.0）——认证中心实现即完成终态 |
| UserCenter Abstractions | **零改动** | 契约中立 |
| 装配实例 | **新装配实例无需再写桥接类**（oracle3 C1 澄清） | `AuthAccountProfileSource`（UserProfileSourceBase 派生）为 UserCenter §5.2 **文档示例模式**（非存量代码）——已写过者可删除（零代码变更 = UserCenter 门面注入契约不变）；**新装配实例**白名单声明认证中心 + UserCenter 后直接获得真实档案，无需桥接 |
| 生产部署 | 无视图/无迁移 | 纯代码 + DI 注册 |

---

## 四、裁定点（需评审确认）

| # | 裁定点 | 建议 | 理由 |
|---|--------|------|------|
| **C1** | `IAuthAccountQueryService` 查询契约形态：只读接口（4 方法）？ | **只读接口（GetByUId/GetByPhone/GetByWechatMp/GetByWechatWeb）** | 对齐 `AuthAccountEntityDataService` 现有查询面（v0.1.0 已实现方法）；写路径（Create/Update/AdminDelete）不暴露（内部 TokenService 职责） |
| **C2** | `IUserProfileSource` 实现类命名与可见性？ | **`AuthAccountUserProfileSource`（public sealed）** | **命名（oracle3 C2）**：区别于 UserCenter 文档示例装配桥接类 `AuthAccountProfileSource`（消除检索/跳转歧义）；**public（C2 论据）**：消费方可能直接注入 `IUserProfileSource` 类型（装配实例临时覆写）——主先例 `IdentityPasswordManager`（public sealed + TryAddScoped），`IdentityRoleProvider`（public）辅证 |
| **C3** | 微信绑定推导（openid/unionid 任一非空）？ | **推导**（`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` 任一非空 = true） | AuthAccount 无显式 `IsWechatBound` 字段——推导唯一准确来源；**措辞精确化（oracle3 C4）**：微信登录必然写 `WechatMpOpenId`（snsapi_base）或 `WechatWebOpenId`（snsapi_login）之一；`UnionId` 由装配层可选补充（非必然）——三字段任一非空即已绑定 |
| **C4** | `IUserProfileSource` 注册语义：TryAddScoped vs AddScoped？ | **TryAddScoped** | 主先例 `IdentityPasswordManager`（`IdentityExtensionInitializer` L47 `TryAddScoped<IAccountPasswordManager, IdentityPasswordManager>()`）——跨扩展契约实现语义；区别于 `IdentityRoleProvider`（AddScoped 覆盖默认——那是角色 Provider 特殊场景，oracle3 C5 精确化） |
| **C5** | 认证中心 v0.2.0 是否纳入审核报告遗留项（原子条件 UPDATE / 黑名单清理 / 按 UserId 批量撤销）？ | **不纳入本方案（分层标注，另行迭代）** | 本方案聚焦**查询契约 + UserCenter 承接**（倒推优化主题）。**oracle3 C3 严重度分层**：① **原子条件 UPDATE = 既有安全正确性缺口（safety）**——`IncrementTokenVersionAsync`（L49-55）read-modify-write 非原子（读 v=5→改 v=6→写），并发改密/绑定变更丢失更新 → 旧 Refresh Token 不失效（TokenVersion 是密码变更后旧 Refresh 失效的闭环依据）——**独立 v0.2.x 安全迭代跟踪（优先）**，§九 P6 列入安全迭代待办；② 黑名单清理/按 UserId 批量撤销 = **能力完善（capability）**——另行规划。本方案不混入 |

---

## 五、影响面

| 面 | 变更 | 说明 |
|----|------|------|
| 认证中心 csproj | 加 `UserCenter.Abstractions` 引用 | L2 门控合规（契约包） |
| AuthCenterExtensionInitializer | 补 2 注册（QueryService + ProfileSource） | TryAddScoped |
| 装配实例（UserCenter 消费方） | **新装配实例无需再写桥接类**（oracle3 C1 澄清——`AuthAccountProfileSource` 为文档示例非存量代码；已写过者可删——零代码变更指 UserCenter 门面注入契约不变） | 认证中心 `IUserProfileSource` 实现接管 |
| 测试 | 新增契约/映射用例 + 过渡类移除验证 | 见 §六 |

---

## 六、测试锚点与新增用例

### 既有断言须保持全绿（写路径零改动 → 天然保留）

- **认证中心**：`TokenServiceTests`（签发/验签/Refresh rotation）/`AuthLoginAttemptServiceTests`/`OAuthTicketServiceTests`/`SmsVerificationServiceTests`/`PlatformAccountMapServiceTests`/`PlatformCredentialServiceTests`——写路径不动，天然保留。
- **UserCenter**：`UserCenterQueryServiceTests`（门面降级矩阵）——契约不变，天然保留。

### 新增用例

| # | 用例 | 验证点 |
|---|------|--------|
| N1 | `AuthAccountQueryService` 4 查询方法委托 DataService | 契约面 + 委托正确 |
| N2 | `AuthAccountProfileSource` 映射：全字段（UId/Phone/Nickname/Avatar/TeacherVerified/AuthLevel） | 映射正确 + Phone 原始值 |
| N3 | 微信绑定推导矩阵（Mp 绑定 / Web 绑定 / 仅 UnionId / 全空） | openid/unionid 任一非空 = true |
| N4 | `IUserProfileSource` 未注册降级（UserCenter 门面 null）| 契约未实现方装配时零阻塞 |
| N5 | 装配冒烟：认证中心 `IUserProfileSource` + UserCenter `IUserCenterQueryService` 全链路（消费方 Host 白名单声明） | 终态装配零桥接 |

---

## 七、实施清单

1. **认证中心**：
   - 新增 `IAuthAccountQueryService`（接口）+ `AuthAccountQueryService`（实现，委托 DataService）
   - 新增 `AuthAccountUserProfileSource`（`IUserProfileSource` 实现——引 `UserCenter.Abstractions`；命名区别于 UserCenter 文档示例桥接类 `AuthAccountProfileSource`，oracle3 C2）
   - csproj 加 `UserCenter.Abstractions` 引用（L2 门控合规）
   - `AuthCenterExtensionInitializer.ConfigureServices` 补 `TryAddScoped<IAuthAccountQueryService, AuthAccountQueryService>()` + `TryAddScoped<IUserProfileSource, AuthAccountUserProfileSource>()`
2. **测试**：新增 N1-N5 用例 + 全量回归
3. **文档**：认证中心 README/使用指南补"查询契约 + UserCenter 承接"章节；UserCenter 使用指南§二 终态说明更新（认证中心已实现——新装配实例无需桥接类，已写过者可删）
4. **落档**：教学系列 §八 路线图更新（认证中心 📋 方案待评审 → ✅ 方案已评审）+ 本篇案例编号 05

---

## 八、验收标准

- [ ] `IAuthAccountQueryService` 公开（4 查询方法）+ `AuthAccountQueryService` 委托 DataService（红线合规）
- [ ] `AuthAccountUserProfileSource` 实现 `IUserProfileSource`（映射全字段 + 微信绑定推导 + Phone 原始值）
- [ ] 消费方白名单声明认证中心 + UserCenter 后 `IUserCenterQueryService.GetProfileAsync` 返回真实档案（装配冒烟 N5）
- [ ] **新装配实例无需再写桥接类**（已写过者可删——零代码变更指 UserCenter 门面注入契约不变，oracle3 C1 澄清）
- [ ] L2 门控零违规（认证中心引 Abstractions 非主包）
- [ ] 既有断言全绿 + 新增 N1-N5 用例绿
- [ ] `lsp_diagnostics` 变更文件干净
- [ ] 两扩展文档补章节

---

## 九、实施前补全项

| # | 项 | 说明 |
|---|----|------|
| P1 | `AuthAccountEntity` 微信绑定字段核对（oracle3 C4 结论已核验） | oracle3 已实地核验：微信登录必然写 `WechatMpOpenId`（snsapi_base）或 `WechatWebOpenId`（snsapi_login）之一；`UnionId` 由装配层可选补充（L67 注释）——推导条件（三字段任一非空 = 已绑定）成立；N3 测试补"仅手机号账号（三字段全空）→ false"边界 |
| P2 | 认证中心 csproj 引用方式 | ProjectReference（本仓库联调）vs PackageReference（NuGet 发布）——对齐双模式（UseLocalFw）；`UserCenter.Abstractions` 是契约包，消费方引用主包时传递解析 |
| P3 | `IUserProfileSource` 注册是否影响 UserCenter 门面构造 | UserCenter `UserCenterQueryService` 经 `GetService<IUserProfileSource>()` 可空解析——认证中心注册后自动生效，无构造破坏 |
| P4 | 测试宿主（AuthenticationTests/UserCenterTests）DI 接线 | 认证中心测试宿主手动构造 `AuthAccountQueryService`/`AuthAccountUserProfileSource` 或经 Host；UserCenter 测试经 Mock 源（不变） |
| P5 | ADR 落档（oracle3 P2 新形态先例） | 认证中心实现 UserCenter 契约 = **ADR48 D7 首次"主扩展实现他扩展读取契约"**（区别于既往"契约拆包"）——ADR 记录依赖方向裁定：数据属主扩展实现读取契约，引契约包不引主包，单向无循环 |
| P6 | **原子条件 UPDATE 安全迭代待办（oracle3 C3）** | `IncrementTokenVersionAsync`（L49-55）read-modify-write 非原子（并发丢失更新 → 旧 Refresh 不失效）——**独立 v0.2.x 安全迭代**；可选：本方案顺带改 `UPDATE AuthAccount SET TokenVersion=TokenVersion+1 WHERE UId=@uid` 原子条件更新（一行改动零风险）——实施时定 |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0-draft | 初始草案——认证中心 v0.1.0 查询缺口（无 IAuthAccountQueryService，UserCenter P2 注记）+ UserCenter 终态承接（认证中心实现 IUserProfileSource——§5.9 终态演进落地） |
| 2026-09-30 | v0.1.1 | **oracle3 评审 PASS WITH CONDITIONS（`bg_6682dabd`）6 条件全修订**——C1（过渡桥接类为文档示例非存量代码——"零代码变更"措辞精确化，§3.2/§3.3/§五/§八）+ C2（命名碰撞——实现类改 `AuthAccountUserProfileSource`，§3.2/§四/§七/§八/§九）+ C3（原子条件 UPDATE 严重度分层 safety/capability + P6 安全迭代待办，§四 C5）+ C4（微信推导理由措辞精确化——UnionId 可选非必然，§四 C3）+ C5（先例显式引用 `IdentityPasswordManager`——public sealed + TryAddScoped 最直接先例，§3.1/§3.2/§四）+ C6（两契约边界显式陈述——消费者/数据形态/合并代价，§3.1）；P1-P4 建议纳入（N3 微信矩阵补仅手机号边界 / ADR 新形态 / UId 数据源为准注释 / 不拆 Abstractions YAGNI） |

---

## 评审记录

| 日期 | 评审人 | 结论 | 修订 |
|------|--------|------|------|
| 2026-09-30 | oracle3 | **PASS WITH CONDITIONS**——核心设计（IAuthAccountQueryService 只读契约 + AuthAccountUserProfileSource public sealed + TryAddScoped 双注册 + 引 Abstractions）与 IdentityPasswordManager 先例完全吻合，可直接实施；6 条件（C1-C6）+ 4 建议（P1-P4） | 全部条件修订完成（§3.1/§3.2/§3.3/§四/§五/§七/§八/§九 + 变更记录 v0.1.1） |
|------|--------|------|------|
| 2026-09-30 | oracle3 | 待评审 | — |
-->
