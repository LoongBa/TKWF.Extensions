# UserCenter 退役收尾 开发方案

> **扩展**: `TKWF.Ext.UserCenter`（用户中心）+ `TKWF.Ext.UserCenter.Abstractions`（契约包）｜**动作**: **完整删除**（代码/契约/测试/文档）
> **版本**: 收尾迭代（2026-10-08）｜**日期**: 2026-10-08
> **状态**: 📋 待评审（Oracle）
> **定位**: UserCenter 独立扩展的**终章方案**——记录"为何删除"的架构判据 + "如何收尾"的完整执行（DTO 归属 / 消费方适配 / 影响面 / 验收）。
> **关联**: ADR-AuthCenter-身份域数据模型与密码能力边界（C.14）· 变更记录（2026-10-08 退役落地条目）· 转达 20261008-01 · 授权面前置裁定（P2 FREEZE）· ABP 对照（无独立用户中心聚合层模块）

---

## 一、背景与动机（为什么删除——架构判据）

### 1.1 立项目标回顾

UserCenter（2026-09-30 独立立项）：**用户中心页面的数据聚合层**——组合公共档案 / 兑换历史 / 我的应用，作为 `auth.loongba.cn/user` 页面服务端数据面，跨业务线装配复用。形态：契约包（三 Source + 门面 + 三 DTO）+ 主包（门面/脱敏/模板基类，零实体零存储）。

### 1.2 三个立项目标假设均已证伪

| # | 立项假设 | 现状 | 结论 |
|---|---------|------|------|
| H1 | 聚合读取层是独立领域价值 | 档案被 AuthCenter 天然供给（`UserProfile` 1:1 表 + `GetProfileByUIdAsync` 门面）；兑换/应用被 AuthSurface 天然供给（VEntity + 自有门面）——UserCenter 门面只转发这三个**已有门面**的输出 | ❌ **伪层**：不产生任何独立领域规则（脱敏已下沉属主/装配层、降级由各门面承载、IDOR 防护在装配层）——**聚合透传与属主门面完全重叠 = 红线"禁 Store 伪 DataService 层"的扩展级翻版** |
| H2 | 跨业务线通用"用户中心页面"值得独立承载 | 页面 = HTTP/UI 渲染（AGENTS §8 表现层归装配层）；数据面各自有主，页面只组合三个门面 | ❌ **假设错位**：把"表现层组装"误当"领域能力"——需要的是装配层模板，不是第四个门面扩展 |
| H3 | 三 Source 契约抽象有信息增益 | P2 FREEZE 后授权面公开声明不实现 Source（自建 VEntity + 门面）；档案 Source 唯一实现者已删——契约 = 无实现方空壳 | ❌ **契约抽象失败实证**：数据属主各有门面 + DTO，Source 契约是二次包装，零信息增益 |

### 1.3 架构判据（明确——非"无消费方"论据）

**判据不是"无消费方"，而是：存在独立领域规则才配拥有扩展。**

"用户中心类似物"**需要**的正面条件（同时满足）：
1. 存在**跨属主**数据面（≥2 个扩展的数据需统一领域规则组合）；
2. 该组合有**独立领域规则**可承载（如统一脱敏策略 / 统一降级语义 / 统一 IDOR 边界）——**非透传**。

对照 TKWF 现状：脱敏策略在装配层（表现面）、降级在各属主门面（属主面）、IDOR 在端点（装配面）——**组合无独立领域规则可承载** → 条件 2 不满足 → **UserCenter 无存在位置**。

> ABP 生态对照：无独立"用户中心聚合层"模块（档案内建 Identity/Account + 消费方定制扩展是标准做法）——收敛方向一致。

### 1.4 决策沿革

| 日期 | 决策 |
|------|------|
| 2026-10-07 | C.14 启动——`IUserProfileSource` 标 `[Obsolete]` + 保留实现，V1.0.0 移除（过渡期） |
| 2026-10-08 | **用户裁定升级为完整删除**：不留 `[Obsolete]`、**不兼容、不过渡**——通知消费方/项目适配即可 |

**本方案执行"完整删除"**：UserCenter 代码、契约、测试物理删除；DTO 资产归 AuthSurface；消费方适配通知。

---

## 二、目标与范围

### 2.1 目标

1. **UserCenter 独立扩展彻底消失**（代码/契约/测试/slnx/文档引用零残留）；
2. **DTO 资产平滑归属** AuthSurface（`RedemptionRecordDto`/`UserAppDto` 迁主包，类型签名不变）；
3. **消费方适配通知送达**（DMP-Lite 等：DTO 新位置 + 接口删除的替代通道）；
4. 决策作为**永久档案**保留（本方案 + 变更记录 + 关联 ADR——不随代码删除而消失）。

### 2.2 删除清单

| 对象 | 处置 |
|------|------|
| `_Framework/UserCenter/`（主包） | **物理删除** |
| `_Framework/UserCenter.Abstractions/`（契约包） | **物理删除**（DTO 已迁 AuthSurface，见 §三） |
| `_Tests/Extension.UserCenter.Tests/` | **物理删除**（27 用例的断言语义已由 AuthCenter 档案测试 + AuthSurface 门面测试覆盖） |
| `TKWF.Extensions.slnx` | 移除 2 项目 + 1 测试项目 |
| `docs/UserCenter/`（旧开发方案 v0.1.0 / 使用指南） | **删除**（避免混淆——历史时间线在变更记录保留） |
| **本收尾方案** | **保留**（终章决策档案，公开） |
| `README.md` 一览表 UserCenter 两行 | **删除**（此前"退役"标注 → 完整删除语义） |
| 主框架总览跟踪 UserCenter 行 | 删除/标记已删（历史保留） |

### 2.3 不删除（永久记录）

- **ADR-Authentication-UserCenter契约承接**：已标"已废弃"（ADR 生命周期——永久保留 + 引用替代）
- **ADR-AuthCenter-身份域数据模型与密码能力边界**（C.14 出处）
- **变更记录时间线**（2026-10-08 条目 + 历史各条目）
- **转达/回填记录**（20261008-01 §8 + 框架转达——消费方适配通知的凭据）

---

## 三、技术方案

### 3.1 DTO 归属（AuthSurface 主包）

**原则（ADR48 D7 契约归数据属主）**：兑换/应用 DTO 属主 = AuthSurface（其门面实际返回）→ 归 AuthSurface。

| DTO | 处置 |
|-----|------|
| `RedemptionRecordDto` / `UserAppDto` | **迁入 AuthSurface 主包**（`_Framework/AuthSurface/Contracts/AuthSurfaceDtos.cs`，命名空间 `TKWF.Ext.AuthSurface`，形状不变） |
| `UserProfileDto` | **废弃**（随 UserCenter 删除消失）——档案消费已定型为 AuthCenter `UserProfileEntity`（`GetProfileByUIdAsync` 返回）+ 装配层脱敏，无第三形状需求 |

**连带修改（AuthSurface）**：
- csproj：删 `UserCenter.Abstractions` ProjectReference（✅ 已执行）
- 3 服务（`RedemptionQueryService`/`UserAppsQueryService`/`RedemptionCommandService`）：删 `using TKWF.Ext.UserCenter;` + 注释更新（✅ 已执行）
- 测试 csproj：删 `UserCenter.Abstractions` ProjectReference
- README：依赖表删 ProjectReference 行、划界表 UserCenter 行改"已删除"、P2 FREEZE 表述更新
- 注释清理：`Views/UserRedemptionHistoryView.cs`（"UserCenter 契约 CodeMasked 透传"）等 4 文件 → 中性表述

### 3.2 其他扩展注释清理（注释级残留，非依赖）

`Settings/SettingManager.cs`、`Federation/*`（StaticChannelRegistry/DbChannelRegistry/ISsoProfileService）、`Notifications/NotificationsExtensionInitializer.cs`、`AuthCenter.Abstractions/ISsoAccountQueryService.cs`、`AuthCenter/`（IAuthAccountQueryService/AuthCenterExtensionInitializer 已改，复查残留）——UserCenter 字样改为中性表述或删除（grep 断言零命中）。

### 3.3 NuGet 处置

- `0.1.0` 两包已 unlist（✅ 2026-10-08，search 索引延迟刷新）
- **preview 版本线验证**（`0.1.1-preview.*` 是否仍 listed）→ 如有，一并 unlist（防新消费者误引退役包）
- 消费方已有 lock file 引用 `0.1.0`/preview 仍可 restore（unlist ≠ 删除）——适配窗口内不破坏既有构建

### 3.4 消费方适配通知（DMP-Lite 等）

| 原引用 | 适配后 |
|--------|--------|
| `RedemptionRecordDto` / `UserAppDto`（`TKWF.Ext.UserCenter` 命名空间） | **`TKWF.Ext.AuthSurface`**（引 AuthSurface 主包即得——类型签名不变，改 using/包引用） |
| `IRedemptionHistorySource` / `IUserAppsSource` | **删除**——兑换/应用查询改经 AuthSurface `IRedemptionQueryService` / `IUserAppsQueryService` |
| `IUserProfileSource` | **删除**——档案读经 AuthCenter `IAuthAccountQueryService.GetProfileByUIdAsync` |
| `IUserCenterQueryService` | **删除**——三数据面分别经 AuthCenter/AuthSurface 门面，页面在装配层组合 |

通知渠道：`20261008-01 §8` 回填 + 框架转达回执（状态：已删除/适配指引）。

### 3.5 主框架侧

`_TKWF/docs/03_扩展模块/总览和跟踪.md`：UserCenter 行删除（历史保留在变更记录）；开放待办已标废弃（✅ 已执行）。

---

## 四、影响面与风险

| 风险 | 影响 | 对策 |
|------|------|------|
| AuthSurface 未发布期间 DTO 迁移 | 无 ghost dep（DTO 已就地归主包，AuthSurface 零 UserCenter 依赖） | DTO 迁移先于 AuthSurface 首次发布（本次迭代完成） |
| 存量消费方引用断裂 | DMP-Lite 等引 UserCenter DTO/接口 | 适配通知（§3.4）+ 类型签名不变（仅包/using 变更）+ unlist 不禁 restore |
| P2 FREEZE 语义（"契约保留向后兼容"）曾承诺契约壳 | 与"完整删除"冲突 | 明确升级决策：**取消契约壳承诺**（无实现方契约无保留价值；装配层从未实现 Source——P2 FREEZE 本身即证明） |
| 文档残留混淆 | 检索/装配误用 | grep 断言零命中 + README/总览同步删行 |
| 过渡期 Obsolete 已补标 | 与"不留 Obsolete"冲突 | 完整删除后 Obsolete 随代码消失（补标仅服务过渡期构建警示，非终态） |

---

## 五、验收标准

| # | 验收条件 | 验收方式 |
|---|---------|---------|
| UC-1 | `UserCenter` 关键字在代码/引用零残留（grep 断言） | grep `_Framework`/`_Tests` `*.cs/*.csproj` 零命中（注释中性化后） |
| UC-2 | slnx 构建 0 错误 | `dotnet build TKWF.Extensions.slnx` |
| UC-3 | 全量测试全绿（AuthSurface 20 用例含 DTO 新归属断言；AuthCenter 138 档案测试） | `dotnet test` |
| UC-4 | AuthSurface 零 UserCenter 依赖（csproj 无 ProjectReference；门面 DTO 自持） | csproj 检查 + 构建 |
| UC-5 | 消费方适配通知已送达（转达 §8 / 回执记录） | 文档检查 |
| UC-6 | NuGet 0.1.0 + preview 全部 unlist | dotnet package search 验证 |
| UC-7 | 文档清理完成（README 一览表删行 / docs/UserCenter 旧文档删除 / 本方案保留） | 文档检查 |

---

## 六、实施步骤

```
T1  DTO 迁 AuthSurface 主包 + 3 服务/csproj 清理          ✅ 已执行（本方案载体）
T2  AuthSurface 测试 csproj + README + 注释清理（4 文件）   （本次迭代）
T3  删 UserCenter 主包/契约包/测试目录 + slnx 移除 3 项目    （本次迭代）
T4  其他扩展注释清理（Settings/Federation/Notifications/AuthCenter）
T5  文档：README 一览表删行 / docs/UserCenter 旧文档删除 / 变更记录追加收尾条目
T6  主框架总览删 UserCenter 行
T7  NuGet preview 版本验证 + unlist
T8  转达回填（删除决策 + 消费方适配通知）
T9  验收（grep 零命中 + slnx 0 错误 + 全量测试全绿）
```

---

## 七、变更记录

| 日期 | 版本 | 变更内容 | 关联 |
|------|------|---------|------|
| 2026-10-08 | 收尾迭代 | 初始版本——UserCenter 完整删除方案：架构判据（聚合层反模式——非"无消费方"论据，判据=无独立领域规则可承载）+ 删除清单 + DTO 归 AuthSurface + 消费方适配 + 验收；用户裁定"不留 Obsolete、不兼容、不过渡" | ADR C.14（升级）+ 转达 20261008-01 |

---

<!-- EOF -->