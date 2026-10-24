# 转达：微信事件回复 Handler 排他单模型 — 消费方适配通知

> **发布**：2026-10-25（Federation v0.12.0 前置 / Federation.WeChat v0.7.0 前置 / WeChatOperations v0.6.0 前置）
> **版本性质**：**加性为主，一处行为语义变化**（运营 handler 未覆盖分支），无接口签名破坏（`IEventReplyHandler` 契约零改动）
> **关联**：使用指南 `docs/Federation/联邦互联-使用指南.md` §5.1（已同步更新）；DMP-Lite ADR04
> **DMP-Lite 适配实证**：v0.9.20（删 `LandingTicketHandler` + `AutoProvision=false` + AuthCenter V0.12.0 `RefreshRequest` 适配）

---

## 一、背景

微信事件回复 handler 此前**多实现并存**（登录/运营/诊断/模板四类挤在一条 `IEventReplyHandler` 集合，first-non-null-wins 按 DI 注册序仲裁）——行为依赖隐式注册序（不确定性），且模板渲染逻辑双份实现。现落地**排他单模型**：

> 同一 `ChannelType` 至多一个**业务** handler（探针/诊断除外）；模板 handler 升级为可继承 T1 默认业务基座；运营规则 handler（`WeChatOperationsReplyHandler`）作为 T2 **继承**模板 handler；排他守卫 fail-fast。

## 二、破坏性 / 加性变更清单

| # | 变更 | 类型 | 影响 |
|---|------|------|------|
| 1 | `WechatTemplateEventReplyHandler` **解除 sealed**（可继承 T1 基座） | 加性 | 存量直接实例化/注册**零破坏**；新能力：自定义业务 handler 可继承它 |
| 2 | `WechatTemplateEventReplyHandler` ctor 增 `IOptions<WechatEventReplyOptions>? options = null`（第二参，可空） | **签名加参（可空）** | 直接 `new WechatTemplateEventReplyHandler(user)` 仍编译（可空缺省）；守卫工厂 `ActivatorUtilities` 自动从 DI 解析 |
| 3 | `WeChatOperationsReplyHandler` 改继承 `WechatTemplateEventReplyHandler`（原继承 `WechatEventReplyHandlerBase`） | 加性（实现链变） | 对外 API/注册方式不变；**行为变化一处**（见 §三） |
| 4 | 新增 `WechatEventReplyOptions.RequireUidForTicket`（默认 false） | 加性 | 默认行为不变；`true` 时模板 `{ticket}` 登录卡仅发已绑定 uid 用户 |
| 5 | 新增 `DiagnosticEventReplyHandlerAttribute`（Federation.Abstractions） | 加性 | 探针已标记；消费方自定义诊断类可复用 |
| 6 | `EventOrchestrator` ctor 排他守卫（fail-fast） | **行为变化（新增守卫）** | 同 ChannelType 注册 ≥2 个**业务** handler → 启动抛装配错误（探针/诊断 + `IChannelSpecificEventReplyHandler` 通道绑定不计数） |
| 7 | 新增 `/land/{ticket}` GET 端点（`LandEndpointEnabled` 默认 false + `LandRedirectUrl`） | 加性 | 默认关零影响；启用需配 `LandRedirectUrl` |
| 8 | 删除 `WeChatOperationsReplyHandler.TemplateFallbackAsync` / `ReadNewsTemplateFromChannel`（私有方法） | 加性（内部重构） | 消费方不可见（private）；模板兜底改走继承的 `OnDefaultAsync` |

## 三、行为语义变化（⚠️ 消费方需知晓）

**运营 handler 未覆盖分支（OnDefaultAsync）**：重构前继承裸基类返回 null（不回复）；重构后继承模板 handler → **返回模板回复**（`Channel.Extra["news_reply"]` 有模板时）。

- 对**已配模板**的通道：未覆盖事件（未知 MsgType/Event）从"不回复"变"模板回复"——符合"模板继承天然兜底"裁定
- 对**未配模板**的通道：行为不变（模板缺失 → null → 不回复）

**`OnTextAsync` 5 步回复链**（规则 → 素材 → 模板兜底 → 默认文案）语义完全不变（第 4 步内部改调 `base.OnDefaultAsync`，功能等价）。

## 四、消费方适配动作

### 4.1 一般消费方（仅用模板/运营，未自写业务 handler）

- **零代码改动**。升级扩展 DLL/源码后：
  - 单注册 `WechatTemplateEventReplyHandler` 或 `WeChatOperationsReplyHandler`（二选一）→ 行为如常
  - **切勿同时注册两个业务 handler**（排他守卫会抛错）——原先"并存靠 first-non-null"的写法必须收敛为单注册
- 可选：启用 `RequireUidForTicket=true`（登录卡仅发已绑定用户）

### 4.2 自写业务 handler 的消费方（如 DMP-Lite 原 `LandingTicketHandler` 模式）

**改继承**：`WechatEventReplyHandlerBase` → **`WechatTemplateEventReplyHandler`**：

```csharp
// 旧（已不推荐——裸基类无模板兜底）
public sealed class MyBizHandler : WechatEventReplyHandlerBase { ... }

// 新（继承模板基座——未覆盖分支天然模板兜底，签票能力复用）
public sealed class MyBizHandler : WechatTemplateEventReplyHandler
{
    public MyBizHandler(IDomainUser user, ILogger? logger = null) : base(user, logger) { }
    protected override Task<WechatReplyPayload?> OnSubscribeAsync(...) { ... }
    // 其余分支未覆盖 → 继承模板 OnDefaultAsync
}
```

- **注册**：`services.TryAddEnumerableConstructible<IEventReplyHandler, MyBizHandler>()`（与模板 handler / 运营 handler **二选一**）
- **模板签票**：模板含 `{ticket}` 时基类 `ResolveTicketAsync` 自动签免登票据（原主包 `EventReplyHandlerBase` 路径可退役）

### 4.3 认证（免登登录）消费方

- **票据语义**：模板 `{ticket}` 签的票据为 **openid 级**（`FederatedIdentity.UId=""`）——登录侧 `FederatedLoginOrchestrator` 实时回查 uid。**admin 登录通道必须 `AutoProvision=false`**（`TKWF:AuthCenter:FederatedProvisioning:AutoProvision`），否则未绑定 openid 点登录卡会静默建号（fail-closed）
- **登录闭环**：`/land/{ticket}` 兑换票据 → 302 落地页带 `?ticket=&uid=` → 消费方 SPA 调自有 `POST /login/ticket`（`IExternalTicketLoginService.LoginAsync`）建会话（两步闭环）
- 如需 uid 承载票据（绑定即签入 uid），保留主包 `EventReplyHandlerBase` 直签路径（DMP 已删该路径，评估后取舍）

### 4.4 调试探针

- 开关不变：`TKWF:Federation:WeChat:EventReply:ProbeEventReplyHandlerEnabled`（默认 false）
- 探针已标记 `[DiagnosticEventReplyHandler]` → 不参与排他计数，可与业务 handler 共存（调试期）
- 调试完成即关（领域自治：表现层零开关）

## 五、升级顺序建议

1. **后端先**：升级扩展 → 收敛单业务 handler 注册（删除多余的并存注册）→ 自写 handler 改继承模板基座 → 编译验证（排他守卫违反 = 编译/启动期即暴露）
2. **配置随后**：admin 通道 `AutoProvision=false`；可选 `RequireUidForTicket=true`；需登录闭环时开 `LandEndpointEnabled` + 配 `LandRedirectUrl`
3. **前端最后**：落地页接 `POST /login/ticket`（若启用 /land）

> ⚠️ **升级后首启必查**：若原注册了 ≥2 个业务 handler（如 DMP 原 `LandingTicketHandler` + `WeChatOperationsReplyHandler` 并存），排他守卫会在首次事件编排/启动期抛 `InvalidOperationException`——**先删多余注册再启**。

## 六、DMP-Lite 适配实证（参考）

| 动作 | 落点 |
|------|------|
| 删 `LandingTicketHandler.cs` + 注册行 | `Platform.Domain` |
| 认证改走模板 `{ticket}` 签票（基类 `ResolveTicketAsync`） | 通道配置 `Channel.Extra["news_reply"]` |
| `AutoProvision=false` | `Platform.Web/appsettings.json` |
| 探针开关回滚 false | `Platform.Web/appsettings.json` |
| AuthCenter V0.12.0 适配（`RefreshRequest` DTO） | `Platform.Web/Program.cs:288` |
| 双模式编译 + 测试 | 0 错误；Merchant.Domain.Test 122/122 |

## 七、测试基线

| 项目 | 用例 | 结果 |
|------|------|------|
| Extension.Federation.Tests | 122 | ✅（含排他守卫 3） |
| Extension.Federation.WeChat.Tests | 119 | ✅（含 uid 门 3） |
| Extension.WeChatOperations.Tests | 143 | ✅（继承重构回归） |
| **合计** | **384** | ✅ |
