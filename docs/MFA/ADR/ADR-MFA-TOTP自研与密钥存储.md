# ADR-MFA-TOTP自研与密钥存储

> **版本**：V0.1.0（目标）
> **状态**：📋 提议（随 v0.1.0 开发方案，Oracle 评审 bg_025c18fc 定案）
> **关联**：`docs/MFA/MFA多因素认证-开发方案.md`（Oracle PASS WITH CONDITIONS，C6/P9/P11）、`PlatformCredentialEntityDataService` AES-GCM 先例、Authentication 手写 RS256 JWT 先例、Calendar RRULE/Tagging 算法自研先例
> **关键字**：MFA、TOTP、RFC 6238、自研、Otp.NET、AES-GCM、SecretEncryptionKeyPath、fail-fast

---

## 目的与目标

裁定 MFA TOTP 能力的**实现路径**（自研 RFC 6238 vs 引 Otp.NET NuGet）与 **secret 密钥存储方案**（加密位置/密钥托管/缺失行为）。目标状态（3 句内）：**TOTP 自研 RFC 6238**（HMAC-SHA1 + 30s 时间步 + 6 位 + Base32 secret + ±1 时窗容差，~100 行）——零第三方 NuGet，正确性以 RFC 6238 附录 B 官方向量 + 时间步边界用例 + 运行时自洽交叉验证锚定；**secret AES-GCM 加密在 DataService 边界**（对齐 `PlatformCredentialEntityDataService` 先例），密钥经 `TKWF:Mfa:SecretEncryptionKeyPath` 配置派生，**生产缺密钥 fail-fast 拒启动**。

---

## 问题

### 问题现象

- MFA 需要 TOTP（时间基一次性密码）能力——主框架/认证扩展均无现成设施（`AuthAccountEntity` 无 TOTP secret 列；全仓零 TOTP 代码）。
- 实现路径两选：**自研 RFC 6238**（HMAC-SHA1 计数器 → 动态截断 → 6 位，~100 行）vs **引 Otp.NET**（成熟库但新增外部依赖）。
- secret 存储：TOTP secret 是长期密钥（不同于一次性 SMS 码）——明文落库不可接受；加密方案/密钥托管/缺失行为须裁定。

### 触发场景

- 用户绑定验证器：生成 secret → 前端展示 provisioning URI/QR → 用户扫码 → 激活确认（输一次码）。
- 每次 TOTP 挑战：按当前时间步算期望码 → 与用户输入比对（±1 时窗容差防时钟漂移）。
- 生产启动：secret 加密密钥未配置——须 fail-fast（否则 secret 无法加解密，绑定/验证全部 500）。

### 现有方案不足

- **自研风险**：HMAC 字节序（计数器大端编码）/动态截断（取末 4 位偏移）/mod 10^digits——细微 bug 难发现（固定时间戳向量可能掩盖运行时错误）。
- **引库代价**：AGENTS §4 外部依赖变更需 ADR；版本跟进/供应链面；Otp.NET 功能超出需求（HOTP/TOTP 完整 + 多哈希）。
- **明文落库**（若偷懒）：secret 泄露 = 验证器可克隆，MFA 形同虚设。

---

## 使用场景

1. **验证器绑定**（常态）：`EnrollAsync(userId,"totp")` → 生成 16 字节随机 secret → Base32 编码 → provisioning URI（`otpauth://totp/{issuer}:{user}?secret=...`）→ 前端 QR → `ConfirmEnrollAsync` 输一次码激活 → secret AES-GCM 加密落库。
2. **TOTP 挑战**：`RequestChallengeAsync(userId,"totp")` → 建票据句柄 → 用户输验证器码 → 解密 secret → 按当前时间步（±1 窗口）算码比对（恒定时间）→ 通过/失败。
3. **生产启动**：`SecretEncryptionKeyPath` 未配置 → 拒启动（fail-fast，防运行期 500 风暴）。
4. **不适用**：TOTP 算法不入主框架 Utility（v0.1.0——需主框架改动 + 发布周期；ADR52 收纳候选留后续）；不支持 HOTP/多哈希（超出范围）。

---

## 决策

### 1. TOTP 自研 RFC 6238（Oracle Q2 裁决 + P11）

- **算法**：HMAC-SHA1（RFC 6238 附录 B 向量锚定）+ 30s 时间步 + 6 位 + Base32 secret（16 字节随机，`Rfc6238Totp` 内部类型）+ **±1 时窗容差**（防时钟漂移，`TotpClockSkewWindows=1` 对齐 Google Authenticator）。
- **正确性三锚**：①RFC 6238 附录 B SHA1 向量（8 位向量验证算法正确性——6 位仅 mod 10^6 截取差异）②时间步边界用例（步切换前后 ±1 窗口内可验、±2 失败）③**P11 运行时自洽交叉验证**（生成 secret → 同 secret 按当前时间步算码 → 立即验证通过——附录 B 固定时间戳无法覆盖运行时正确性）。
- **零 NuGet**（不引 Otp.NET）——免 ADR 外部依赖变更；对齐最强先例：Authentication **手写 RS256 JWT（零第三方 JWT 库）**（比 TOTP 复杂得多的密码学自研先例）+ Calendar RRULE/Tagging 算法自研。
- **收纳候选**：`TKW.Framework.Utility`（对齐 ADR52 收纳先例）留作后续（需主框架改动 + 发布周期）。

### 2. secret AES-GCM 加密在 DataService 边界（Oracle C6 修正）

- **对齐 `PlatformCredentialEntityDataService` 先例**：`AppSecretEncrypted` 列 + `AuthCenterOptions.SecretEncryptionKeyPath` 派生 AES-GCM 密钥——DB 无明文；`MfaSecretEntity.SecretEncrypted` 同理（`DtoFieldIgnore`/`JsonIgnore` 不入 DTO）。
- **密钥配置**：`TKWF:Mfa:SecretEncryptionKeyPath`（`SecretEncryptionKeyPath` 派生密钥）。
- **⚠️ 不引虚构接口**：`ICredentialProtector` 全仓不存在（Oracle C6 实证）——加密在 DataService 边界内实现（对齐 PlatformCredential 实际机制）。
- **fail-fast**：生产缺密钥 → 拒启动（对齐 Authentication 密钥 fail-fast 先例——`IsProduction` 门 + 启动预检）。

> **否决"引 Otp.NET"**（Option ②）：功能超出需求（HOTP/TOTP 完整 + 多哈希）；外部依赖 + 供应链面；手写 RS256 JWT 先例证明自研质量可控。
> **否决"明文落库 secret"**：secret 泄露 = 验证器可克隆，MFA 失效；AES-GCM 加密是安全底线。

---

## 备选方案

### 引 Otp.NET（Option ②）
- 成熟库 + 少写代码。
- **否决**：外部依赖变更（需 ADR）+ 版本跟进；功能超出需求；与自研先例（手写 RS256 JWT）冲突；正确性可由附录 B 向量 + 交叉验证等价锚定。

### TOTP 算法直接入主框架 Utility
- 对齐 Calendar RRULE 收纳先例。
- **否决（v0.1.0）**：需主框架改动 + CPM lockstep 发布周期；扩展先行验证算法成熟后再收纳（Tagging 收纳先例的时序）。

### secret 明文落库（Option ③）
- **否决**：secret 泄露 = 验证器可克隆；AES-GCM 加密是安全底线；`PlatformCredentialEntity` 先例明确"DB 无明文"。

---

## 后果

### 正面
- 零第三方依赖（供应链面最小）；正确性三锚（向量 + 边界 + 自洽交叉）可控。
- secret 密文落库（DB 无明文）；缺密钥 fail-fast 防运行期故障风暴。

### 负面
- 自研算法维护责任（~100 行 + 测试）；收纳主框架前扩展侧持有算法。
- AES-GCM 密钥托管归消费方（配置路径）——消费方需安全保管密钥文件。

### 缓解
- 三锚测试固化正确性；收纳候选明确（后续对齐 ADR52）。
- 使用指南明示密钥配置 + fail-fast 行为。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-10-01 | 📋 提议 | 初稿——随 v0.1.0 开发方案 Oracle 评审（bg_025c18fc）定案：自研 RFC 6238（Q2）+ DataService 边界 AES-GCM + SecretEncryptionKeyPath + fail-fast（C6）+ 运行时交叉验证（P11） |
