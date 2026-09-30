# TKWF.Ext.UserCenter 用户中心扩展技术规范

**状态**: 通用档案面扩展（用户中心——从认证中心 v0.2.0 拆分独立立项） | **版本**: V0.1.0 | **框架**: .NET 10

**核心约束**: 契约归中立 `UserCenter.Abstractions`（数据属主扩展实现）、领域逻辑（脱敏/降级/聚合）进主包门面、**零实体零存储**（聚合读取层）、数据访问红线合规（零 ORM/零 IEntityDAC——接线型不适用边界）、仅本人防护（门面显式 userId 参数，装配层强制）

---

## 一、需求分析 (Demand Analysis)

企业应用普遍需要"用户中心档案面"——公共 Profile API + 用户中心页面数据（兑换历史/我的应用）。TKWF 扩展机制核心优势是"**扩展内自定义数据结构/对象 → 装配进消费方 DI/DomainHost**"——本扩展将这套机制用到极致：

- **契约与实现分离**：档案数据属主 = 认证中心（AuthAccount）；兑换/授权数据属主 = 授权面（未立项）——本扩展只出**读取契约**（`IUserProfileSource`/`IRedemptionHistorySource`/`IUserAppsSource`），数据属主扩展在**自己**的 Initializer 内实现契约并 `TryAddScoped` 注册，消费方零手写桥接。
- **领域逻辑进扩展**：门面（组合三 Source）+ 强制脱敏（PhoneMasker）+ 降级矩阵——单一真相源，实现方不需要也不得自行处理（防漏防重）。
- **多业务线复用**：教育工具系列 + 生活服务电商 + 并行业务系统——装配复用同一用户中心骨架（需求来源：`LoongEduTools/docs/教育工具/` 认证中心与用户中心抽取需求 v1.0）。

**消费场景**：`/api/edu/user/me|redemptions|apps` 端点（装配实例映射 + 从 DomainUser 解析 userId）；公共档案（昵称/头像/手机号脱敏/微信绑定/认证声明）。

**依赖倒置（ADR48 D7 第 6 次落地）**：主包通过 Abstractions 契约消费——实现方（认证中心/授权面）引 **Abstractions**（不引主包），零扩展间实现依赖（TKWF0022 零违规）。

---

## 二、设计原理 (Design Principles)

### 1. 分层结构

- **契约包 `TKWF.Ext.UserCenter.Abstractions`**（零实现、零框架依赖——纯 BCL）：三 Source 接口 + 门面接口 + 三 DTO（`UserProfileDto`/`RedemptionRecordDto`/`UserAppDto`）。**命名空间保持 `TKWF.Ext.UserCenter`**（对齐 Emailing.Abstractions 先例）。
- **主包 `TKWF.Ext.UserCenter`**（门面 + 领域逻辑）：`UserCenterQueryService`（组合 Source + 强制脱敏 + 降级）+ `PhoneMasker` + 三模板基类（`UserProfileSourceBase`/`RedemptionHistorySourceBase`/`UserAppsSourceBase`——过渡期装配最小适配）+ `UserCenterOptions` + `UserCenterExtensionInitializer`。
- **存储边界**：两包均**零实体/零表/零 ORM**——聚合读取层（对齐 Dashboard 零存储先例）；档案/兑换/应用数据归各自属主扩展。

### 2. 关键设计

- **脱敏责任不对称（P1）**：手机号脱敏归**门面**（AuthAccount 明文存储，Source 返回原始值，门面统一 Mask——单一真相源防漏）；兑换 code 脱敏归**源实现方**（`CodeMasked` 契约约束，授权面已脱敏存储，门面透传）。
- **降级矩阵（§5.3）**：Source 未注册 → null/空列表（不抛异常）；Source 抛异常 → null/空列表 + `ILogger.Warning`（异常静默对齐仓库惯例）——不阻塞用户中心页面渲染。
- **模板基类过渡（§5.5）**：装配实例继承 `UserProfileSourceBase` 仅实现查询钩子（`QueryRawProfileAsync`）——映射/降级/日志管线预实现；终态（认证中心 v0.2.0+）扩展内直接实现白地接口，装配实例删除桥接类零代码变更。
- **仅本人防护（需求 L385）**：门面显式 `userId` 参数——装配端点从 DomainUser/令牌解析 userId，**不信任客户端传参**（门面不做用户上下文隐式推断，防隐式 IDOR）。

### 3. 与 ABP 对比（机制优势）

ABP Profile 模块（AbpAccount `IProfileAppService`）是"模块自带实体+服务、消费方引模块即用"；TKWF 更彻底——**契约+实现分离、数据归属主、领域逻辑入扩展、三钩子自动装配**：消费方仅白名单声明 `[TKWFEnabledExtension]`，DI 自动装配（门面 + 各属主 Source 实现），零桥接。

---

## 三、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IUserProfileSource`** | 公共档案源契约（数据属主扩展/装配实例实现） | 认证中心 v0.2.0+ / 装配实例（`UserProfileSourceBase`） |
| **`IRedemptionHistorySource`** | 兑换历史源契约（授权面/装配实例实现） | 授权面（未立项）/ 装配实例 |
| **`IUserAppsSource`** | 我的应用源契约（授权面/装配实例实现） | 授权面（未立项）/ 装配实例 |
| **`IUserCenterQueryService`** | 门面契约（消费方注入入口） | `UserCenterQueryService`（Scoped，本扩展） |
| **`UserCenterQueryService`** | 门面实现——组合三 Source + 强制脱敏 + 降级矩阵 | 本扩展 |
| **`PhoneMasker`** | 手机号脱敏（前 3 后 4 / 长度适配 / null·空安全） | 静态类（领域逻辑进扩展） |
| **`UserProfileSourceBase`** | 过渡期模板基类（档案）——装配实例最小适配 | 本扩展 |
| **`RedemptionHistorySourceBase`** | 过渡期模板基类（兑换）——同型 | 本扩展 |
| **`UserAppsSourceBase`** | 过渡期模板基类（应用）——同型 | 本扩展 |
| **`UserCenterOptions`** | 配置（`TKWF:UserCenter` 节：PhoneMask 可配） | 内置 |
| **`UserCenterExtensionInitializer<T>`** | 扩展初始化器（`[TKWFExtension]` 三钩子 DI 接线） | 本扩展 |

**DTO**（Abstractions）：`UserProfileDto`（UserId/Phone⚠️原始/IsWechatBound/Nickname/AvatarUrl/IsTeacherVerified/AuthLevel）；`RedemptionRecordDto`（CodeMasked 已脱敏/ProductName/TargetAppId/RedeemedAtUtc/Status）；`UserAppDto`（AppId/AppName/IsAuthorized/ExpiresAtUtc/UsageSummary 不含学习明细）。

---

## 四、依赖关系

```
TKWF.Ext.UserCenter（主包——纯 Domain，零实体零存储）
├── TKWF.Ext.UserCenter.Abstractions（契约——ADR48 D7 第 6 次落地）
└── 主框架 Domain（ExtensionInitializer 基座）

TKWF.Ext.UserCenter.Abstractions（契约包）
└── 零框架引用（纯 BCL——对齐 Emailing.Abstractions / Account.Abstractions 先例）

实现方（终态）：
  认证中心 v0.2.0+ ──▶ IUserProfileSource（经 Abstractions，TryAddScoped 注册）
  授权面（未立项） ──▶ IRedemptionHistorySource / IUserAppsSource（经 Abstractions）
  装配实例（过渡期）──▶ 继承 UserProfileSourceBase 等模板基类
```

**L2 门控**：主包引 Abstractions（✅）；实现方引 Abstractions（✅）——零 TKWF0022。

---

## 五、装配与使用 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.UserCenter` 包，扩展经 `[TKWFExtension]` 被 SG1 发现；**V4.9.85 起发现不自动启用**——消费方须在领域初始化器上声明 `[TKWFEnabledExtension]` 白名单：

```csharp
using TKWF.Ext.UserCenter;

[TKWFEnabledExtension(typeof(UserCenterExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

自动注册：`IUserCenterQueryService`（默认 `UserCenterQueryService`）。**Source 接口不注册默认实现**——由数据属主扩展或装配实例提供。

### 2. 过渡期装配示例（认证中心 v0.1.0 完成后——认证中心桥接）

```csharp
// 装配实例：继承 UserProfileSourceBase 仅实现查询钩子（映射/降级/日志管线已预实现）
public sealed class AuthAccountProfileSource : UserProfileSourceBase
{
    private readonly IAuthAccountQueryService _accounts;   // 认证中心查询服务（装配实例层可用）
    public AuthAccountProfileSource(IAuthAccountQueryService accounts, ILogger<UserProfileSourceBase> logger) : base(logger)
        => _accounts = accounts;

    protected override async Task<RawProfile?> QueryRawProfileAsync(string userId, CancellationToken ct)
    {
        var acct = await _accounts.GetByUidAsync(userId, ct);   // 查 AuthAccountEntity
        return acct is null ? null : new RawProfile(acct.Phone, acct.HasWechat, acct.Nickname, acct.Avatar, acct.TeacherVerified, acct.AuthLevel);
    }
}

// DomainHostConfigureServices：
services.AddScoped<IUserProfileSource, AuthAccountProfileSource>();
```

### 3. 使用（消费方注入门面）

```csharp
public class UserCenterPage(IUserCenterQueryService userCenter)
{
    public async Task<UserProfileDto?> MeAsync(string currentUserId)   // userId 从 DomainUser/令牌解析——仅本人
        => await userCenter.GetProfileAsync(currentUserId);
}
```

### 4. 终态演进（认证中心/授权面实现契约后）

| 阶段 | IUserProfileSource 实现方 | 装配实例工作 |
|------|--------------------------|-------------|
| **v0.1.0（认证中心未就绪）** | 装配实例继承 `UserProfileSourceBase`（或白地实现） | 1 个桥接类（几行查询钩子）+ 1 行 DI 注册 |
| **认证中心 v0.2.0+（终态）** | 认证中心扩展内直接实现契约 + TryAddScoped | **删除过渡桥接类，零代码变更** |

---

## 六、配置 (Configuration)

`UserCenterOptions` 绑定 `TKWF:UserCenter` 配置节（`AddOptions` 默认值兜底）：

```json
{
  "TKWF": {
    "UserCenter": {
      "PhoneMaskHead": 3,
      "PhoneMaskTail": 4
    }
  }
}
```

| 属性 | 默认 | 说明 |
|------|------|------|
| **`PhoneMaskHead`** | 3 | 手机号脱敏保留位数（头）——缺省内置规则 |
| **`PhoneMaskTail`** | 4 | 手机号脱敏保留位数（尾）——缺省内置规则 |

---

## 七、边界 (Boundaries)

### 包含

- ✅ 公共档案查询（`GetProfileAsync`——Phone 强制脱敏）+ 手机号脱敏矩阵
- ✅ 用户中心 API 骨架（兑换历史 `GetRedemptionsAsync` / 我的应用 `GetAppsAsync`——缺省降级）
- ✅ 契约包（三 Source + 门面 + 三 DTO——实现方中立引用）
- ✅ 过渡期模板基类（装配最小适配）+ 终态演进路径
- ✅ 降级矩阵（未注册/异常 → null/空列表）+ 异常静默

### 不包含

- ❌ 口令兑换体系（code/redemption/grant/batch + 管理端）——**授权面扩展**（未立项）；本扩展仅经 `IRedemptionHistorySource` 读取
- ❌ 授权计算/授权快照（grants/verify）——授权面（本扩展经 `IUserAppsSource` 读取聚合结果）
- ❌ 用户中心页面渲染（HTML 登录态页面）——**装配实例**（对齐 Notifications.SignalR「不含前端 UI」/ Dashboard「UI 渲染归消费方」先例）
- ❌ 账号写操作（改昵称/头像/密码）——认证中心（写归数据属主）；本扩展 v0.1.0 只读
- ❌ 档案偏好持久化（`UserCenterPreferenceEntity`）——待写需求出现（YAGNI）
- ❌ 业务档案（任课班级/孩子信息/家庭码/学习进度/购买明细）——各业务系统本地（立项方案 §4.1 不适合集中）

---

## 八、安全契约 (Security Contract)

| 项 | 约束 | 归属 |
|----|------|------|
| **IDOR 越权** | 门面显式 `userId` 参数 + 契约文档声明「仅本人令牌可读」（需求 L385）——装配端点从 DomainUser/令牌解析 userId，不信任客户端传参 | 装配实例（端点层强制）+ UserCenter（契约文档约束） |
| **手机号泄露** | 门面输出强制脱敏（PhoneMasker）；源/门面不得将含原始 Phone 的 DTO/异常栈写日志或序列化（C1） | UserCenter（门面）+ 源实现方 |
| **兑换 code 明文** | `RedemptionRecordDto.CodeMasked` 契约约束（实现方输出前脱敏，授权面已脱敏存储） | Source 实现方 |
| **儿童数据红线** | `UserAppDto.UsageSummary` 契约约束「不含学习明细」 | Source 实现方（授权面） |
| **Source 异常吞业务** | 降级（null/空列表）+ Warning（异常静默惯例） | UserCenter |

---

## 九、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 契约包（三 Source + 门面 + 三 DTO）+ 主包（门面/脱敏/模板基类/Options/Initializer）
- 零实体零存储（聚合读取层）+ 数据访问红线合规（接线型不适用边界）
- 降级矩阵 + 脱敏矩阵测试 + 模板基类装配测试
- 双包 slnx 接线 + L2 门控零 TKWF0022

### V0.2.0+（规划——最优解探索终态路径，见 `docs/框架实战教学/01-扩展模块最优解探索-Schema级数据组合-开篇.md` §八）
- **组合视图 `vm_UserCenterProfile`**（首个跨扩展组合 VEntity 先例）：JOIN 认证中心基表（`AuthAccount` 等）——装配层单查询组装档案 + 项目定制字段；SQL 硬脱敏（投影下沉）直接作用于基表；`IGlobalQueryFilter` 按 UserId 过滤（仅本人，框架 F6 候选 / 方案 A）或 `ExposeGraphqlQuery=false` 经门面（方案 B）
- 认证中心 v0.2.0+ 实现 `IUserProfileSource`（数据属主扩展内实现——删除装配实例桥接类）
- 授权面立项后实现 `IRedemptionHistorySource`/`IUserAppsSource`
- 档案偏好持久化（写需求出现时按红线标准路径补充）

### 远期 / 评估
- 个人信息编辑（改昵称/头像/密码）——随认证中心写能力
- 用户中心页面渲染（装配实例示例增强）

---

**文档信息**: V0.1.0 | 2026-09-30 | 关联：[v0.1.0-UserCenter-用户中心-开发方案](../../docs/UserCenter/v0.1.0-UserCenter-用户中心-开发方案.md)（Oracle 评审 PASS + Oracle3 复审 PASS WITH CONDITIONS）、[用户中心使用指南](../../docs/UserCenter/用户中心扩展-使用指南.md)、框架实战教学开篇（最优解探索方法论）