# ADR-WeChatOperations-回复模板与通道级Handler

> **版本**：v0.2（Oracle 评审 PASS WITH CONDITIONS 意见吸收——P0×1 选项 A + 新需求全消息类型回复/订阅统计）
> **状态**：草稿（Oracle 评审已完成，P0 已吸收——待实施）
> **关联**：开发方案 `docs/WeChatOperations/微信公众号运营扩展-开发方案.md`（业务扩展 §3.3）；`ADR-Federation.WeChat-菜单出站API与凭证解析可插拔.md`（平台库增强——template 多图文升级 D4）；`ADR-Federation-平台特定事件回复Handler基类与二级分发.md`（事件回复底座——`WechatEventReplyHandlerBase` 二级分发分支全集）
> **Oracle 评审**：PASS WITH CONDITIONS（2026-10-17）——P0×1（handler 桶位/优先级 → 选项 A 内部组合 template 兜底）+ P1×7 已吸收
> **日期**：2026-10-17

---

## 目的与目标

为运营扩展（`TKWF.Ext.WeChatOperations`）定义**消息运营回复模型**：① **template 默认 + 可配置**——`WeChatOperationsReplyHandler` **内部组合**平台层 template 逻辑（`Channel.Extra["news_reply"]`）兜底，DB 规则未配置时生效，不依赖编排器迭代顺序（Oracle P0 选项 A）；② **图文 pattern 多图文**——素材库承载图文四件套（标题/正文/链接/图片地址），多图文 = 1..8 篇数组，经 `BuildNewsReply` 出微信图文信封；③ **全消息类型回复规则**——通用 `ReplyRuleEntity`（SceneType 覆盖 subscribe/keyword/click/image/voice/video/shortvideo/location/link/scan/default），满足"各种消息的回复配置"需求；④ **订阅统计**——subscribe/unsubscribe 事件驱动计数（按 channel 按日），满足"统计（订阅、取消订阅）"需求；⑤ **通道级 handler 编译期静态绑定**——不同通道（公众号）允许配置不同 handler，但**不走动态加载**（DB 不存类型名，绕过编译期门控风险）；⑥ **secret 加密落库**——公众号凭证 AES-GCM 密文入 DB，明文不入库不进日志。读者 3 句话内明白：回复逻辑 = 通用规则表（全消息类型）驱动素材渲染，无 DB 规则时 handler 内部 template 兜底；多通道差异经编译期 `IChannelSpecificEventReplyHandler` 静态绑定，DB 只存数据不存代码；凭证密文落库密钥分离。

## 问题

### 问题现象

1. **消息运营回复无数据底座**：欢迎语（subscribe）/ 关键词回复（text）/ Click 菜单 Key 映射 / 其他消息类型回复 / 素材库——无实体模型，运营无法配置。
2. **template handler 仅单图文**：需求"图文 pattern + 支持多图文"——现状 `WechatTemplateEventReplyHandler` 构造 `News: [单篇]`。
3. **handler 桶位/优先级未定义**（Oracle P0）：`WeChatOperationsReplyHandler` 与 `WechatTemplateEventReplyHandler` 若同在 ChannelType 桶，同桶迭代顺序未定义——template handler 先迭代并返回非 null 时运营扩展不被调用，违反"DB 规则优先"语义。
4. **不同通道配置不同 handler 的需求 vs 动态加载红线**：多公众号场景，不同公众号需要不同回复业务逻辑——若走"DB 存类型名 → 运行时反射实例化"（动态加载）则存在合规性问题（绕过 SG1/PAGE/DI 编译期静态门控——P7 缺省硬失败语义被静默绕过）。
5. **订阅/取关无统计**：需求"统计（订阅、取消订阅）"——无计数数据底座。
6. **secret 入 DB 的泄露面**：多公众号运营场景凭证自然归宿是 DB（Options 只适合静态少数账号），但明文入库即泄露面。

### 触发场景

- 运营配置欢迎语（subscribe 事件回复内容——文字/图文）。
- 运营配置关键词回复（完全/包含匹配 + 默认兜底；开启开发模式后微信后台关键词自动回复失效，必须自建）。
- 运营配置 Click 菜单 Key→回复内容映射（**独立于菜单结构——修改映射不需要重新发布公众号菜单**，不调用 menu/create）。
- 运营配置**其他消息类型回复**（image/voice/video/shortvideo/location/link 消息 + SCAN 扫码——"各种消息的配置"）。
- 运营查看订阅/取关统计（按 channel 按日趋势——"统计（订阅、取消订阅）"）。
- 运营维护素材库（文本/图文——被欢迎语/关键词/Click 复用）。
- 多公众号（多通道）差异化回复策略。

### 现有方案的不足

- `WechatTemplateEventReplyHandler` 单图文 + 模板在 `Channel.Extra["news_reply"]`（通道级静态配置）——无 DB 规则驱动、无多图文、无按 channel 的规则表。
- 裸 `WechatEventReplyHandlerBase` 继承（消费方自建 handler）——每消费方重复实现欢迎语/关键词/Click 逻辑，且规则数据无落库。
- 双 handler 独立注册依赖编排器迭代顺序（Oracle P0 反模式）——同桶迭代顺序未定义，优先级不可靠。
- 专用规则实体（WelcomeConfig/KeywordRule/ClickReplyMap 三表）——无法覆盖"各种消息的回复配置"（image/voice 等无对应实体）。
- 动态加载（DB 存类型名 → 反射实例化）——绕过 SG1 编译期门控（PAGE001-004/DI001-004 均为编译期静态检查），且合规性风险（运行时装配不可审计、不可静态验证）。

---

## 使用场景

### 场景清单

| 场景 | 使用方式 |
|---|---|
| 关注欢迎语（subscribe） | `WeChatOperationsReplyHandler.OnSubscribeAsync` → 读 `ReplyRuleEntity`（ChannelId + SceneType=Subscribe）→ 素材（文本 `BuildTextReply` / 图文 `BuildNewsReply` 多图文）；**同时记 SubscriptionStat（订阅 +1）**；无规则 → 内部 template 兜底 |
| 关键词回复（text） | `OnTextAsync` → 读 `ReplyRuleEntity`（ChannelId + Keyword + TriggerKey）→ 完全匹配优先 → 包含匹配（MatchMode）→ **未命中 → Default 兜底规则** → 无规则 → 内部 template 兜底 |
| Click 菜单映射 | `OnClickAsync` → 读 `ReplyRuleEntity`（ChannelId + Click + TriggerKey=EventKey）→ 素材回复；未映射 → null（不回复） |
| 其他消息类型回复 | `OnImageAsync`/`OnVoiceAsync`/`OnVideoAsync`/`OnShortVideoAsync`/`OnLocationAsync`/`OnLinkAsync` → 读 `ReplyRuleEntity`（对应 SceneType）→ 回复；无规则 → template 兜底 |
| 扫码回复 | `OnScanAsync` → 读 `ReplyRuleEntity`（ChannelId + Scan，可含 TriggerKey=场景值）→ 回复；无规则 → null |
| 订阅/取关统计 | `OnSubscribeAsync`/`OnUnsubscribeAsync` → `ISubscriptionStatService.RecordAsync(channelId, eventType)`（每日 upsert）→ 趋势查询 |
| 无 DB 规则（channel 未配置） | `WeChatOperationsReplyHandler` **内部**读 `Channel.Extra["news_reply"]` template 兜底（Oracle P0 选项 A——不依赖编排器迭代顺序）；`WechatTemplateEventReplyHandler` 仅供未启用运营扩展的场景 |
| 特定 channel 差异化业务逻辑 | 消费方编译期注册 `IChannelSpecificEventReplyHandler`（W3 绑定桶，声明 ChannelId 常量）——**编译期静态绑定** |
| 素材库 | `MaterialEntity`（文本/图文 pattern——全局共享，规则引用）——欢迎语/关键词/Click 按 MaterialId 复用 |
| 凭证落库 | `WeChatChannelConfigEntity` AppSecret/Token/EncodingAESKey AES-GCM 密文（keyed `ISymmetricKeyProvider`）——解密仅出站瞬时 |

### 不适用边界

- **非事件通道平台（QQ/Alipay/OIDC/Google/Microsoft）**：无被动回复语义——不适用。
- **纯签票图文场景**：主包 `EventReplyHandlerBase`（既有，保留）——运营扩展不涉签票（消息运营回复，无登录链接需求时）。
- **异步能力**（客服消息/异步通知）：被动回复 5 秒超时之外——归消费方/二期。
- **动态加载 handler**（DB 存类型名 → 反射实例化）：**本 ADR 明确禁止**——不适用边界即红线。
- **VIEW/VIEW_MINIPROGRAM 菜单事件**：微信直接跳转，无回复——**不做回复规则**（OnViewAsync/OnViewMiniProgramAsync 默认 null）。

---

## 关键决策记录

| # | 决策 | 理由 |
|---|------|------|
| D1 | **`WeChatOperationsReplyHandler` 内部组合 template 兜底**（Oracle P0 选项 A） | Oracle P0 修正——双 handler 独立注册依赖编排器迭代顺序不可靠（同桶顺序未定义，template 先迭代则运营扩展不被调用）；内部组合 = 读 `Channel.Extra["news_reply"]` 复用平台基类 `ReadNewsTemplate`/`BuildNewsReply`，**不依赖迭代顺序**，消费方无需额外注册 template handler |
| D2 | **图文 pattern 多图文**：素材库字段 = Title/Description/Url/PicUrl（微信图文回复 XML 四件套），多图文 = 组表/JSON 数组 1..8 篇经 `BuildNewsReply` | 需求"需要提供图文消息的 pattern，例如标题、正文、链接、图片地址等——支持多图文"；平台库 V0.4.1 已升级 template 多图文（ADR-Federation.WeChat D4），运营扩展复用 |
| D3 | **通用 `ReplyRuleEntity` 规则表**（SceneType 全消息类型）替代专用三实体（WelcomeConfig/KeywordRule/ClickReplyMap） | 需求"各种消息的回复配置"——image/voice/video/shortvideo/location/link/scan 无对应专用实体；通用表 + TriggerKey（关键词/菜单 key/场景值）覆盖全场景；Click 映射独立于菜单结构语义保留（SceneType=Click 修改不触发 menu/create） |
| D4 | **订阅统计**：`SubscriptionStatEntity` 按 channel 按日 upsert，subscribe/unsubscribe 事件驱动 | 需求"统计（订阅、取消订阅）"——`OnSubscribeAsync`/`OnUnsubscribeAsync` 副作用记录（取关回复被门控吞掉但统计可做） |
| D5 | **通道级 handler 编译期静态绑定，不走动态加载** | 用户裁定 2026-10-17（动态加载存在合规性 + 绕过静态编译门控风险）；DB 只存规则/素材数据，不存类型名/程序集名；差异化 = 消费方编译期实现 `IChannelSpecificEventReplyHandler`（W3 绑定桶）并注册 |
| D6 | **secret 加密落库**（AES-GCM 单段密文 + keyed `ISymmetricKeyProvider` 密钥文件分离） | 用户裁定"secret 可以入 db，能否通过加密解决安全隐患？"——能：密文落库 + 密钥文件分离，明文不入库不进日志；对齐 AuthSurface/TrustCenter/AuthCenter 三重既有先例 |
| D7 | **素材全局共享**（Material 无 ChannelId，规则按 MaterialId 引用） | 需求"素材可被欢迎语、关键词回复、click 菜单回复复用"——全局共享 + 规则引用（一个素材多规则复用）；不按 channel 归属 |
| D8 | **Click 映射独立于菜单结构**（SceneType=Click 规则，改映射不发布菜单） | 需求"❗重要区分"：菜单编辑器控制按钮结构（menu/create 全量发布）；Click 映射控制点击返回内容（独立模块，不调用微信菜单 API）——两套独立实体/端点 |

---

## 验收（方案 §九 摘要）

- [ ] `WeChatOperationsReplyHandler`：subscribe→欢迎语（+订阅计数）/ unsubscribe→取关计数 / text→关键词（完全/包含/Default 兜底）/ click→映射 / image/voice/video/shortvideo/location/link→对应规则 / scan→场景规则 / view→null / channel 过滤——测试全绿
- [ ] **handler 内部 template 兜底**（无 DB 规则 → Channel.Extra 模板，不依赖编排器迭代顺序）——Oracle P0 验收
- [ ] 不走动态加载：DB 无 handler 类型名，全部编译期静态注册（grep 零命中反射实例化）
- [ ] 凭证 AES-GCM 密文落库 + 解密仅出站瞬时（明文不落库/日志）+ DTO 投影不含凭证 + 出站日志脱敏 + 异常消息无明文——安全红线测试断言
- [ ] 订阅统计：subscribe/unsubscribe 事件驱动计数 + 按日趋势查询
- [ ] Click 映射修改不触发菜单发布（SceneType=Click 规则 + 独立端点测试）
- [ ] 素材全局共享：一个素材被欢迎语/关键词/Click 多规则引用
- [ ] slnx 构建 0 错误 + 受影响面测试全绿

---

## 生命周期注记

- 本 ADR 为运营扩展（WeChatOperations）消息运营回复模型记录——平台库事件回复底座 ADR 保持活跃（`WechatEventReplyHandlerBase` 二级分发不变）。
- 若未来开放动态加载（第三方程式化/插件化），须新 ADR 废弃 D5 并论证门控绕过风险已被替代方案消解。

<!-- EOF -->
