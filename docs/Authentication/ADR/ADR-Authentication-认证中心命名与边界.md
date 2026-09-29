# ADR-Authentication-认证中心命名与边界

## 状态

活跃

## 一、目的与目标

确立认证中心扩展的**命名、包形态与职责边界**，使认证中心（`TKWF.Ext.Authentication`）与既有 `TKWF.Ext.Identity`（用户/角色管理）/ `TKWF.Ext.Account`（锁定/重置）/ `TKWF.Ext.SecurityLog`（安全事件）/ `TKWF.Ext.RateLimiting`（Web 限流）形成**互补而非重叠**的扩展矩阵，消费方按需叠加启用。读者应在 3 句话内明白：认证中心 = 令牌签发/验证 + 认证方式矩阵 + 登录保护 + 身份适配层 + 跨系统映射 + 平台凭证；用户中心（档案面）**独立立项**（`TKWF.Ext.UserCenter`，另行立项）；命名规避既有 Identity 撞名。

## 二、问题

### 问题现象

- 抽取需求合并文档（v1.0，§四）建议模块划分含"**Identity**（身份适配层——`JwtDomainUserParser`/`ITokenVerifier`/`IAuthorizationMapper`）"，但本仓库**已有 `TKWF.Ext.Identity`**（V0.3.3，用户/角色管理 + PasswordHasher + `IdentityAuthService`，已发布 nuget.org）——命名**直接冲突**：若新扩展叫 Identity，两个包同名 `TKWF.Ext.Identity` 无法共存。
- 认证中心能力（TokenService/AuthLoginAttempt/AuthRefreshToken/平台凭证/微信客户端/身份适配层）在本仓库**零覆盖**（grep 全仓库零匹配），但职责与既有 Identity/Account/SecurityLog/RateLimiting 存在**表面重叠**（都有"登录/认证"字眼）——若无边界裁定，消费方会误用/重复建设。
- DMP-Lite 的账号模型（PlatformAdmin/MerchantUserInfo/MemberUser 多态）与既有 Identity 的 `UserEntity`（用户名+角色）**模型根本不同**（手机号主键 + 微信绑定 vs 用户名 + PasswordHasher），强行复用 Identity 会耦合两套不兼容模型。

### 触发场景

- 业务系统要接入统一认证（令牌签发/微信便捷登录/短信登录/票据换令牌）——找哪个扩展？
- 业务系统已启用 `TKWF.Ext.Identity`（用户/角色管理）又要启用认证中心——两者是否冲突？
- DMP-Lite / 教育线装配认证中心——账号模型用谁的？

### 现有方案不足

- 需求文档建议的"Identity 身份适配层"命名在 TKWF 生态**不可用**（撞名）；直接照搬会产生两个 `TKWF.Ext.Identity`。
- 若让认证中心复用 Identity 的 `UserEntity`：认证中心账号（手机号主键 + 微信 openid + unionid + teacher_verified + auth_level）与 Identity 用户（用户名 + 密码 + 角色）模型不兼容，强行复用导致字段残缺/语义错位。

## 三、使用场景

### 适用场景

- 多业务线（生活服务电商 / 教育工具系列 / 并行业务系统）独立装配认证中心实例，消费统一 JWT 令牌。
- 消费方同时启用 `TKWF.Ext.Identity`（本地用户/角色管理）+ `TKWF.Ext.Authentication`（统一认证/令牌）——两者并存、互不干扰。
- 认证中心实例装配（DMP-Lite / 教育线 auth.loongba.cn）：TokenService + 认证矩阵 + 登录保护全量启用。

### 不适用边界

- **用户中心（档案面）**：公共 Profile API + 兑换历史 + 我的应用 + 用户中心页面——**不属于本扩展**（**独立立项 `TKWF.Ext.UserCenter`**，用户裁定 2026-09-30，另行立项）。
- 口令兑换体系 / 教师审核流 / 家庭码：教育线特有，装配层/应用层，不进通用内核。
- 具体认证 Provider 实现（腾讯云短信 / 抖音 OAuth）：装配层实现 `ISmsSender` / Provider，Ext 只抽象。

## 四、选项

### 选项 A：`TKWF.Ext.Authentication`（单包，认证面 + 身份适配层 + 映射）

- 描述：一个包承载认证中心全量——令牌体系/认证矩阵/登录保护/票据换令牌/身份适配层（JwtDomainUserParser/ITokenVerifier/IAuthorizationMapper/UserHelperBase/中间件）/PlatformAccountMap/平台凭证。
- 优点：命名语义准确（认证中心 = 认证）；规避既有 Identity 撞名；消费方引一包即得完整认证能力；对标清单"身份认证服务"条目同向。
- 缺点：包较大（7 实体 + 10+ 服务）；身份适配层契约随主包发布（未来其他扩展要消费契约需再拆 Abstractions）。

### 选项 B：`TKWF.Ext.Identity`（沿用需求文档建议名）

- 描述：认证中心身份适配层沿用"Identity"命名（Oracle I1 是教育线方案语境）。
- 优点：与教育线方案文档一致。
- 缺点：**与既有 `TKWF.Ext.Identity`（V0.3.3）命名空间/包名完全冲突**——不可行；除非合并既有 Identity（破坏 V0.3.3 消费方，且模型不兼容）。

### 选项 C：拆两包（`TKWF.Ext.Authentication` 认证面 + `TKWF.Ext.Identity` 适配层——第二个包改名）

- 描述：认证面与身份适配层分拆（如 `TKWF.Ext.Authentication` + `TKWF.Ext.TokenValidation`）。
- 优点：消费方按需引包（业务系统只引适配层，认证中心实例引全量）。
- 缺点：两包共享同一令牌契约（签发/验证同密钥体系）拆开增加契约包依赖复杂度；身份适配层很小（4 组件）单独成包收益低；需求文档也把四模块归一个 TKWF.Extension 组件。

## 五、决策

选定：**选项 A——`TKWF.Ext.Authentication`（单包）**。

理由：
1. **命名可行性**：`TKWF.Ext.Authentication` 与既有 33 个扩展包名零冲突；语义准确（认证中心 = 认证面 + 身份适配层 + 映射）。
2. **模型独立性**：认证中心自建 `AuthAccountEntity`（手机号主键 + 微信绑定 + 认证声明），与 Identity `UserEntity`（用户名 + 角色）模型不同——不复用、不合并，两扩展共存（边界裁定 #3/#4）。
3. **单包内聚**：签发/验证/刷新/撤销共享同一密钥体系与令牌契约，拆包引入契约依赖无收益；消费方（业务系统）引一包即得「令牌验证 + 身份适配 + 角色映射」完整链路。
4. **DMP 语义对齐**：`ITokenVerifier` 替代 DMP 无实现的 `ISessionExchangeService`（主框架探索确认该接口不存在）；`PlatformAccountMap` 统一 DMP 双机制。

## 六、后果

### 正面影响

- 命名清晰、零撞名；与既有 Identity/Account/SecurityLog/RateLimiting 边界明确（互补不重叠，可叠加启用）。
- 消费方接入路径简单：引 `TKWF.Ext.Authentication` + 白名单声明 + 实现 `IAuthorizationMapper` + 装配 `ISmsSender` → 完成。
- DMP-Lite 迁移路径清晰（§九）：替换 TokenService/AuthLoginAttempt/AuthRefreshToken/GlobalUserMap/PlatformCredential/WeChatApiClient，端点重写消费 Ext Service。

### 负面影响 / 风险

- 包体较大（7 实体 + 10+ 服务）——消费方引包含未启用组件（Provider 按 `EnabledAuthTypes` fail-closed 不接线，实体表仅启用时建——需实现期确认 SyncTables 粒度）。
- 未来用户中心（`TKWF.Ext.UserCenter` 独立立项）若需消费认证契约（如查 PlatformAccountMap），可能需按 ADR48 D7 拆 `.Abstractions` 契约包——届时评审。
- "认证中心"与"用户中心"两扩展名在文档语境中易混（Authentication vs UserCenter）——使用指南明确命名。

### 后续待办

- 使用指南含「与既有 Identity/Account/SecurityLog/RateLimiting 划界」章节。
- DMP-Lite 迁移完成后更新本 ADR 关联文档。

## 七、关联文档

- 扩展开发方案版本：`docs/Authentication/v0.1.0-Authentication-认证中心-开发方案.md`（v0.1.0，待 Oracle 评审）
- 扩展审核报告版本：待评审后生成

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-09-30 | 初始版本——命名 `TKWF.Ext.Authentication` 单包；与既有 Identity 划界（不复用 UserEntity、自建 AuthAccountEntity）；用户中心**独立立项（`TKWF.Ext.UserCenter`）**；ITokenVerifier 替代 DMP ISessionExchangeService |
| 2026-09-30 | **用户中心单独立项裁定（用户确认命名）**——档案面/用户中心页面不再归认证中心 v0.2.0，独立扩展 `TKWF.Ext.UserCenter`（命名已确认，另行立项）；本文档 L9/L40/L85/L86 表述同步 |
