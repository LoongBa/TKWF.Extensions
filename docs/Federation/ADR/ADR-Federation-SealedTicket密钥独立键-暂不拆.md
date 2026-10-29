# ADR-Federation-SealedTicket密钥独立键-暂不拆

> **版本**：v1.0（定稿）
> **状态**：已定稿（2026-10-30，D03 第 1 部分实施开发方案 Obs4 登记；框架组转达待办）
> **关联**：D03 §16.5 设计缺陷 4（T3 payload 密钥与 SsoClient credential 共钥）；开发方案 `docs/Federation/D03-联邦微信侧-第1部分实施-开发方案.md` §5.4；主框架 `SymmetricKeyProviderKeys`（v4.10.70 并入）
> **日期**：2026-10-30

---

## 目的与目标

决定 `ISealedTicketService<T>`（TrustCenter DB CAS 后端）与 `SsoClientService`（应用注册 credential AES-GCM）**是否拆分对称密钥域**。目标状态：明确本迭代**不拆键**（共钥维持），并登记转达框架组（新增 `SymmetricKeyProviderKeys.TrustCenterSealedTicket` 常量）+ 记录评估结论，后续版本择机拆键。

## 问题

### 问题现象

`SealedTicketService<T>`（TrustCenter 主包）经 `[FromKeyedServices(SymmetricKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider` 解析密钥——与 `SsoClientService`（应用注册 credential AES-GCM 加密/解密）**共用同一密钥键**（`TrustCenter`）。**轮换影响面耦合**：拧动 `TrustCenter` 密钥 → 同时失效 SealedTicket payload 密文 + SsoClient credential 密文——两个用途的密钥生命周期被绑死，运维轮换需同时迁移两类密文（Payload 表 + SsoClient 表）。

### 触发场景

- **生产密钥轮换**：运维按合规周期轮换 `TKWF:TrustCenter:SecretEncryptionKeyPath` 指向的密钥文件 → 存量 `TKWF_SealedTicket` Payload 密文 + `TKWF_SsoClient` credential 密文**全部不可解**——必须双表同时重写（SealedTicket 短 TTL 可自然消退，但 SsoClient credential 长生命周期直接失效需重注册）。
- **泄露隔离**：若某一用途密钥泄露（如凭据备份外泄），共钥下另一用途密文同样暴露——违背纵深防御。

### 现有方案的不足

- **单键深度耦合**：两个业务域（信任票据 vs 应用凭据）共用一把密钥 → 无法独立轮换、泄露影响面相乘；
- **框架常量缺口**：主框架 `SymmetricKeyProviderKeys`（v4.10.70）仅定义 `TrustCenter` 单键，无 `TrustCenterSealedTicket` 专用键——扩展侧无法凭空注册同名键（keyed DI 键视为唯一标识，新键须框架组并入常量 + 扩展 CPM lockstep）。

## 使用场景

### 适用场景（本次决策的边界）

- **D03 1.3 载荷切换**（2026-10-30 本迭代）：`ISealedTicketService<FederatedConnection>` → `FederatedTicketPayload` 切换——**不影响密钥域决策**（载荷类型变，密文机制不变）；共钥维持不阻塞兑付正确性。
- **后续版本拆键**（登记）：框架组新增 `SymmetricKeyProviderKeys.TrustCenterSealedTicket` → TrustCenter 初始器 `SealedTicketService<T>` 改用新键 → 生产迁移（SealedTicket 表短 TTL 自行消退，无需重写存量；SsoClient 表密钥不变）。

### 不适用边界

- 本 ADR 不覆盖其他扩展密钥域（AuthCenter `SymmetricKeyProviderKeys.AuthCenter` / Federation `SymmetricKeyProviderKeys.Federation` / MFA `SymmetricKeyProviderKeys.Mfa` 等）——各域独立；
- 不覆盖 SealedTicket 与 **Events/Credentials 之外的第三用途**（如未来 payload 字段级加密）——新用途一律新键。

---

## 决策

**本迭代不拆键**——理由：

1. **拆键依赖框架组**：需主框架新增 `SymmetricKeyProviderKeys.TrustCenterSealedTicket` 常量（keyed DI 键），扩展侧跨仓等待 + CPM lockstep——本迭代（载荷切换主体）零增益的情况下引入跨仓依赖不经济；
2. **正确性不阻塞**：共钥的代价是"轮换耦合 + 泄露面相乘"，不损害兑付正确性/防重放语义（CAS + TTL 独立）；
3. **风险登记**：转达框架组（新增键常量建议）+ 本 ADR 永续记录，拆键议题随 TrustCenter 下个破坏性迭代（若有）一并评估。

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-10-30 | 定稿——D03 §16.5 缺陷 4 评估落地（本迭代不拆、转达框架组、ADT 记录） |

<!-- EOF -->