# ADR-Federation.WeChat-菜单发布确认语义

> **版本**：v0.1
> **状态**：已实施（2026-11-04，v0.17.1/v0.8.1 迭代）
> **关联**：开发方案 `docs/Federation.WeChat/v0.17.1-微信当前菜单sub_button形状修复-开发方案.md`；`ADR-Federation.WeChat-菜单出站API与凭证解析可插拔.md`（菜单 3 API 既有 ADR）；DMP-Lite `v0.10.4-微信当前菜单查询sub_button结构不匹配-转扩展组.md`（缺陷转达）
> **Oracle 评审**：方案 PASS WITH CONDITIONS（2026-11-04）——P1-2 半成功副作用处理裁定"本轮一并修（best-effort 确认语义）"
> **日期**：2026-11-04

---

## 目的与目标

发布菜单时消除「微信侧已生效、本地未落」的**半成功副作用**：`PublishAsync` 在 `menu/create` 成功后确认拉取（`get_current_selfmenu_info`）失败时，仍以提交结构落本地（快照/版本/审计），保证本地菜单基准与微信侧最终一致（由后续 `GetCurrentAsync` 拉取自愈）。读者 3 句话内明白：菜单发布的本地落库语义从"确认拉取成功才落"变为"创建成功即落（确认 best-effort）"；快照 = 最后已知菜单结构，自愈前可能是"提交结构"而非"微信真实结构"；该语义变更经审计 Remark 标注可追溯。

## 问题

### 问题现象

1. **半成功副作用**：`PublishAsync` L134 `CreateMenuAsync`（`cgi-bin/menu/create`）已在微信侧**真实生效**，L135 确认拉取失败（异常发生在事务开启之前）→ 本地 `MenuSnapshot`/`MenuVersion`/`MenuPublishLog` 未落，用户看到"发布失败"，但微信侧菜单已变。
2. **缺陷叠加**：v0.10.4 转达的形状缺陷（`sub_button` 对象形状 vs poco 数组建模）使含二级子菜单的发布**必现**该副作用；converter 修复后形状缺陷消除，但网络抖动/超时/微信限流/5xx 等**非确定性失败**仍会触发。
3. **回滚同链**：`RollbackAsync` 复用 `PublishAsync` 全链，半成功副作用传染回滚操作——回滚失败时微信侧已回滚、本地版本未追加，状态混乱。

### 触发场景

- 发布含二级子菜单的菜单（形状缺陷场景——v0.17.1 修复后不再触发，但为同类确定性失败兜底）。
- 确认拉取网络超时（`TaskCanceledException` 且 ct 未取消——HttpClient 超时形态）、连接失败、微信限流/5xx（非确定性失败——best-effort 的核心目标）。

### 现有方案的不足

- **失败即中断**：确认拉取失败抛异常 → 微信侧已变但本地不落 → 重试或回滚都在错误基准上操作。
- **无补偿路径**：既无"失败仍落"的兜底，也无后台补偿任务；用户手动重试发布前存在不可控窗口。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 发布菜单（确认拉取失败——非取消） | best-effort：`WrapSubmittedMenuJson(menuJson)` 包装提交结构落快照 + 版本 `AfterJson` 保 raw + 审计 Remark 标注"确认拉取失败 best-effort"；返回降级结果 |
| 回滚菜单（复用 PublishAsync 全链） | 同 above——回滚操作同语义受益 |
| 快照自愈 | 后续 `GetCurrentAsync` 拉取微信真实状态 → `UpsertSnapshotAsync` 覆盖快照（自愈前快照 = 提交结构，可能非微信真实结构） |
| 真取消（ct 已请求） | 不吞：`catch when (ex is not OperationCanceledException \|\| !ct.IsCancellationRequested)`——真取消传播异常 |

### 不适用边界

- **无需补偿性重试**：发布后页面即拉取（GetCurrent 自愈足够），不做后台补偿任务。
- **不改变快照承载格式**：快照仍存 pocoo 序列化（数组 sub_button 形状），不直接存 raw 提交 JSON（`WrapSubmittedMenuJson` 包装保 pocoo 兼容——`GetClickKeysAsync` 可解析）。
- **不吞真取消**：`OperationCanceledException` 且 ct 已请求 → 传播（用户主动取消不应伪造成功）。

---

## 关键决策记录

| # | 决策 | 理由 |
|---|------|------|
| 1 | 确认拉取失败时用提交结构构造快照承载（`{"is_menu_open":1,"selfmenu_info":<raw>}` 包装——pocoo 兼容形状） | `GetClickKeysAsync` 按 pocoo 反序列化；raw 内 `sub_button` 数组形状由 `WeChatSubButtonListConverter` 接受，双重兼容 |
| 2 | 版本 `AfterJson` 恒存 raw 透传原样 | 回滚数据源不因 best-effort 污染 |
| 3 | 审计 Remark 追加"（确认拉取失败 best-effort）" | 业务留痕可追溯，与正常发布区分 |
| 4 | 语义变更：快照从"微信侧真实状态"→"最后已知结构（自愈前可能瞬时偏差）" | 消除半成功窗口的代价——快照短暂可能为提交结构；GetCurrent 自愈收敛 |
| 5 | 不做补偿性重试 | 发布后页面即拉取，自愈链足够；避免重试风暴 |
| 6 | 捕获范围 `Exception when (ex is not OperationCanceledException \|\| !ct.IsCancellationRequested)` | 非取消异常全兜 + 超时型（TaskCanceledException、ct 未取消）纳入；真取消传播 |
| 7 | catch 内 `ILogger?` 可选注入 + `LogWarning(ex, ...)` | 失败可观测（审核 P1-2 条件；仓库 ILogger 可选尾参惯例对齐） |

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-11-04 | v0.1 | 初始——best-effort 确认语义裁定（Oracle PASS WITH CONDITIONS P1-2 吸收）；实施于 v0.17.1（Federation.WeChat converter）/ v0.8.1（WeChatOperations PublishAsync） |