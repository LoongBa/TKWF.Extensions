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

/// <summary>账号查询服务实现——委托 AuthAccountEntityDataService（红线合规）。</summary>
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

### 3.2 `IUserProfileSource` 实现（认证中心承接 UserCenter 终态路径）

```csharp
using TKWF.Ext.UserCenter;   // Abstractions 契约

namespace TKWF.Ext.Authentication;

/// <summary>公共档案源实现——认证中心作为 AuthAccount 属主实现 UserCenter 契约（终态路径）。
/// ⚠️ Phone 返回原始值——UserCenter 门面强制脱敏（实现方不得自行 Mask）。</summary>
public sealed class AuthAccountProfileSource(IAuthAccountQueryService accounts)
    : IUserProfileSource
{
    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var account = await accounts.GetByUIdAsync(userId, ct);
        if (account is null) return null;
        return new UserProfileDto(
            UserId: account.UId,
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

- **注册**：`AuthCenterExtensionInitializer.ConfigureServices` 补 `TryAddScoped<IUserProfileSource, AuthAccountProfileSource>()`——**扩展内实现契约 + TryAdd 注册，装配层零桥接**（UserCenter 机制优势终态落地）。
- **依赖**：认证中心 csproj 加 `ProjectReference`（或 PackageReference）`UserCenter.Abstractions`——**L2 门控合规**（引契约包非实现包）。
- **消费方效果**：白名单声明认证中心 + UserCenter 后，`IUserCenterQueryService.GetProfileAsync` 自动获得真实档案；**装配实例删除 `AuthAccountProfileSource` 过渡桥接类，零代码变更**（UserCenter 方案 §5.9 终态演进表落地）。

### 3.3 边界与划界

| 面 | 变更 | 说明 |
|----|------|------|
| 认证中心新增 | `IAuthAccountQueryService` + `IUserProfileSource` 实现 | 契约 + 终态承接 |
| 认证中心写路径 | **零改动** | `TokenService`/`AuthLoginAttemptService`/`OAuthTicketService` 等内部逻辑不变（查询方法复用经契约） |
| UserCenter 主包 | **零改动** | 契约已冻结（v0.1.0）——认证中心实现即完成终态 |
| UserCenter Abstractions | **零改动** | 契约中立 |
| 装配实例 | **删除过渡桥接类** | `AuthAccountProfileSource`（UserProfileSourceBase 派生）移除——认证中心实现接管 |
| 生产部署 | 无视图/无迁移 | 纯代码 + DI 注册 |

---

## 四、裁定点（需评审确认）

| # | 裁定点 | 建议 | 理由 |
|---|--------|------|------|
| **C1** | `IAuthAccountQueryService` 查询契约形态：只读接口（4 方法）？ | **只读接口（GetByUId/GetByPhone/GetByWechatMp/GetByWechatWeb）** | 对齐 `AuthAccountEntityDataService` 现有查询面（v0.1.0 已实现方法）；写路径（Create/Update/AdminDelete）不暴露（内部 TokenService 职责） |
| **C2** | `IUserProfileSource` 实现类 public 还是 internal？ | **public**（`AuthAccountProfileSource`） | 消费方可能需要直接注入 `IUserProfileSource` 类型（如装配实例临时覆写）；对齐 `IdentityRoleProvider` public 先例 |
| **C3** | 微信绑定推导（openid/unionid 任一非空）？ | **推导**（`WechatMpOpenId ?? WechatWebOpenId ?? UnionId` 任一非空 = true） | AuthAccount 无显式 `IsWechatBound` 字段——推导是唯一准确来源（绑定必然写 openid/unionid） |
| **C4** | `IUserProfileSource` 注册语义：TryAddScoped vs AddScoped？ | **TryAddScoped** | 对齐 UserCenter 契约实现方先例（认证中心 TryAdd——装配实例/其他实现优先）；区别于 `IdentityRoleProvider`（AddScoped 覆盖默认——那是角色 Provider 特殊场景） |
| **C5** | 认证中心 v0.2.0 是否纳入审核报告遗留项（原子条件 UPDATE / 黑名单清理 / 按 UserId 批量撤销）？ | **不纳入本方案（能力完善另行规划）** | 本方案聚焦**查询契约 + UserCenter 承接**（倒推优化主题）；遗留项属安全能力完善，独立 v0.2.0 迭代跟踪（不混入） |

---

## 五、影响面

| 面 | 变更 | 说明 |
|----|------|------|
| 认证中心 csproj | 加 `UserCenter.Abstractions` 引用 | L2 门控合规（契约包） |
| AuthCenterExtensionInitializer | 补 2 注册（QueryService + ProfileSource） | TryAddScoped |
| 装配实例（UserCenter 消费方） | 删除 `AuthAccountProfileSource` 过渡桥接类 | 认证中心实现接管——零代码变更 |
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
   - 新增 `AuthAccountProfileSource`（`IUserProfileSource` 实现——引 `UserCenter.Abstractions`）
   - csproj 加 `UserCenter.Abstractions` 引用（L2 门控合规）
   - `AuthCenterExtensionInitializer.ConfigureServices` 补 `TryAddScoped<IAuthAccountQueryService, AuthAccountQueryService>()` + `TryAddScoped<IUserProfileSource, AuthAccountProfileSource>()`
2. **测试**：新增 N1-N5 用例 + 全量回归
3. **文档**：认证中心 README/使用指南补"查询契约 + UserCenter 承接"章节；UserCenter 使用指南§二 终态说明更新（认证中心已实现——过渡桥接类可删）
4. **落档**：教学系列 §八 路线图更新（认证中心 ⚪ 未列入 → ✅ 方案已评审）+ 本篇案例编号 05

---

## 八、验收标准

- [ ] `IAuthAccountQueryService` 公开（4 查询方法）+ `AuthAccountQueryService` 委托 DataService（红线合规）
- [ ] `AuthAccountProfileSource` 实现 `IUserProfileSource`（映射全字段 + 微信绑定推导 + Phone 原始值）
- [ ] 消费方白名单声明认证中心 + UserCenter 后 `IUserCenterQueryService.GetProfileAsync` 返回真实档案（装配冒烟 N5）
- [ ] 装配实例过渡桥接类可删除（认证中心实现接管——零代码变更）
- [ ] L2 门控零违规（认证中心引 Abstractions 非主包）
- [ ] 既有断言全绿 + 新增 N1-N5 用例绿
- [ ] `lsp_diagnostics` 变更文件干净
- [ ] 两扩展文档补章节

---

## 九、实施前补全项

| # | 项 | 说明 |
|---|----|------|
| P1 | `AuthAccountEntity` 微信绑定字段核对 | 确认 `WechatMpOpenId`/`WechatWebOpenId`/`UnionId` 三字段非空语义（v0.1.0 微信登录写入路径）——推导条件准确 |
| P2 | 认证中心 csproj 引用方式 | ProjectReference（本仓库联调）vs PackageReference（NuGet 发布）——对齐双模式（UseLocalFw）；`UserCenter.Abstractions` 是契约包，消费方引用主包时传递解析 |
| P3 | `IUserProfileSource` 注册是否影响 UserCenter 门面构造 | UserCenter `UserCenterQueryService` 经 `GetService<IUserProfileSource>()` 可空解析——认证中心注册后自动生效，无构造破坏 |
| P4 | 测试宿主（AuthenticationTests/UserCenterTests）DI 接线 | 认证中心测试宿主手动构造 `AuthAccountQueryService`/`AuthAccountProfileSource` 或经 Host；UserCenter 测试经 Mock 源（不变） |
| P5 | ADR 落档 | 认证中心 v0.2.0 查询契约 + UserCenter 承接为 ADR48 D7 依赖倒置第 7 次落地先例（扩展间契约：认证中心实现 UserCenter 契约）——立 ADR 记录 |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0-draft | 初始草案——认证中心 v0.1.0 查询缺口（无 IAuthAccountQueryService，UserCenter P2 注记）+ UserCenter 终态承接（认证中心实现 IUserProfileSource——§5.9 终态演进落地）；待 Oracle 评审 |

---

<!--
评审记录（Oracle 评审后填写）：

| 日期 | 评审人 | 结论 | 修订 |
|------|--------|------|------|
| 2026-09-30 | oracle3 | 待评审 | — |
-->
