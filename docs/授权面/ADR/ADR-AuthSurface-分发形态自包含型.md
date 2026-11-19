# ADR-AuthSurface-分发形态自包含型

> **版本**：v1.0
> **状态**：已实施（2026-11-20）
> **关联**：框架 A6 裁定（`_TKWF/docs/G17A-设计扩展模块-使用指南.md` §7.8、`G17B-使用扩展模块-使用指南.md` §10.2，v4.10.122）；本仓 `docs/扩展模块通用指南.md` §二；技术规范 `_Framework/AuthSurface/README.md`

## 目的与目标

明确 AuthSurface 对外分发形态为**自包含型（单包）**：消费方按"单包"引用本扩展，不再因分发产物缺少 `TKWF.Ext.AuthSurface.Abstractions` 而误判为分发缺口；同时禁止"空 Abstractions + DTO 仍留主包"的第三形态。

## 问题

- **现状成因**：UserCenter 于 2026-10-08 退役后，AuthSurface 成为兑换/应用查询的唯一通道，DTO（`RedemptionRecordDto`/`UserAppDto`/批次系列）归主包 `Contracts/`，零跨扩展契约消费——属框架判据下的**自包含型**，而非疏漏。
- **问题现象**：框架 v4.10.122 前无显式形态裁定，消费方按旧约定"主包 + Abstractions 成对引用"接入 AuthSurface 时无法完成（找不到契约包），只能违反约定直引实现或改用本地 stand-in。
- **现有方案的不足**：若为对齐模板而新建空 Abstractions，会形成"契约包空壳 + DTO 在主包"的双源形态，比缺包更差（框架已明令禁止）。

## 使用场景

| 场景 | 口径 |
|---|---|
| 终端应用消费 AuthSurface（兑换 / 我的应用 / 批次管理） | **单包引用** + `[TKWFEnabledExtension]` 白名单启用（须同时启用 AuthCenter——两 VEntity 视图依赖其基表） |
| 其他扩展需跨扩展消费 AuthSurface 的接口/DTO | 触发**升格**：先抽 `TKWF.Ext.AuthSurface.Abstractions`（迁移 DTO/错误码，namespace 保持 `TKWF.Ext.AuthSurface`，独立 tag `AuthSurface.Abstractions/v`），再由消费方改引契约包 |
| 分发产物检查 | `build\extensions\refs` 无 `TKWF.Ext.AuthSurface.Abstractions.dll` 属**预期**，不作为缺包处理 |

**不适用**：任何"先建空 Abstractions 占位"的做法；无真实跨扩展消费方时为对齐模板而拆分。

## 决策

采用**自包含型**（依据框架 A6 判据：零跨扩展契约消费）：

1. 产物形态 = 单包（主包 + `Contracts/` DTO），不建 Abstractions；
2. `_Framework/AuthSurface/README.md` 首屏声明形态（消费方判型依据）；
3. `docs/授权面/授权面扩展-使用指南.md` §1 注明"只引主包"；
4. 升格路径固定：出现首个跨扩展契约消费方 → 当批抽取契约包（DTO/错误码迁出、主包引用契约包、独立 tag），不留过渡空壳。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| 新建空 `TKWF.Ext.AuthSurface.Abstractions` | 框架 G17A §7.8 / G17B §10.2 明令禁止第三形态（双源更糟）；当前无跨扩展消费方，纯维护成本 |
| 现在就抽完整契约包（含 DTO） | 违反"按需创建，不过度设计"（框架 §7.6）；无消费方时契约包与主包版本联动徒增发布面 |
| 维持"半契约"（只抽接口不抽 DTO） | 接口与 DTO 分离会制造跨包引用复杂度，且当前无跨扩展消费需求 |

## 后果

**正面**：
- 消费方引用口径唯一（单包），分发产物不再被误判为缺口；
- 与框架 v4.10.122 形态裁定一致，跨仓库口径统一。

**负面**：
- 若未来出现跨扩展消费方，需要一次形态升格（DTO 迁移 + 消费方改引 + 契约包 tag）。

**缓解**：
- README 首屏 / 使用指南 §1 / 本 ADR 三处写明形态与升格路径；通用指南 §二 已按型引用同步。
