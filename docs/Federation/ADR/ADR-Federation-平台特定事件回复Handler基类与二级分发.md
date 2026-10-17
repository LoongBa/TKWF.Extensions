# ADR-Federation-平台特定事件回复Handler基类与二级分发

> **版本**：v0.1（随开发方案评审——Oracle PASS WITH CONDITIONS 后实施落地）
> **状态**：已实施（2026-10-17——WechatEventReplyHandlerBase / WechatTemplateEventReplyHandler / WechatProbeEventReplyHandler / WeChatEventChannel 编排放权 / TestEventReplyHandler 删除）
> **关联**：开发方案 `docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md`（Oracle PASS WITH CONDITIONS——P0×2 + P1×4 + P2×7 全部吸收）；本 ADR **废弃** `ADR-Federation-事件回复handler基类与内置测试设施.md`（其 EventReplyHandlerBase/TestEventReplyHandler 决策被本 ADR 演进）；ADR-Federation-外部身份联邦与密封票据适配（E1-E3 事件链底座）；TrustCenter.Abstractions（`ISsoChannel`/`SsoChannelEventContext`/`EventReplyResult`/`EventReplyNewsArticle`）
> **日期**：2026-10-17

---

## 目的与目标

为微信公众平台事件回复链路提供**平台特定 handler 分层**：① `WechatEventReplyHandlerBase`（Federation.WeChat 平台库）——MsgType/Event **二级分发**（强类型虚方法 OnSubscribe/OnClick/OnText...）+ 图文/文本构造工具（支持多图文）+ **协议门控**（unsubscribe/MASSSENDJOBFINISH 强制禁回复）；② `WechatTemplateEventReplyHandler`——模板驱动默认 handler（消费方显式注册，零代码模板配置）；③ `WechatProbeEventReplyHandler`——诊断探针（全分支可区分回复/日志，开关门控默认关）。同时**收口事件链迭代权**——`WeChatEventChannel.HandleEventAsync` 回归"验签+解密+富化+返回 null"，handler 迭代唯一归口编排器 `EventOrchestrator`（W3 绑定桶对微信可达）。读者 3 句话内明白：平台库提供微信特有的二级分发 handler 三层能力（基类/模板/探针），通道不再自迭代 handler（编排放权编排器），协议门控由平台基类强制（业务无法对禁回复事件回复）。

## 问题

### 问题现象

1. **无 MsgType/Event 二级分发**：业务 handler 无法按事件/消息类型分流——CLICK 菜单 key、SCAN 场景值、location 经纬度等需自行 re-parse `__raw` XML（通道仅富化扁平 `Parameters["msg_type"]/["event"]/["content"]`，EventKey 等未类型化——G3 缺口）。
2. **协议门控缺失**：handler 可对 unsubscribe/MASSSENDJOBFINISH 回复被动消息——微信平台拒绝/报错（回复不送达/群发回调禁回复）——G2 缺口。协议规则原为"留给消费方自觉"，违约不可控。
3. **W3 绑定桶对微信生产不可达**：`WeChatEventChannel.HandleEventAsync` 恒返回非 null（空回复兜底）→ 编排器 `EventOrchestrator` 的 handler 双桶（`IChannelSpecificEventReplyHandler` 精确 ChannelId 绑定优先）被短路（L104-106），通道内部迭代仅 ChannelType 匹配——**通道特定绑定缝在微信真实路径永不触发**（G4 缺口，测试用 Fake 通道掩盖）。
4. **handler 迭代权分裂**：通道内部迭代 + 编排器双桶并存，平台间行为不一致（WeCom/DingTalk 走编排器，WeChat 走通道内部）——G5 缺口。
5. **XML 信封在通道内硬编码**：`BuildTextReplyXml`/`BuildNewsReplyXml` 在 `WeChatEventChannel` 内——通道职责超出"信任根+取身份"——G6 缺口。
6. **模板驱动潜伏缺陷**：`ReadNewsTemplate` 用 STJ 默认反序列化（大小写敏感 + 不处理 snake_case），模板 JSON 键为 `title/description/link/pic_url/ttl_seconds/enabled` → **模板恒绑定失败 → 模板 handler 永远返回 null**（潜伏缺陷，自 `TestEventReplyHandler.ReadTemplate` 迁移源携入，模板功能从未被真实配置验证——2026-10-17 测试发现并修复）。

### 触发场景

- 消费方接入微信未认证号事件链路，需按事件/消息类型分流处理（关注欢迎语 / 菜单点击 / 扫码返图 / 文本自动回复等）。
- 微信协议规则保护——unsubscribe（用户已取关，回复无意义）/ MASSSENDJOBFINISH（群发回调禁回复）必须由平台层强制，不依赖各消费方自觉。
- 多公众号（多 channelId）场景——不同公众号需要不同回复策略（W3 绑定缝）。
- 开发期对接验证——无需先写业务 handler 即可验证"微信 → 验签 → 二级分发 → 各分支"全链路（探针）。

### 现有方案的不足

- `IEventReplyHandler` 裸接口 + `EventReplyHandlerBase`（主包，签票→图文高内聚）：平台无关，**不区分 MsgType/Event**（任何 wechat_event 一律同一回复路径）——无法支持按消息类型分流的业务（G1）。
- `TestEventReplyHandler`（模板驱动）：所有事件一律回同一图文链接，无法区分分支；且模板反序列化缺陷致功能实际不可用。
- `WeChatEventChannel` 自迭代 handler + 恒返回非 null：W3 绑定桶不可达（G4）、迭代权分裂（G5）、信封在通道（G6）——通道职责越界。
- 平台库"不注册 handler 防过度设计"禁令被误读为"不提供类型"——实际须区分**提供基类/默认类型**（消费方注册，合规）vs **注册 handler 实例**（越界）。

## 使用场景

### 场景清单

| 场景 | 使用方式 |
|---|---|
| 业务 handler 按消息类型分流（菜单/文本/关注/扫码...） | 继承 `WechatEventReplyHandlerBase`，Override `OnClickAsync`/`OnTextAsync`/`OnSubscribeAsync`/`OnScanAsync` 等关心的分支（模板方法）；其余分支默认 null 不回复 |
| 纯模板驱动零代码（多公众号统一默认回复） | 消费方 `TryAddEnumerableConstructible<IEventReplyHandler, WechatTemplateEventReplyHandler>()` + `Channel.Extra["news_reply"]` 模板配置（title/description/link/pic_url/ttl_seconds/enabled） |
| 开发期全链路验证（不分业务场景） | `ProbeEventReplyHandlerEnabled=true`（`TKWF:Federation:WeChat:EventReply` 节）→ 探针注册 → 发消息/点菜单 → 收到 `PROBE:branch=...` 可区分回复（显式分支走通）；门控分支经 ILogger `PROBE branch=gated swallowed` 验证 |
| 协议门控（unsubscribe/MASSSENDJOBFINISH） | 平台基类回复组装层强制 null——业务经 `OnUnsubscribeAsync` 等虚方法做副作用（记录取关）但回复被吞；任何 handler（含 Template/Probe）无法回复 |
| 特定 channelId 定制（多公众号场景） | 实现 `IChannelSpecificEventReplyHandler`（声明 ChannelId）——编排器绑定桶优先（G4 修复后微信可达） |
| 需要签密封票据的"事件→登录链接" | 选用主包 `EventReplyHandlerBase`（签票专用基类）或经 `User.Use<ISealedTicketService<FederatedIdentity>>()` 懒加载签票——平台基类无签票耦合 |

### 不适用边界

- **非事件通道平台（QQ/Alipay/OIDC/Google/Microsoft）**：无入站事件推送/被动回复——不适用（本方案仅微信需要完整形态）。
- **WeCom/DingTalk 事件回调**：无被动回复语义（回调只做增量/解绑通知，官方不保证 100% 送达）——走 DIM null + 编排器双桶即可，**不需要** WechatEventReplyHandlerBase 层。
- **纯签票图文场景（无二级分发需求）**：继续用主包 `EventReplyHandlerBase`（既有，保留不动）。
- **被动回复 5 秒超时之外的异步能力**（客服消息/异步通知）：归 Phase 2/消费方——本方案只保证 5 秒内空回复兜底（端点层 200 空回复）。
- **明文模式 POST 推送**：通道仍要求 `msg_encrypt`（安全/兼容模式）；明文模式未被本方案纳入（既有 4.10.80 适配核对结论——非本迭代范围）。

---

## 关键决策记录

| # | 决策 | 理由 |
|---|------|------|
| D1 | 平台基类 **不继承** `EventReplyHandlerBase`（两基类**并列**） | Oracle P1-1——主包基类 = 签票→图文（平台无关）；平台基类 = 二级分发 + XML 信封（微信特定）。继承会形同虚设（override HandleAsync 绕过签票流程），且签票能力可经 `User.Use<ISealedTicketService>` 自行调用 |
| D2 | 协议门控在**回复组装层**强制 null（虚方法仍执行） | 业务副作用（记录取关）可做，回复被吞——比"分派层短路"更优（O2 设计取舍）；OrdinalIgnoreCase 匹配（Oracle P2-1） |
| D3 | 模板缺失/损坏 → null（**不回退内置文案**） | Oracle P0-2——对齐"不回复不拦截"语义；内置文案作模板配置缺省值而非降级 |
| D4 | `WeChatEventChannel.HandleEventAsync` 返回 null（正常路径）+ 异常返回空哨兵 | Oracle P2-3——正常 null 转 handler 双桶；异常哨兵（非 null）区分"解密失败"与"无平台回复"，避免 handler 收到不完整 context |
| D5 | Template handler **不默认注册**（消费方显式）；Probe **无条件注册** + IOptions 惰性判断 | Oracle P0-1/P1-3——平台库不注册 handler 实例（防过度设计）；Probe 因开关门控（默认关 fail-safe）无条件注册由内部开关短路 |
| D6 | Parse 数据源 = 通道富化 `__decrypted_xml`（解密后明文） | Oracle P1-4——`__raw` 是密文 body 无法解析未富化字段；通道存明文 XML 供平台基类一次性解析 |
| D7 | `TestEventReplyHandler` 直接删除（不保留过渡）+ 模板键常量 `NewsReplyTemplateKey` 持平台层 | 用户裁定 2026-10-17；Oracle P2-2 常量承接防编译破坏 |
| D8 | `[JsonPropertyName]` 显式映射 snake_case 模板键 | 2026-10-17 测试发现缺陷修复——STJ 默认大小写敏感 + snake_case 不匹配致模板恒失败 |

---

## 验收（方案 §九 摘要）

- [x] 平台基类 + Template + Probe 落地，二级分发分支全集（8 事件 + 7 消息 + 兜底）实现完整（测试 52 用例实证）
- [x] 协议门控（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）强制 null——任何 handler 无法回复（测试实证）
- [x] `WeChatEventChannel.HandleEventAsync` 返回 null + `__decrypted_xml` 富化 + 异常哨兵——W3 双桶对微信生产可达（真实通道 null + 编排器双桶 + W3 绑定三件套实证）
- [x] Template 不默认注册（消费方显式）；Probe 无条件注册 + IOptions 门控（开关关全分支 null）
- [x] 主包 `TestEventReplyHandler` 删除 + 开关移除（零残留 grep）
- [x] `IEventReplyHandler`/`EventReplyHandlerBase` 契约零变化；平台基类不继承主包基类
- [x] slnx 构建 0 错误 + 受影响面测试全绿（Federation.Tests 79 + WeChat.Tests 86）

---

## 生命周期注记

- 本 ADR **废弃** `ADR-Federation-事件回复handler基类与内置测试设施.md`（2026-10-15）——其 `EventReplyHandlerBase`（保留）与 `TestEventReplyHandler`（删除）决策：前者被演进（平台基类并存），后者被替换（能力迁平台层 Template/Probe）。旧 ADR 不删除（AGENTS §4 生命周期——决策永久记录，本 ADR 为演进引用）。

<!-- EOF -->