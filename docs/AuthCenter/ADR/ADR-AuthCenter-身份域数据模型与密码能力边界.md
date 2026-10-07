# ADR-AuthCenter-身份域数据模型与密码能力边界

> **状态**：已批准（2026-10-07 设计讨论定案）
> **日期**：2026-10-07
> **关联**：ADR-AuthCenter-归层与命名 · ADR-Authentication-UserCenter契约承接 · ADR-Authentication-账号写契约 · ADR-Authentication-认证中心命名与边界（已废弃，历史裁定保留）· ADR-MFA-独立扩展与零依赖边界 · `ADR-RateLimiting-点检查原语与三层限流分工`（频控关联）· Oracle 中立审视（本会话 oracle8/oracle3/oracle7）
> **性质**：数据模型 + 密码能力边界裁定——多轮设计讨论（认证中心 vs 用户中心 vs Identity 三方关系）最终定案

---

## 一、目的与目标

裁定认证中心（AuthCenter）的**身份域数据模型**与**密码能力边界**：确立"凭据/档案表级分离 + 双角色字段 + 密码生命周期归属 + 二级认证可复用"的最终设计，为 AuthAccount/UserProfile 两表重构、密码三件套（验证/修改/找回）、找回多通道、MFA 可选强化提供权威依据。目标状态：AuthCenter 是身份锚 + 凭据 + 最小通用档案的属主，密码能力在域内闭环，MFA 独立可选，业务层经 VEntity 自由扩展。

## 二、问题

### 问题现象
1. **`AuthAccountEntity` 是"四关切合一"的神实体**：身份锚（UId/Phone）+ 凭据（PasswordHash/联邦 Id）+ 档案（Nickname/Avatar/TeacherVerified/AuthLevel）+ 安全态（TokenVersion/IsEnabled）混居一表——档案变更触碰认证关键表（锁竞争/审计噪音/迁移风险）。
2. **凭据与档案读/写劈裂**：UserCenter 独立立项为零实体零存储聚合读取层，档案数据属主却是 AuthCenter——"读门面拆走、数据留在原处"的错缝（oracle3 诊断：提取过早）。
3. **密码能力归属未定**：`AuthAccount.PasswordHash` 列预留但无密码 Provider（`AuthTypes.Password` 扩展点未实现）、无 SetPassword/修改/找回；Account 扩展已有按 `userName` 的 `IPasswordResetFlow`（主框架契约）+ `IAccountPasswordManager`（写 IdentityUser）——与 AuthAccount 手机号主键模型**结构性不兼容**。
4. **双凭据存储危害**：AuthAccount.PasswordHash vs IdentityUser.PasswordHash 两套，无桥接无迁移（oracle8 警示双 hash 安全维护危害）。
5. **业务化内容漏出认证域**：`TeacherVerified`（教育线核实）被消费方直接映射业务角色（`IdentityAdapterTests` L114）；`AuthLevel` 枚举 3=教师核实混入认证强度。

### 触发场景
- AuthCenter 新增密码登录 Provider、修改密码、找回密码（短信/邮件/扫码多通道）；
- 业务层扩展用户档案（头像/生日/性别/联系方式）——经 VEntity 跨扩展 JOIN，不受跨表/跨程序集限制；
- 消费方多实例部署需要 MFA 二次验证（改密/找回高风险操作）；
- 联邦账号（FederationAnchorOpenId）需绑定本地 AuthAccount。

### 现有方案的不足
- 维持 AuthAccount 单表 = 神实体持续膨胀（新档案字段逐个塞入）；
- 维持 UserCenter 零存储聚合层 = 1-属主契约间接层不值得（Rule of Three）；
- 内置密码找回复用 Account `IPasswordResetFlow` = 按 userName 写 IdentityUser，与手机号模型错配（第二套落库路径）；
- 扫码进验证码通道 = 混淆"通道可达证明"与"身份持有证明"层级。

## 三、使用场景

### 适用场景
- AuthCenter V0.9.0+ 身份域重构（AuthAccount 瘦身 + UserProfile 提取）；
- 密码生命周期：验证（PasswordAuthenticationProvider）/ 修改（SetPasswordAsync）/ 找回（SMS/Email/扫码多通道）；
- 高风险操作二级认证（MFA 可选强化）；
- 业务层档案扩展（VEntity 自由扩展用户数据）。

### 不适用边界
- 业务角色/权限（令牌只答"你是谁"，`IAuthorizationMapper` 本地映射）；
- 组织（OrganizationUnit 独立扩展）、教师核实（教育线业务扩展）；
- Identity 的 userName 模型密码流程（Account `IPasswordResetFlow` 服务 Identity，不用于 AuthAccount）；
- MFA 本体（独立扩展，零依赖边界 ADR 保留）；
- 档案写能力（UserCenter 只读，写归数据属主 AuthCenter）。

## 四、裁定内容（11 条 + 密码能力 + 二级认证）

### A. 身份域数据模型

1. **凭据/档案表级分离**：`AuthAccount`（凭据核心：UId/Phone[凭据角色]/PasswordHash/联邦 Id[经 PlatformAccountMap]/TokenVersion/IsEnabled——零档案字段）+ `UserProfile`（基础档案 1:1：Nickname/Avatar/Birthday/Gender[宽松]/Email[联系方式角色]——照顾大多数，业务可用可自建）。**`UserProfile` 采用 `[DomainGenerateCode]`（SG1 生成实体 + DataService，同 AuthAccount——有持久化+查询需求，AGENTS §8 判据）**。表级分离（非扩展级分离）修复神实体。
2. **AuthAccount 列冻结白名单 + 准入标准**：新列仅当"认证时采集 ∩ 每个部署每个消费方普遍需要"（防倾倒场滑坡）；业务域字段走业务扩展表 + VEntity。
3. **AuthLevel 泛化**：1=手机号级 / 2=**联邦快捷认证**（微信/QQ/支付宝扫码、OIDC 联邦登录——不写死平台）；平台差异归 `AuthType`/`ChannelId`；剔除业务档 3（教师核实）。
4. **TeacherVerified 迁出** → 教育线业务扩展（自建声明/表）；令牌不再携带（防角色漏出）。
5. **Phone 双角色**：凭据角色归 AuthAccount（登录锚点）；联系方式角色**归业务层**——框架不提供"复制登录号"默认操作，业务自决重新填/绑或显式复制；框架只提供 PhoneMasker 脱敏 + DTO 裁剪。
6. **Email 可选凭据扩展点**：默认不启用（fail-closed，`EnabledAuthTypes` 由消费方决定）；支持"联系方式→凭据"角色升级（迁移至 AuthAccount 不破坏档案面）；Email 找回通道与 Email 登录凭据是两件事，可独立启用。
7. **Gender 宽松**：自由文本（可空 max 32），无枚举约束——框架不枚举业务分类（"108 种性别"场景）。
8. **联邦 Id 存储归一化**：全部平台联邦 Id 进 `PlatformAccountMapEntity`（ChannelId+ExternalUserId 复合唯一，N2 模型）；AuthAccount 仅留 `FederationAnchorOpenId`（N2 锚点冗余快查列）；WechatMp/WechatWeb/UnionId 三列一次性迁移（重大版本）。

### B. 密码能力边界

9. **验证 + 修改归 AuthCenter**（凭据数据属主职责）：`PasswordAuthenticationProvider`（扩展点已预留，实现）+ `SetPasswordAsync(uid, ...)` 字段级写接口（替代裸 UpdateAsync 整实体）+ TokenVersion++ 联动（旧 refresh 失效，2h 内旧 access 有效属预期——业界主流）。
10. **找回密码多通道（AuthCenter 自建链路，Phone 模型）**：SMS（`SmsScenes.Reset` 已存在——**端点白名单已含，零找回流程消费**，承接）/ Email（`Emailing.Abstractions` 投递，消费契约包）/ 扫码（**已绑定身份证明前置条件**——改密前证明持有微信，走 OAuthTicket 链路，**不进验证码通道**）。**不实现 `IAccountPasswordManager` 第二实现**（该契约按 userName 设计，AuthCenter 自建面向 UId 的密码服务，平行不互认，与两套账号模型边界一致）。
11. **找回投递层缺口**：`DefaultPasswordResetFlow.InitiateResetAsync` 生成码后不投递（Account 侧缺口）——AuthCenter 自建链路自带投递（SmsScenes.Reset 现成 + Emailing 可空解析降级）。

### C. 二级安全认证 + 关联

12. **MFA 保持独立扩展**（Oracle 中立审视：操作无关验证原语，非认证域能力）；改密/找回挂 MFA = **可选强化**（消费方编排，`ITkExtensionContainer.Find("MFA")?.IsEnabled` 查询启用态，零框架增强）。频控共享/票据无操作/恢复码循环三点由编排层处理。
13. **扫码 = 前置条件（身份持有证明）**，层级区分：CAPTCHA（反自动化）< SMS/Email 验证码（通道可达）< 扫码/MFA（身份持有）。多层组合（扫码+SMS+MFA），一次性+TTL+state 绑定（OAuthTicket 模式），不单层作唯一证明。
14. **UserCenter 处置**：零存储聚合层**退役**（oracle3 裁定：VEntity 能力替代聚合层）；`IUserProfileSource` DEPRECATE（**V0.9.0 标 Obsolete + 保留实现，V1.0.0 移除**）、`IRedemptionHistorySource`/`IUserAppsSource` FREEZE、`IUserCenterQueryService` + PhoneMasker KEEP as optional utility、模板基类/降级矩阵 RETIRE。
15. **频控关联**：密码能力频控走 `IRateLimitCheck`（三层限流分工 ADR）；AuthCenter 登录保护（`AuthLoginAttempt`/`SmsRecord` 审计表 COUNT）保留双目的。
16. **表名前缀**：`TKWF_`（表）/`TKWFV_`（视图）/`TKWFIX_`（索引）——转达框架组核查（`转达-扩展表名别名与前缀机制全链路核查`），AuthAccount 改名 `TKWF_AuthAccount` 随前缀批次执行。

## 五、后果

### 正面影响
- 凭据/档案表级分离消除神实体（档案变更不再触碰认证关键表）；
- 密码生命周期域内闭环（验证/修改/找回 + 多通道），扩展点全部现成（PasswordHash 列/Provider 字段/SmsScenes.Reset）；
- MFA 可选强化不破零依赖边界（`ITkExtensionContainer` 现成）；
- 业务层 VEntity 自由扩展用户档案（框架优势最大化）。

### 负面影响与风险
- **存量迁移**：AuthAccount 表结构变更（删 3 联邦列/档案列拆 UserProfile）为破坏性变更——**破坏性变更预期随 V0.9.0（semver pre-1.0 minor 允许破坏，须消费方知晓）+ DBA 脚本**；或显式 major（V1.0.0）视实施批次裁定；
- **消费方迁移**：已知消费方 DMP-Lite/EduPlatform 须按迁移指引适配（表结构变更 + token contract 变更 + UserProfile 拆分）——具体步骤留开发方案/转达，本 ADR 仅登记事实；
- **双账号模型并存**（AuthAccount vs IdentityUser）无桥接——本裁定承认可共存（手机号 vs userName 场景分离），桥接表 `UserAccountLink` 为未来候选（oracle8 提议）；
- 密码 Provider 默认不启用（fail-closed）——消费方需显式 `EnabledAuthTypes` 装配（DMP 迁移时）。

### 后续待办
- AuthCenter V0.9.0+ 身份域重构（A.1-A.8）；
- 密码三件套 + 找回多通道实施（B.9-B.11）；
- UserCenter 退役（C.14：V0.9.0 标 Obsolete + 保留实现，V1.0.0 移除）；
- **`AuthAccountUserProfileSource` 更新**（V0.8.0 已实现读 AuthAccount——UserProfile 拆分后改读 UserProfile 表，实施批次内）；
- **token contract 迁移**（`teacher_verified` claim 移除——令牌不再携带业务声明，消费方解析侧同步）；
- `UserAccountLink` 桥接表（未来候选）；
- 授权面立项前置（见 `docs/授权面/` 或主框架跟踪）。

## 六、关联文档

- 本会话讨论记录（认证中心 vs 用户中心 vs Identity 三方关系，oracle8 中立审视 + oracle3 方案审视 + oracle7 频控复审）
- ADR-AuthCenter-归层与命名 / ADR-Authentication-UserCenter契约承接 / ADR-Authentication-账号写契约
- ADR-MFA-独立扩展与零依赖边界
- ADR-RateLimiting-点检查原语与三层限流分工
- `转达-扩展表名别名与前缀机制全链路核查-请框架组确认.md`

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-07 | 起草（整合本会话 11 条裁定 + 密码能力边界 + 二级认证 + 关联裁定） |

---
<!-- EOF -->
