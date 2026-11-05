# ADR-Federation-免登裸票注入与落地URL生成

> **版本**：v0.1（随开发方案 v0.5 审核闭环定稿）
> **状态**：已定（`docs/Federation/免登裸票注入与落地URL生成-开发方案.md` v0.5——Oracle6 `PASS WITH CONDITIONS`（C1/C2/C3）条件全闭环；code-reviewer 迭代级审核附条件通过，M1 + m1~m11 整改闭环）
> **关联**：开发方案 `docs/Federation/免登裸票注入与落地URL生成-开发方案.md`；ADR-Federation-渠道事件信封与关注关系域（`{ticket}` 免登链路前置）；框架 `ISealedTicketService<T>`（`TKW.Framework.Domain.Ticket`）；`TKWF.Federation.Abstractions`（契约包）/ `TKWF.Ext.Federation`（实现）/ `TKWF.Federation.WeChat`（占位符消费）
> **日期**：2026-11-05

---

## 目的与目标

把「免登票据」的**票值**与「落地 URL」的**组装权**解耦：扩展只签发一张密封票据并以**同票双形态**（裸票 `Ticket` + 完整 URL `Url`）一次性返回，落地 URL 的业务形态（`mid`/路径/参数）归消费方图文模板自拼。目标状态：① `ILoginEntryService` 新增 `IssueTicketAsync` 返回 `TicketIssueResult(Ticket, Url)`——一次签票、两值同源、恒双非空；② 平台层占位符新增 `{ticket_value}`（裸票）与既有 `{ticket}`（完整 URL）**并存同票**，不是二选一；③ 免登 URL 基址三级链（`ChannelBaseUrls` → `TestTicketEndpointBaseUrl` → 请求 Host 推导）全空时产**相对路径**而非 null；④ 显式 TTL 运行时钳到 `[1, MaxTicketTtlSeconds]`；⑤ 票据全串零日志（含 dev）在**类型层**亦有防护。读者 3 句话内明白：扩展交票、消费方拼 URL，同一张票两个形态同时可用，任何失败路径整体 null 不抛。

## 问题

### 问题现象

1. **落地 URL 被扩展独占**：`BuildTicketUrlAsync` 只产出扩展托管的 `{baseUrl}{RoutePrefix}{TicketPath}?ticket=` 形态，消费方业务落地页（如 `/m/{mid}/auth`）无法插入自己的路径与参数，只能被迫接受扩展形态或在扩展里加配置。
2. **裸票拿不到**：图文模板只能替换 `{ticket}`（完整 URL），无法拿到票值本身去拼业务 URL——`{ticket_value}` 占位符在模板中不替换（V0.5.0 已知缺口）。
3. **基址缺失即断链**：三级基址全空时旧语义为 fail-closed null，微信图文 link 拿不到任何链接（实际链路恒有请求 Host，可产同源相对路径）。
4. **TTL 只钳上限**：显式 `ttlSeconds` 为 0 或负数时透传给票据后端——DB 后端回落默认、InMemory 后端抛异常，同一配置在不同后端行为分叉。
5. **URL 组装异常逃逸**：签票成功后 URL 组装（`CombineUrl`/`RoutePrefix`）在 fail-closed 之外，异常会整条吞掉事件回复且票已签发（孤儿票）。
6. **票据可能被日志带出**：`TicketIssueResult` 若被 `$"{result}"` 或结构化日志整体输出，票值全串进入日志（§6.4 红线）。

### 触发场景

- 消费方图文模板需要把裸票拼进自有落地页：`https://m.example.com/m/{mid}/auth?ticket={ticket_value}`。
- 同时需要扩展托管 URL 的存量模板：`https://sso.example.com/login?ticket={ticket}`。
- 多域名/反代部署：基址未显式配置时依赖请求 Host 推导，或退化到本站点同源相对路径。
- 模板 `ttl_seconds` 误配（0/负数/超大值）。
- 票据后端不可用或 URL 组装异常：要求回复结构不变、不抛异常。

### 现有方案的不足

- **单一形态**：只有完整 URL，无法支撑业务自拼落地页；若要支持就得在扩展里堆业务路径配置，扩展被迫承载消费方业务编排。
- **null 即断链**：基址缺失按失败处理，实际可用信息（请求 Host）被丢弃。
- **钳制不完整**：仅上限，非正值跨后端语义不一致。
- **fail-closed 边界不清**：签票失败已 null，但组装失败仍抛——「不阻断回复」的契约在实现边界上不完整。
- **脱敏靠纪律**：无类型层防护，日志红线依赖调用方自觉。

## 使用场景

### 适用场景

- **业务自拼落地 URL（主场景）**：消费方模板 `{ticket_value}` 取裸票，自行拼 `{业务域名}/m/{mid}/auth?ticket={ticket_value}`。
- **存量扩展托管 URL**：`{ticket}` 继续可用，与裸票同票同源（一次回复只签一次票）。
- **双占位符并存**：同一模板同时使用 `{ticket_value}` 与 `{ticket}`——合法场景，两值来自同一次签票。
- **零配置冒烟**：测试端点 URL（`BuildTestTicketUrlAsync`）不经 `Enabled` 门控，密钥/DB 就绪即可产链接。
- **反代/多域名部署**：`ChannelBaseUrls` 按通道覆盖 → 全局测试端点基址 → 请求 Host 推导 → 相对路径兜底。

### 不适用边界

- **不承载业务路径配置**：扩展不新增业务 `mid`/路径配置项——落地 URL 形态归消费方模板，扩展只交票与默认 URL。
- **不改变票据原语**：仍用框架 `ISealedTicketService<T>`，不引入第二套票据实现。
- **不用于非事件链路**：`{ticket_value}` 仅在事件回复模板链路（`WechatTemplateEventReplyHandler`）替换；非签票场景沿用 4 参重载，`{ticket}` 保留原样。
- **不做票据落库/追踪**：本 ADR 不涉及票据持久化与审计（归 TrustCenter 密封票据职责）。

---

## 决策

### 1. 契约：`IssueTicketAsync` + `TicketIssueResult(Ticket, Url)`（`Federation.Abstractions` new minor）

- 新增 `Task<TicketIssueResult?> IssueTicketAsync(...)`，与 `BuildTicketUrlAsync` 共享同一签票核心与同一 URL 组装核心（逐字节等价，测试锁定）。
- `TicketIssueResult` 为 `sealed record (string Ticket, string Url)`——**两值恒双非空**（构造即完整，不存在部分成功）；任何失败路径由外层 `Task<TicketIssueResult?>` 的 null 整体表达。
- `override ToString()` 脱敏（`Ticket=***` / `Url=<含票URL省略>`）——防 `$"{result}"`/结构化日志把票值带出（§6.4 类型层防护）。

### 2. 占位符：`{ticket_value}` 与 `{ticket}` 并存同票

- `WechatEventReplyHandlerBase.ReplacePlaceholders` 新增 6 参重载：`{ticket}` → 完整 URL（存量语义不变）、`{ticket_value}` → 裸票；两值来自同一次 `IssueTicketAsync`。
- 用户裁定「不是二选一」：双占位符并存合法，一次回复只签一次票，不置空任何一方。

### 3. 基址三级链 + 相对路径兜底

- 顺序：`FederationLoginEntryOptions.ChannelBaseUrls[channelId]`（空白视同未配置）→ `FederationEventReplyOptions.TestTicketEndpointBaseUrl` → 请求 Host 推导 `requestBaseUrl` → 全空产 `/{RoutePrefix}/{path}` 相对路径（本站点同源），**不再 fail-closed null**。
- `CombineUrl` 对 `null`/空 `routePrefix` 安全归一且不产生双斜杠（纵深防御）。

### 4. TTL 钳制：`[1, MaxTicketTtlSeconds]`

- 显式 `ttlSeconds` 一律 `Math.Clamp(ttl, 1, MaxTicketTtlSeconds)`（默认上限 120、下限 1）；`null` 取 `TicketTtlSeconds`。
- 双层护栏：`Options` 启动期校验（`TicketTtlSeconds ≤ MaxTicketTtlSeconds`，超限 fail-fast）+ 运行时钳制。

### 5. fail-closed 边界完整化

- 签票失败、载荷组装失败（空 `channelId`/`externalUserId`）、URL 组装失败，统一 → null + Warning（不抛、不阻断回复）。
- URL 组装纳入 fail-closed 包装（`TryBuildTicketUrl`）；票已签发时不因组装异常吞掉整条回复。

### 6. 票据零日志

- `IssueTicketAsync` 任何模式（含 dev）不输出含票日志；完整 URL 的 dev 日志出口仅存于 `BuildTicketUrlAsync`。
- 失败 Warning 只记 `channelId` + `externalUserId` 前 6 位。

---

## 后果

### 正面

- 扩展与消费方职责清晰：扩展交票、消费方拼 URL；存量 `{ticket}` 零破坏。
- 一次签票双形态，无双签票/双票值不一致风险。
- 基址缺失、TTL 误配、组装异常三类边界行为确定且可测。
- 日志红线有类型层与出口层双重防护。

### 代价与约束

- `Federation.Abstractions` 新增成员（minor 版本），平台库/消费方需随包升级；`ResolveTicketAsync` 返回类型由 `string?` 变为 `TicketIssueResult?`（平台层内部链，属破坏性编译变更，方案 §4 已登记）。
- 相对路径兜底要求消费方落地页与服务同源部署（跨域场景须显式配基址）。
- 票据曝光面受 `MaxTicketTtlSeconds` 上限约束（默认 120 秒）。

### 已验证

- `TKWF.Ext.Federation.Tests` 183 用例全绿（含 29 条 `LoginEntryServiceTests`：双形态同票、URL 逐字节等价、三级基址、TTL 上下限钳制、tier1 空白跳过、零日志、`CombineUrl` 归一）。
- `TKWF.Federation.WeChat.Tests` 141 用例全绿（含 `{ticket_value}`/`{ticket}` 并存同票、fail-closed 空串渲染、内置测试模板链路）。

---

## 变更记录

| 版本 | 日期 | 内容 |
|------|------|------|
| v0.1 | 2026-11-05 | 初版——需求 B（形态①②）定稿：契约双形态、占位符并存、基址三级链+相对路径、TTL 区间钳制、fail-closed 完整化、零日志类型层防护 |
