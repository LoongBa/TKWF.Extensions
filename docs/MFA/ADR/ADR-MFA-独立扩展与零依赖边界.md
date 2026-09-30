# ADR-MFA-独立扩展与零依赖边界

> **版本**：V0.1.0（目标）
> **状态**：📋 提议（随 v0.1.0 开发方案，Oracle 评审 bg_025c18fc 定案）
> **关联**：`docs/MFA/MFA多因素认证-开发方案.md`（Oracle PASS WITH CONDITIONS，Q1/Q5 裁决）、对标清单 §2.2（`TKWF.Ext.MFA`）、Account v0.1.0 方案（「验证码渠道归消费方」裁定）、`ADR-扩展数据访问红线.md`、ADR48 D7（L2 门控）
> **关键字**：MFA、独立扩展、零依赖、消费方编排、IMfaSmsSender、Abstractions、IAccountLockoutPolicy

---

## 目的与目标

裁定 MFA 扩展的**归属与依赖边界**：独立包 `TKWF.Ext.MFA` vs 并入 Account vs 挂主框架新契约；以及消费 `ISmsVerificationService`/`ITokenService` 的路径（拆 Authentication.Abstractions vs 消费方抽象）。目标状态（3 句内）：**独立扩展 `TKWF.Ext.MFA`**（对标清单已定名），零主框架改动、零扩展间依赖（L2 门控零违规）；短信通道经**消费方抽象 `IMfaSmsSender`**（TryAdd 无默认——对齐 `ISmsSender` 先例与 Account「验证码渠道归消费方」裁定）；**MFA 不签令牌、不维护登录状态**——登录编排归消费方（主认证 → MFA 挑战验证 → 消费方签发），符合 TKWF 装配层驱动哲学。

---

## 问题

### 问题现象

- 主框架**零 MFA 契约**（勘察实证：全仓 grep `Mfa|TwoFactor|Totp|...` 零命中；`IAuthController.RequestChallengeAsync` 是密码传输加固非二因素；无 VerifyChallengeAsync/pending 状态/EnumLoginAuthType MFA 成员）。
- 候选归属三路：**并入 Account**（Account README 旧路线图曾规划 MFA→V0.2.0，但 V0.2.0 实际交付 Identity 集成，MFA 未落地；Account 现 V0.4.0 稳定）、**挂主框架**（仿 `IAccountLockoutPolicy`——框架调用扩展先例）、**独立扩展**（对标清单已定名 `TKWF.Ext.MFA`）。
- 消费 SMS/令牌能力有 L2 门控问题：Authentication 无 Abstractions 包——MFA 若 ProjectReference 其本体违反 ADR48 D7（TKWF0022 Error）；拆 Abstractions 是重前置（契约迁移 + 打包 + Authentication 版本升级）。

### 触发场景

- 消费方已有主认证（框架 AuthController / Authentication 扩展 / 自有 IdP），只缺第二因素层——MFA 应为**可插拔验证服务**，而非重建认证。
- 消费方登录流需要"主认证成功后插入 MFA 挑战"——插入点由消费方编排（无框架/认证扩展调用点可挂）。
- 短信发送渠道：消费方已接 Emailing/自有短信——MFA 不应强迫走某一条（Account v0.1.0 已裁定渠道归消费方）。

### 现有方案不足

- **`IAccountLockoutPolicy` 先例不适用于 MFA**：框架在 `LoginByContextAsync` 有 lockout **调用点**，但**无 MFA 调用点**（加 `IMfaPolicy` + 调用点 = 框架破坏性改动 + CPM lockstep 发布周期——v4.10.39 先例的周期成本）。
- **并入 Account**：第二因素 ≠ 账户策略（与锁定/重置流程正交），并包增大 Account 面；Account 已 V0.4.0 稳定，为 MFA 动其版本线不值。
- **拆 Authentication.Abstractions**：为下游消费者改上游契约是**倒置依赖**（Authentication V0.2.0 已稳定）；非 v0.1.0 范围（独立迭代候选）。

---

## 使用场景

1. **独立扩展**（常态）：消费方 `[TKWFEnabledExtension(MFA)]` + 白名单声明；登录编排：主认证 → `IsMfaEnabledAsync` → `RequestChallengeAsync` → `VerifyChallengeAsync` → 消费方签发令牌。MFA 与主认证完全解耦。
2. **短信渠道**：消费方实现 `IMfaSmsSender`（包装其 Emailing/自有短信）；MFA 不感知具体渠道。
3. **TOTP**：消费方配置 `TKWF:Mfa:SecretEncryptionKeyPath`；MFA 自研 RFC 6238（零第三方）。
4. **不适用**：MFA 不做主认证/不签令牌/不维护登录状态；多实例 SMS 频控不跨实例（v0.1.0 内存单实例，见方案 C8）；不拆 Authentication.Abstractions（v0.1.0 明确排除）。

---

## 决策

### 1. 独立扩展 `TKWF.Ext.MFA`（对标清单裁定落地）

- 目录 `_Framework/MFA`、tag `MFA/v*`、命名空间 `TKWF.Ext.MFA`、`[TKWFExtension("MFA")]`。
- **Account README 旧路线图（MFA 并入 Account V0.2.0）作废**——Account 已 V0.4.0 稳定，第二因素与账户策略正交。
- **零主框架改动**：不新增框架 MFA 契约/调用点（避免破坏性 + CPM lockstep 周期）；`IAccountLockoutPolicy` 先例不适用（框架无 MFA 调用点，勘察实证）。

### 2. 零扩展间依赖（L2 门控零违规）

- **不引 Authentication 本体**、**不拆其 Abstractions**（v0.1.0 明确排除；拆包 = 为下游改上游契约的倒置依赖）。
- **短信通道经消费方抽象 `IMfaSmsSender`**（TryAdd 语义无默认——对齐 `ISmsSender` 先例 + Account「验证码渠道归消费方」裁定）。
- **已知代价**：`SmsMfaMethod` 与 `SmsVerificationService` 有逻辑重叠（6 位码 + SHA256 + 频控 + 单次消费），但**语义不同**（MFA 第二因素 per-user×method vs Authentication 首因素 per-phone×scene + IP，频控维度不同）——非简单复制（Oracle Q1 裁决）。
- **收敛出口**：后续若消费方诉求直接复用 `ISmsVerificationService`，独立迭代评估 `Authentication.Abstractions` 拆包（命名空间不变零破坏，Emailing.Abstractions 模板）。

### 3. 消费方编排（MFA 不签令牌）

- **职责边界**：MFA = 第二因素验证服务（绑定管理 + 挑战验证 + 频控 + 恢复码）；**不签发令牌、不维护登录状态、不做主认证**。
- **哲学一致性**：TKWF 装配层驱动（框架 `IAccountLockoutPolicy` 是"框架有调用点调扩展"；MFA 无调用点 → 消费方在登录流编排是唯一零破坏路径——与 ABP 同路径）。

> **否决"挂主框架 IMfaPolicy"**（Option ③）：需框架新增契约 + `LoginByContextAsync` 调用点 + EnumLoginAuthType 扩展 + CPM lockstep 发布周期——v0.1.0 破坏性/周期成本不可接受；框架 v4.9.25 文档亦记录"interface 方向…远期"倾向。
> **否决"并入 Account"**（Option ②）：第二因素与账户锁定/重置流程正交；Account 已 V0.4.0 稳定；对标清单已独立定名 `TKWF.Ext.MFA`。
> **否决"拆 Authentication.Abstractions"**（v0.1.0）：倒置依赖；重前置；非本方案范围。

---

## 备选方案

### 并入 Account（Option ②）
- Account V0.5.0 增 MFA（旧路线图）。
- **否决**：正交性（第二因素 ≠ 账户策略）；Account 面膨胀；对标清单独立定名；已 V0.4.0 稳定。

### 挂主框架（Option ③）
- 框架新增 `IMfaPolicy` + `LoginByContextAsync` 调用点。
- **否决**：框架破坏性改动 + CPM lockstep 周期；框架 v4.9.25 文档"interface 方向…远期"；v0.1.0 零主框架改动目标。

### 拆 Authentication.Abstractions 供 MFA 消费
- MFA 直接复用 `ISmsVerificationService`/`ITokenService`。
- **否决（v0.1.0）**：倒置依赖（为下游改上游契约）；拆包重前置；独立迭代候选。已知代价（SmsMfaMethod 逻辑重叠）经 Q1 裁决可接受。

---

## 后果

### 正面
- L2 门控零违规（零扩展间依赖）；零主框架改动；零既有 API 破坏（绿地扩展）。
- 消费方编排 = 最大组装自由度（任意主认证 + MFA 组合）。
- 短信渠道归消费方（Account 裁定延续）——渠道单一真相。

### 负面
- 消费方需自行组装（主认证 → MFA → 签发）——使用指南给完整编排样例缓解。
- `SmsMfaMethod` 与 `SmsVerificationService` 逻辑重叠（独立实现代价）——收敛出口留后续。

### 缓解
- 使用指南编排样例（框架 AuthController + Authentication 双路径）。
- 重叠逻辑文档化 + 拆包收敛出口明确。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-10-01 | 📋 提议 | 初稿——随 v0.1.0 开发方案 Oracle 评审（bg_025c18fc）定案：独立扩展 + 零依赖 + 消费方编排（Q1/Q5 裁决） |
