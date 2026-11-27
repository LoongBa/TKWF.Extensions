# ADR-AuditLogging-完整性提供者与Web环境钩子

> **版本**：v0.1
> **状态**：提议（v0.7.0 合并迭代，待实施）
> **关联**：`docs/AuditLogging/v0.7.0-审计契约收口与统一读模型-开发方案.md` §11.2（G9/完整性并入）；《设计原则手册》P3/P5/P7/P12/P13；`设计原则/横切设计域.md`（事件/多租户）；ADR87（Web 装配钩子）
> **日期**：2026-11-27

## 目的与目标

补齐两件正交能力：① **可选防篡改**（`IAuditIntegrityProvider` 哈希链，默认关）为合规级审计提供篡改检测；② **IP/UA 自动填充**（独立 Web 包 `TKWF.Ext.AuditLogging.Web`）让手写 HTTP 端点/中间件也能为安全事件审计提供 ambient 上下文。两者均为**可选增强**，默认关/独立装配，不影响审计核心。

## 问题

### 问题现象

1. 合规场景需要"审计记录不可篡改/可检测篡改"；当前只有 append-only（不删），无篡改检测（改库直改可绕过）。
2. 安全事件依赖 `IAmbientContext["ClientIp"/"UserAgent"]`；手写认证/二维码端点走不到域 AOP，ambient 未填充 → IP/UA 缺失。
3. Domain 包不得引用 ASP.NET Core（ADR87/分层），Web 填充不能塞进主包。

### 触发场景

- 等保/合规要求审计防篡改（启用完整性）。
- 手写端点/自定义中间件需要 IP/UA 审计（装配 Web 钩子）。

### 现有方案的不足

- 默认开完整性：性能/迁移成本高、破坏链历史。
- 把 Web 填充放主包：违反 domain 零 AspNetCore / ADR87。
- 无完整性：直改库无法检测。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 防篡改（可选） | `IAuditIntegrityProvider`：`ComputeAsync(prev, entry)` 生成链 hash 落 `IntegrityHash`；读侧 `VerifyAsync` 校验链；**默认关** |
| 特权删除 + 完整性 | 启用完整性时，特权删除改**墓碑**（Action=`Deleted` + 保留记录/标记），不物理删，保链完整 |
| IP/UA 自动填充 | 装配 `AuditAmbientWebExtension`（AfterRouting 后从 `HttpContext` 填充 `IAmbientContext`）；domain 零 AspNetCore |

**不适用边界**：完整性不替代对外不可变存储（DBA 仍可改库，需外部 WORM/WORM 存储配合）；Web 钩子只管 ambient 填充，不承载领域决策（ADR87）；不承诺 full AOT（ADR84）。

## 决策

1. **`IAuditIntegrityProvider`（Abstractions，默认关）**：哈希链（前条 hash→本条 hash）；启用时写路径计算并落 `IntegrityHash`；读侧 `VerifyAsync` 检测断链。默认关闭（P3：能力=注册；默认不注册/不启用）。
2. **特权删除与完整性协同**：启用完整性 ⇒ 特权删除**改墓碑**（不物理删），保链连续；未启用 ⇒ 物理删 + 留痕（v0.7 现状）。
3. **独立 Web 包 `TKWF.Ext.AuditLogging.Web`**：`AuditAmbientWebExtension`（`IWebExtension`，ADR87 三钩子只做宿主注册）从 `HttpContext` 填充 `IAmbientContext`；domain 包保持零 AspNetCore（P13）。
4. **默认关 + 零订阅零开销**：完整性未启用时零计算；Web 包未装配时零影响。
5. **契约在 Abstractions / Web 在独立包**：`IAuditIntegrityProvider` 契约入 Abstractions；Web 钩子入 `*.Web` 包（对齐 Notifications.SignalR/ADR87）。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 默认开启完整性 | 性能/迁移成本；破坏既有数据链 |
| B. 完整性放主包并耦合写路径 | 不可选/不可关；违反 P3 |
| C. Web 填充塞主包 | domain 引 AspNetCore（违 ADR87/P13） |
| D. 不做完整性 | 合规场景缺失 |

## 后果

- **正面**：合规可选项就绪；IP/UA 覆盖手写端点；两者正交、默认可关。
- **负面/缓解**：完整性启用需链迁移（历史记录无 hash → 从启用点起算，文档化）；Web 包需消费方装配（未装配时 IP/UA 仍需消费方自行填充）。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿——完整性哈希链（默认关/特权删除墓碑）+ 独立 Web ambient 包；v0.7.0 合并迭代 |
