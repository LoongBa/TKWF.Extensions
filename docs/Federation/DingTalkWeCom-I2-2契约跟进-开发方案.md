# DingTalk/WeCom I2-2 契约跟进 开发方案（v0.2，C 档立项）

> **状态**：C 档立项（方案文档——只写方案，禁止写业务代码；实施另开迭代）。**v0.2（2026-11-09 修订）：oracle-pro1 评审 PASS WITH CONDITIONS（2 P0 / 5 P1 / P2）条件逐条闭环入方案正文**——P0-1/P0-2（钉钉协议事实重订：`encrypt` 双键 + 加密应答信封）+ P1-1~P1-5 闭环 + P2 三要点登记，见 §3.1/§3.2/§3.5/§3.6/§4/§6/§7/§10/§11 与变更记录。
> **依据**：`_TKWF/docs/03_扩展模块/阶段性评审/扩展模块-阶段性评审记录-2026-11-08.md` 批次 9（DingTalk+WeCom 合体，oracle-pro3 **PASS WITH CONDITIONS**）——**DT-1 / WC-1 / WC-2 三条 P1**（I2-2 契约跟进缺口）本立项收口；DT-2~DT-6 / WC-3~WC-8 等 A/B 档问题不在本方案范围。
> **评审问题原文**（评审记录 L580-582）：DT-1 = `DingTalkEventChannel`（未实现 HandleEventAsync）——I2-2 契约跟进缺口：DIM NotApplicable → FederationWebExtension 装配链事件编排跳过（uid 映射/handler 回复全无），README §四"编排层映射"矛盾；WeChatEventChannel 已跟进。WC-1 = 同 DT-1（WeComEventChannel）。WC-2 = WeComEventChannel（未实现 VerifyUrlAsync）——echostr URL 验证握手在装配层失效：VerifyUrlAsync DIM null → 400 `WECHAT_URL_VERIFY_FAILED`；README"归装配层"声称与实际（装配层调 `channel.VerifyUrlAsync` 非直接调 Crypto）矛盾。
> **拟版本**：`Federation.DingTalk` **V0.4.2**（0.4.1 → patch：仅补契约方法实现，零公共 API 破坏）· `Federation.WeCom` **V0.4.2**（同，patch）· 如需装配层门扩展（§3.6）：`Federation` 主包 patch bump（V0.18.0 → **V0.18.1**）联动——先决于两库 tag。
> **前置**：I2 语义层契约（`ChannelEvent`/`ChannelEventOutcome`/`ChannelCapabilities`/`HandleEventAsync`/`VerifyUrlAsync`）已随 `Federation.Abstractions` V0.2.0+ 就位（两库已改引 FA V0.4.1）；微信先例 `WeChatEventChannel.HandleEventAsync`/`VerifyUrlAsync` 已落地（V0.6.0 I2-7）。
> **关键边界（v0.2 评审闭环修订）**：`AuthenticateAsync` **语义保持**（验签/解密/receiveid/身份提取主体零变化；仅两处最小修订闭环评审条件——密文参数名 `encrypt` 主/双键兼容（P0-1）与 check_url/组织事件无身份**放行**（P1-2，§3.6））；**不新造** crypto 原语（复用两库 `{平台}EventCrypto` 既有闸门；钉钉应答信封复用既有 `EncryptMsg` 加密能力 + `ComputeSignature`，P0-2）；**不动** Federation 编排器契约（`EventOrchestrator` 双桶迭代/R3 记录在后零变更）。

---

## 〇、摘要（一句话 + 三问）

- **目的与目标**：补齐 DingTalk/WeCom 两平台事件通道的 I2-2 契约实现——`HandleEventAsync` 从 DIM 默认 `NotApplicable` 提升为真实解析产出 `ChannelEventOutcome.Parsed(ChannelEvent)`（验签→解密→归一），`VerifyUrlAsync` 从 DIM null 提升为可应答的 URL 验证握手（**钉钉 = 按官方规则加密的应答信封**——check_url 与业务事件统一回密文回执，非明文 success，P0-2/P1-1）；同步补两库 `ChannelSource.Capabilities` 事件相关能力声明（DIM None → `Event`），使 Federation 装配层事件编排链（uid 映射 / `IEventReplyHandler` 双桶迭代 / 关注域记录）对两平台**可达**。目标状态：微信先例形态 × 两平台协议（钉钉 JSON——密文参数名 **`encrypt` 主、`msg_encrypt` 兼容别名**（P0-1）/ 企业微信 XML），消费方零适配。
- **问题**：两库 `ISsoChannel` 契约方法缺实现 → 装配层 `FederationWebExtension.HandleEvent` 照常分派，但 channel 侧 DIM 默认（`NotApplicable`/null）导致：① 事件编排链静默跳过（`EventOrchestrator` 从 `HandleEventAsync` 拿不到 `ChannelEvent`——uid 只读映射、handler 第一非空回复双桶、关注域记录全不达），与两库 README §四「编排层映射」声明矛盾；② WeCom echostr URL 验证握手失效（装配层调 `channel.VerifyUrlAsync` 得 null → 400 `WECHAT_URL_VERIFY_FAILED`，企业微信后台保存回调 URL 必失败）；③ DingTalk 回调 URL 校验（`check_url` 事件）同样不可达（当前 `AuthenticateAsync` 对无 `FromUserId` 的 `check_url` 事件抛解密失败 → 400）。
- **使用场景**：DingTalk/WeCom 事件订阅推送实机入站（DingTalk `dingtalk_event`——扫码/组织变更/check_url；WeCom `wecom_event`——通讯录变更/成员关注事件）；WeCom 回调 URL 后台配置校验（GET echostr 握手）；涉及消费方装配 FederationWebExtension + 对应平台库（认证中心完整形态；纯外部联邦登录 BYO IdP）。**不适用边界**：非事件平台库（QQ/Alipay/Oidc 系——出站-only，无此契约）；DingTalk Stream Mode（长连接——另立项）；事件业务处理与被动回复内容（归 handler 层/消费方——**钉钉例外：协议不承载业务自由文本回执，恒回加密 success 信封、handler 回复被忽略**，见 §3.6/P1-1）。**披露（P1-2 放行设计）**：钉钉组织变更等**无 FromUserId 事件**实机回执可达（200 加密信封、不再 400），但进编排后 `HandleEventAsync` 返回 NotApplicable（无身份不可 handler 编排）——「钉钉端到端可编排」= 有 FromUserId 事件（SCAN 等）+ check_url；组织变更类 = 回执可达、编排跳过。

---

## 一、现状与缺口（评审证据索引）

### 1.1 评审问题完整描述

| # | 级别 | 位置 | 问题 | 建议（评审） |
|---|:---:|------|------|------|
| DT-1 | P1 | `DingTalkEventChannel.cs`（未实现 HandleEventAsync） | I2-2 契约跟进缺口：DIM NotApplicable → FederationWebExtension 装配链事件编排跳过（uid 映射/handler 回复全无），README §四"编排层映射"矛盾；WeChatEventChannel 已跟进 | 实现 HandleEventAsync → Parsed（C 档立项） |
| WC-1 | P1 | `WeComEventChannel.cs`（未实现 HandleEventAsync） | 同 DT-1 | 同（C 档） |
| WC-2 | P1 | `WeComEventChannel.cs`（未实现 VerifyUrlAsync） | echostr URL 验证握手在装配层失效：VerifyUrlAsync DIM null → 400 `WECHAT_URL_VERIFY_FAILED`；README"归装配层"声称与实际（装配层调 `channel.VerifyUrlAsync` 非直接调 Crypto）矛盾 | 实现 VerifyUrlAsync 委托 `WeComEventCrypto.VerifyAndDecryptEcho`（与 WC-1 合并立项） |

### 1.2 源码事实（现状证据）

| 事实 | 位置 / 证据 | 对方案影响 |
|---|---|---|
| `DingTalkEventChannel` 仅实现 `AuthenticateAsync`（验签+解密+receiveid 校验+取 `FromUserId`），**无** `HandleEventAsync`/`VerifyUrlAsync` 覆写 → 落入契约 DIM 默认（NotApplicable / null） | `_Framework/Federation.DingTalk/DingTalkEventChannel.cs:49-93`（全文件 94 行，仅 ctor/ChannelType/ChannelId/AuthenticateAsync） | 本次补两方法（§3.1/§3.2） |
| `WeComEventChannel` 同上——仅 `AuthenticateAsync`（三道闸门 + `ExtractFromXml` 双策略），无两契约覆写 | `_Framework/Federation.WeCom/WeComEventChannel.cs:52-106`（全文件 107 行） | 本次补两方法（§3.3/§3.4） |
| 契约 DIM 默认实现与语义：`HandleEventAsync` DIM `NotApplicable`（非事件通道零强制，编排跳过）；`VerifyUrlAsync` DIM null（无 URL 验证契约 → 装配层映射 400）；`ChannelCapabilities` DIM `None` = 刻意 fail-closed | `_Framework/Federation.Abstractions/ISsoChannel.cs:85-107`；`ChannelCapabilities.cs:8-21` | 方法与能力声明双补（§3.5） |
| 装配层事件端点分派：GET+`echostr` → `VerifyUrlAsync` 回显（null → 400 `WECHAT_URL_VERIFY_FAILED`）；否则 `AuthenticateAsync` → `OrchestrateAsync` → reply/200 空；参数包 = `__raw`(body) + `channel_id` + query 透传 + 服务端可信 `base_url` | `_Framework/Federation/FederationWebExtension.cs:230-291`（端点构造 + 分派；echostr 门 L268） | WeCom 零装配层改动（query 有 echostr）；**DingTalk check_url 需要装配层门扩展**（§3.6） |
| `DingTalkEventCrypto` 已有原语：`VerifySignature`（一票否决）/`DecryptMsg`（含 receiveid=corpId 校验）/`ExtractFromUserId`/`ComputeSignature`/`EncryptMsg`（L169-205——**真实 AES-256-CBC 加密能力**（与 `DecryptMsg` 严格对称），既有注释标"测试辅助"——v0.2 提升为应答信封生产原语，见 §3.2）——**无** echostr/check_url 专用原语 | `_Framework/Federation.DingTalk/DingTalkEventCrypto.cs:48-163`（EncryptMsg:169-205） | 不需要新原语；check_url/业务事件应答信封在通道层复用既有两原语构造（§3.2，P0-2） |
| `WeComEventCrypto` 已有 `VerifyAndDecryptEcho`（URL 验证握手原语——验签失败 null，解密失败抛）——正是 WC-2 建议委托目标 | `_Framework/Federation.WeCom/WeComEventCrypto.cs:152-162` | 直接委托（§3.4） |
| 微信先例：`WeChatEventChannel.HandleEventAsync`（双模式验签→解密→提取 openId/MsgType/Event/Content/CreateTime/MsgId→EventType 归一→`wechat.` 前缀 Attributes→`OccurredAt` UTC→`RawPayload` 明文 XML→`Parsed`；验签/解密失败→`Rejected`；缺 openid→`NotApplicable`；日志脱敏）+ `VerifyUrlAsync`（安全 4 参验签+AES 解密回显 / 明文 3 参原样回显；一切失败→null） | `_Framework/Federation.WeChat/WeChatEventChannel.cs:219-444` | 本方案对齐形态（§三） |
| 两库 ChannelSource 均未覆写 `IChannelSource.Capabilities` → DIM None → 编排层能力门控 fail-closed（关注域不记录；但**能力门不挡 handler 派发**——`EventOrchestrator` 双桶迭代以 `ChannelEvent.ChannelType` 过滤） | `_Framework/Federation.DingTalk/DingTalkChannelSource.cs:32-49`；`Federation.WeCom/WeComChannelSource.cs:35-56` | Capabilities 补 `Event`（§3.5）；微信先例 `Event|Follow|FollowQuery|Menu(+SilentAuthorize)`（`WeChatChannelSource.cs:36-44`） |
| DIM 默认值已被测试显式锚定（防未来破坏）——本轮实现后 `FederationContractDimTests` 的 DIM 断言**不受影响**（其 `DefaultChannel` 为独立最小覆写类，非两库通道） | `_Tests/Extension.Federation.Tests/FederationContractDimTests.cs:26-41` | 无测试改写诉求；实现行为由两库各自新用例锁定（§六） |
| 两库事件样本格式（测试现有）：钉钉 JSON `{"EventType":"SCAN","FromUserId":...,"CorpId":...,"CreateAt":1720000000}`；企业微信 XML `<xml><ToUserName>..<FromUserName><CreateTime>..<MsgType>event</MsgType><Event>change_contact</Event><ChangeType>update_user</ChangeType>...</xml>` | `_Tests/Extension.Federation.DingTalk.Tests/DingTalkEventChannelTests.cs:42`；`_Tests/Extension.Federation.WeCom.Tests/WeComEventChannelTests.cs:45` | 事件产出用例复用同款构造（§六） |
| 两库 README 版本记录表已带 V0.4.1 行（头部 V0.4.1）；README §四「编排层映射」声明与实现脱节（本立项修复后一致） | 两库 `_Framework/Federation.{平台}/README.md` §四 | 文档同步（§八） |

### 1.3 缺口归纳（契约缺口清单）

| 契约方法 | DingTalk | WeCom | 缺口后果 |
|---|---|---|---|
| `HandleEventAsync → ChannelEventOutcome` | DIM `NotApplicable`（缺实现） | DIM `NotApplicable`（缺实现） | 事件编排链跳过：uid 只读映射不达、`IEventReplyHandler` 双桶不迭代、关注域（如需）不记录——实机事件入站只回 200 空 |
| `VerifyUrlAsync → string?` | DIM null（缺实现；钉钉 URL 校验为事件式 check_url，无 echostr query） | DIM null（缺实现；echostr 原语已存在） | WeCom：后台保存回调 URL → 400 `WECHAT_URL_VERIFY_FAILED` 必败；DingTalk：check_url 事件走 `AuthenticateAsync` 缺 `FromUserId` → 解密失败 400 |
| `ChannelSource.Capabilities` | DIM `None` | DIM `None` | 能力门 fail-closed（未声明 `Event`——能力面声明失实，关注域等消费方按能力表判断的平台库能力面为空） |

---

## 二、目标与边界

### 2.1 目标（对齐微信先例形态）

1. **DingTalk `HandleEventAsync`**：验签（一票否决）→ AES 解密（receiveid=corpId 二道闸门）→ 解析事件 JSON → 产出 `ChannelEventOutcome.Parsed(ChannelEvent)`（EventType 归一 + `dingtalk.` 前缀 Attributes + `OccurredAt` UTC + `RawPayload` 明文 JSON）；失败哨兵对齐微信（信任根/解密失败 → `Rejected`；无身份标识且非 check_url → `NotApplicable`）。
2. **DingTalk `VerifyUrlAsync`**：钉钉回调 URL 校验/事件回执应答（合法入站 → **加密 JSON 应答信封**——复用 `EncryptMsg("success")` + `ComputeSignature` 四字段构造，P0-2；不再回明文 success；验签/解密失败 → null）。
3. **WeCom `HandleEventAsync`**：同型——验签 → `DecryptAndVerify`（ReceiveId 二道闸门）→ 解析事件 XML → `Parsed(ChannelEvent)`（EventType 归一 + `wecom.` 前缀 Attributes + `OccurredAt` UTC + `RawPayload` 明文 XML）。
4. **WeCom `VerifyUrlAsync`**：GET echostr 握手——**委托 `WeComEventCrypto.VerifyAndDecryptEcho`**（验签 + AES 解密 echostr → 回显明文），失败 → null（装配层 400 语义不变）。
5. **Capabilities 声明**：两库 `ChannelSource.Capabilities` 补 `Event`（DingTalk 同时声明 `OAuth`——其双通道已具 OAuth 能力？见 §3.5 决策；WeCom 补 `Event|OAuth`——保守对齐两库双通道实际能力面）。
6. **装配层小改（仅 DingTalk check_url 可达需要）**：`FederationWebExtension.HandleEvent` 门扩展为「GET+echostr **或任意**入站先试 `VerifyUrlAsync`」——见 §3.6（以最小副作用实现对两平台 URL 验证的统一覆盖；WeChat 行为零变化）。

### 2.2 边界（不做）

- **不重构** `AuthenticateAsync`（钉钉/企微"事件回调换身份"路径语义保持——`AuthenticateAsync` 为 OAuth 语义外的既有入站通道；v0.2 两处最小修订闭环评审条件：密文参数名 `encrypt` 主/`msg_encrypt` 别名双键（P0-1）+ check_url/组织事件无身份**放行**（P1-2，不再 400）——其余行为零变化，见 §3.6）。
- **不新造 crypto 原语**：钉钉 check_url 判定仅需 `DingTalkEventCrypto.DecryptMsg` + JSON `EventType` 字段读取（在通道层内联）；企微直接委托既有 `VerifyAndDecryptEcho`。
- **不动** `EventOrchestrator`/`IEventReplyHandler`/`IEventOrchestrator` 契约与编排器实现（双桶/记录在后 R3 零变更——本立项只是让渠道侧"喂"出事件）。
- **不做** DingTalk/WeCom 平台级 `IEventReplyHandler` 基类与模板 handler（微信专有 `WechatEventReplyHandlerBase` 形态——两平台事件回复业务未立项，YAGNI；消费方可按 `IEventReplyHandler` 直接实现）。
- **不补** 关注域平台语义（钉钉/企微无"关注"概念——不声明 `Follow` 能力、不产出 follow/unfollow 归一；组织同步事件按"记事实不翻状态"原文透传）。
- **不处理** DT-2~DT-6 / WC-3~WC-8（A/B 档，另行立项）。

---

## 三、设计

### 3.0 通用守卫与哨兵（对齐 `WeChatEventChannel.cs:291-377` 形态）

两平台 `HandleEventAsync` 均按下述优先级返回（顺序即语义，注释对齐微信先例"可被直连调用——验签信任根须自持，纵深防御"）：

| 条件 | 返回 | 说明 |
|---|---|---|
| `_channel is null`（模板实例） | `ChannelEventOutcome.NotApplicable` | 集合类型索引源不解析（对齐现有 AuthenticateAsync 守卫） |
| `!_channel.IsEnabled` | `ChannelEventOutcome.NotApplicable` | 停用通道不解析（WeChat 先例同款） |
| Extra 缺信任根凭证（Token/EncodingAESKey[/CorpId 期望]） | `ChannelEventOutcome.Rejected` | 信任根配置缺失——绝不解析 |
| `context.Parameters is null` | `ChannelEventOutcome.Rejected` | 参数包缺失 |
| 验签参数缺失 / 验签失败（一票否决） | `ChannelEventOutcome.Rejected` | 绝不解密伪造密文 |
| 解密失败 / 格式非法（Exception catch，白名单排除取消） | `ChannelEventOutcome.Rejected` | 编排短路：不记录、不回复、不回退 handler |
| 解密成功但**无 FromUserId**（含 check_url——v0.2 起本方法不再承担 check_url：其由 VerifyUrlAsync 恒回信封应答 + 本守卫 NotApplicable 跳过编排，见 §3.2/§3.6） | `ChannelEventOutcome.NotApplicable` | 对齐微信「解析不出 openid → NotApplicable」先例 + 契约 L12-13（不造半个事件） |

> 注意对齐微信先例会**重复验签+解密链**（`WeChatEventChannel.cs:291-298` 注释——Oracle P2-2 记录的已知设计：端点已先 AuthenticateAsync 验签，契约方法仍自验——本方法可被直连调用，纵深防御；不可用"external_user_id 已存在即跳过验签"优化——存在直调伪造身份绕过信任根的漏洞）。两库同款保留。

### 3.1 DingTalk `HandleEventAsync` 实现细节

位置：`_Framework/Federation.DingTalk/DingTalkEventChannel.cs`（增两方法，类头注释补 I2-2 段）。

**签名/产出**：`public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)`——按 §3.0 守卫后：

1. **参数解析（P0-1 双键双位重订——钉钉官方事件订阅 HTTP 回调模式）**：官方密文参数名 = **`encrypt`**（body JSON 单键 `{"encrypt":"..."}`；验签参数 `signature`/`timestamp`/`nonce` 在 URL query）；兼容既有实现/旧文档的 `msg_encrypt` 别名（查询参数与 body JSON **双位探查**）：
   - 密文读取（query）：`encrypt` 优先，`msg_encrypt` 别名兜底；
   - 密文读取（body）：query 均无 → 从 `__raw`（body JSON——双键任一：`{"encrypt":"..."}` 或 `{"msg_encrypt":"..."}`）`JsonDocument` 提取（对齐 WeChat 先例 `ExtractEncrypt` 从 body 提取的同款补充语义：`WeChatEventChannel.cs:127-143`）；`__raw` 非 JSON → 保持参数缺失语义。
   - 签名键兼容：`signature` 优先（官方），`msg_signature` 别名兜底（与现有 `AuthenticateAsync` L67-68 同款）；`timestamp`/`nonce` 官方键名直取。
   - **验签/解密链路以官方签名字段口径为准**：`signature/timestamp/nonce + 密文原文` 四参字典序排序拼接 SHA1（`DingTalkEventCrypto.ComputeSignature` L31-37 同款——`VerifySignature` 内置恒定时间比对）。
2. **一票否决验签**：`DingTalkEventCrypto.VerifySignature(token, timestamp, nonce, msgEncrypt, signature)` 失败 → `Rejected`。
3. **二道闸门解密**：`DingTalkEventCrypto.DecryptMsg(encodingAesKey, msgEncrypt, corpId)`（receiveid=corpId 恒定时间比对内置）——异常 → `Rejected`。
4. **事件 JSON 解析**（新增库内私有解析——复用 `JsonDocument`；**不改** `ExtractFromUserId` 语义，单独读取顶层字段）：
   - `EventType`（顶层字符串）→ 归一 Type（§4.1 表）；
   - `FromUserId`（可空——check_url 事件无此字段）；
   - `CorpId`/`UserId`（事件体回显，可空——诊断/Attributes）；
   - `CreateAt`（秒级时间戳，可空）→ `OccurredAt = DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime`；空 → `DateTime.UtcNow`（对齐微信先例 L401-403）。
5. **产出 `ChannelEvent`**（对齐微信先例 L405-429）：
   ```csharp
   new ChannelEvent
   {
       ChannelId = _channel.ChannelId,
       ChannelType = "dingtalk_event",
       ExternalUserId = fromUserId!,             // ⚠️ 解析路径保证非空——空已在 §3.0 守卫返回 NotApplicable（契约 L12-13 不造半个事件；无 check_url 占位残留，P2-3 清理）
       EventType = normalizedType,          // §4.1 归一
       EventKey = null,                      // 钉钉事件无菜单 key/scene 概念
       Source = eventJson 层事件来源字段（如 "CorpId" 值，可空）,
       MessageId = null,                     // 钉钉事件订阅无 MsgId 概念
       OccurredAt = occurredAt,              // CreateAt 秒级 → UTC
       ReceivedAt = DateTime.UtcNow,
       RawPayload = json,                    // 解密后明文 JSON——handler/SyncProxy 透传数据源
       Attributes = new Dictionary<string, string?>() { ... } // §4.2 键表
   }
   ```
   > **ExternalUserId 取值决策（v0.2 定稿——P0-1/P0-2/P1-2 联动）**：`ChannelEvent.ExternalUserId` 契约标注"不可空——解析不出应 NotApplicable/Rejected"。**check_url 事件无 FromUserId**——v0.2 起 check_url 由 `VerifyUrlAsync` 恒回加密信封应答（§3.2），经装配层门扩展（§3.6）先于编排被处理；payload 编排路径上 check_url/组织事件（无 FromUserId）一律在 §3.0 守卫返 NotApplicable（不造半个事件——L12-13 语义）。本方法实现因此**统一**：`FromUserId` 空 → `NotApplicable`（不再区分 check_url/业务，无占位值分支）。
6. **日志**：入站 Information（ChannelId/EventType/FromUserId），PII 脱敏对齐 FW-09 先例（FromUserId 非 PII，但 Content 类字段不产——钉钉事件体仅诊断字段）。
7. **catch 白名单**：`when (ct.IsCancellationRequested is false)`（对齐两库现有 catch 模式）。

### 3.2 DingTalk `VerifyUrlAsync` 实现细节（check_url 应答——加密信封，P0-2 重订）

位置：同文件。**前提**：装配层门扩展（§3.6）使 POST 入站也先试本方法。

**签名/产出**：`public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)`——返回**加密应答信封 JSON**（check_url 与业务事件**同型**——钉钉官方要求所有回调以加密 JSON 应答信封回执，明文 `"success"` 会被判失败重试）或 null（验签/解密失败 / 缺参 / 模板 / 禁用 / 缺凭证——装配层 400 语义）。**不再返回明文 success（P0-2）**。

**应答信封构造（零新 crypto——复用既有加密原语）**：
- 信封明文 = `"success"`（钉钉官方回执语义）；
- `encrypt` = `DingTalkEventCrypto.EncryptMsg("success", encodingAesKey, corpId)`（L169-205）——**真实 AES-256-CBC 加密能力**（与 `DecryptMsg` 严格对称：`[16B random][4B 网络序 len][msg][receiveid]` + PKCS7），非仅测试辅助——实现时将库内其 XML 注释由"测试辅助"提升为"应答信封加密/事件回执"生产原语（签名、语义零变化，`internal` 可见性不变）；
- 信封 = JSON 四字段 `{"msg_signature": ComputeSignature(token, timeStamp, nonce, encrypt), "timeStamp": <本请求 timestamp>, "nonce": <本请求 nonce>, "encrypt": <encrypt>}`——**四字段名/大小写约定与响应 Content-Type 以钉钉官方回调文档为准**（字段别名兼容与否、`timeStamp` vs `timestamp` 等经实机封包核实——见 §十实机封包核实清单，P0-1/P0-2 风险登记）。

流程（守住信任根——可被直连调用）：

1. 守卫：`_channel` null / 禁用 / 缺 Token/EncodingAESKey/CorpId / Parameters null → null。
2. 参数：`timestamp`/`nonce`/`signature`（双键兼容）+ 密文 `encrypt`（query 或 `__raw` JSON body 提取——同 §3.1-1 双键双位探查）。
3. 一票否决验签 → 失败 null（绝不解密伪造密文）。
4. `DecryptMsg`（receiveid=corpId 二道闸门）→ JSON 解析 → **EventType 判定基于解密 JSON（P1-5）**：任意合法入站（check_url 或业务事件）→ 返回加密信封；验签/解密失败 / JSON 非法 → null。
5. 日志：通过 Information（ChannelId）/ 失败 Warning。

> 设计取舍（P0-2/P1-1/P1-2 联动，替代原"回显明文 success"方案）：`check_url` 经本方法恒回信封应答；**业务事件亦由本方法生产恒回信封**（§3.6 段 1 保存、段 4 原样回显——handler 回复被忽略，钉钉协议不承载业务自由文本，P1-1）；`AuthenticateAsync` 对 check_url 的既有失败行为（缺 FromUserId → `DINGTALK_EVENT_DECRYPT_FAILED` 400）经 **P1-2 放行设计**消除（无身份事件 → success(null) 放行，见 §3.6）——编排路径不再对 check_url/组织事件 400。

### 3.3 WeCom `HandleEventAsync` 实现细节

位置：`_Framework/Federation.WeCom/WeComEventChannel.cs`（增两方法，类头注释补 I2-2 段）。

**签名/产出**：同 §3.1 形态。流程：

1. **参数解析**（企微官方 URL query：`msg_signature`（兼容 `signature` 别名）/`timestamp`/`nonce`；密文 `msg_encrypt` query 或 `__raw` body XML `<Encrypt>` 提取——企业微信事件回调 body 为 XML `<xml><Encrypt>...</Encrypt></xml>`；对齐微信先例 `WeChatEventChannel.cs:127-143` 的 `ExtractEncrypt` 兜底提取模式：`__raw` 非 XML → 保持缺失语义）。
2. **一票否决验签**：`WeComEventCrypto.VerifySignature(token, timestamp, nonce, msgEncrypt, msgSignature)` 失败 → `Rejected`。
3. **二道闸门解密**：`WeComEventCrypto.DecryptAndVerify(encodingAesKey, msgEncrypt, corpId)`（ReceiveId=corpid/suiteid 恒定时间比对内置；**supports suiteid**——三方应用 IsThirdParty 时期望为 suiteid，取值同现有 `AuthenticateAsync` L60-64 的 Extra["CorpId"]；三方 suiteid 亦载于该键，见 `WeComChannelSource.cs:46`）——异常 → `Rejected`。
4. **事件 XML 解析**（新增库内私有解析，复用 `XDocument`；**不改** `ExtractFromXml` 语义）：
   - `Event`（如 `change_contact`）/ `ChangeType`（通讯录子类型：`create_user`/`update_user`/`delete_user`/`create_party`/...）/ `subscribe`/`unsubscribe`（成员关注应用事件）→ 归一 Type（§4.3 表）；
   - `UserID`/`OpenUserID`（external_uid 双策略同 `AuthenticateAsync` L90-97）——**事件产出 direct 用双策略值**（与 AuthenticateAsync 同款：三方 open_userid 优先 → 自建 `{CorpId}:{userid}` 复合）；
   - `FromUserName`（事件入口触发者，可空——关注事件为 OpenID 形态当注意：企业微信成员关注事件 `FromUserName` = 成员 openid/userid 语义与通讯录 `UserID` 不同——解析策略见 §4.3 注）；
   - `CreateTime`（秒级）→ `OccurredAt` UTC；空 → `UtcNow`。
5. **产出 `ChannelEvent`**：`ChannelType = "wecom_event"`、`RawPayload = xml`（解密后明文 XML）、Attributes 按 §4.4 键表。
6. 解密成功但身份标识（UserID/OpenUserID/FromUserName 三源）全空 → `NotApplicable`（契约 L12-13——不造半个事件）。
7. 日志 + catch 白名单：同 §3.1-6/-7。

### 3.4 WeCom `VerifyUrlAsync` 实现细节（echostr 握手——WC-2 直答）

位置：同文件。

**签名/产出**：`public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)`——**直接委托** `WeComEventCrypto.VerifyAndDecryptEcho`（WC-2 评审建议原文）：

1. 守卫：`_channel null` / 禁用 / 缺 Token/EncodingAESKey/CorpId → null。
2. 参数：`timestamp`/`nonce`/`echostr`/`msg_signature`（企微官方 GET 验证参数——无 signature 明文模式分支：企业微信**无**明文 3 参模式，文档以 msg_signature 唯一签名键；`signature` 别名兼容保留（对齐库内双命名惯例））——缺任一 → null。
3. `return WeComEventCrypto.VerifyAndDecryptEcho(token, timestamp, nonce, echostr, msgSignature, encodingAesKey, corpId)`——验签失败 null、解密/ReceiveId 校验失败抛（装配层 catch → 503 兜底，当前端点语义）；异常捕获归一 null 亦可（断言选择：对齐微信先例 catch 归一 null——解密失败回 null 让装配层 400，不产生 503 噪音；**记决策**：整体 try-catch 归一 null）。
4. 日志：通过 Information / 失败 Warning。

> 装配层零改动（`FederationWebExtension.cs:268` 的 echostr 门已存在）——WC-2 与 WC-1 同立项合并，本方法补全即闭环，200 text/plain 回显明文。

### 3.5 能力声明（Capabilities）

两库 `ChannelSource` 增 `Capabilities` 覆写（DIM None → 实际能力面；对齐微信先例 `WeChatChannelSource.cs:36-44`）：

| 平台 | 声明值 | 依据 |
|---|---|---|
| DingTalk | `ChannelCapabilities.OAuth \| ChannelCapabilities.Event` | 双通道（dingtalk_oauth/dingtalk_event）已实现 `AuthenticateAsync`（OAuth）与本立项 `HandleEventAsync`（Event）；**不声明** `Follow`/`FollowQuery`——钉钉无"关注"概念（组织变更非关注域语义）、无反查 API 立项 |
| WeCom | `ChannelCapabilities.OAuth \| ChannelCapabilities.Event` | 同型（wecom_oauth/wecom_event）；不声明 `Follow`/`FollowQuery`——企微关注应用事件属成员关系非"公众号关注"订阅语义、反查未立项（YAGNI） |

> 影响（P1-3 事实校正）：`EventOrchestrator.OrchestrateAsync`（`EventOrchestrator.cs:170-184`）**无条件调用 `FollowStateService.RecordAsync`——无能力门检查**（`ChannelCapabilities.cs:8-10` 注释的"记录前检查 Follow"在实际编排器实现中不存在）；`RecordAsync` 以**事件类型判据**区分（`FollowStateService.cs:229-230` `IsFollowTransition`：仅 follow/unfollow/scan_follow 翻状态，其余"记事实不翻状态"）。两平台不产 follow/unfollow 归一事件（WeCom subscribe 原文透传——P1-3 定案选项 b，见 §4.3）→ 无翻状态事件入关注域，状态聚合根零写入（`FollowEventEntity` 事实行仍落——追加写幂等）；`Event` 能力声明面向未来消费方能力表查询与审计（README §四能力面记录同步）。

### 3.6 装配层门扩展（Federation 主包小改——DingTalk check_url 可达 + 业务事件恒回信封，P1-1/P1-2/P1-5 闭环）

**问题**：`FederationWebExtension.HandleEvent` 现有 URL 验证门 = `echostr in parameters`（`FederationWebExtension.cs:268`）——DingTalk 回调为 POST（body JSON，无 echostr query），永不进 `VerifyUrlAsync` 路径；且钉钉协议要求**所有回调**（URL 校验与业务事件）以加密 JSON 应答信封回执——装配层现直接回 handler 回复 / 200 空，不符协议 → 钉钉判推送失败并重试（重试风暴 + handler 业务写重复执行风险，P1-1）。

**改法（最小副作用——分派顺序调整 + DingTalk 专用直达分支）**：

| 段 | 行为 | 说明 |
|---|---|---|
| 段 0（既有保留） | GET 入站且 `echostr in parameters` → `VerifyUrlAsync` → 回显（WeChat/WeCom——text/plain 明文 echostr；DingTalk 无此形态） | 语义不变，原快速门保留 |
| 段 1（新增——POST 也试 VerifyUrlAsync） | 非 GET/无 echostr → **先试 `channel.VerifyUrlAsync(new ChannelInboundContext(parameters), ct)`** | WeChat/WeCom：守卫 null（无 echostr）→ 段 2（行为零变化；每笔 POST 多一次方法调用——守卫秒退，P1-4）；**DingTalk：任意合法入站（check_url 或业务事件）→ 加密应答信封——保存供段 4 回显，不短路**；验签/解密失败/缺参 → null → 段 2（既有失败语义） |
| 段 2（既有保留 + P1-2 放行） | `AuthenticateAsync`——success → 段 3；成功但无 FromUserId（check_url/组织事件）经放行设计 success(null) → 段 3；验签级失败 → 400 既有语义 | 放行见下 |
| 段 3（既有保留） | `OrchestrateAsync`（uid 只读映射 + handler 双桶迭代 + R3 关注域记录在后零变更） | check_url / 组织事件 → `HandleEventAsync` → 无 FromUserId → NotApplicable → 编排跳过；有 FromUserId 业务事件（SCAN 等）→ Parsed → handler 副作用执行 |
| 段 4（新增——DingTalk 恒回信封，P1-1） | 编排后对 `cfg.PlatformType == "dingtalk"` **恒回段 1 保存的加密信封**（`Results.Text(envelope, contentType: "application/json")`——Content-Type 以钉钉官方规定为准，§3.2） | **handler 回复被忽略**——钉钉协议不承载业务自由文本：`IEventReplyHandler` 对 `dingtalk_event` 的回复内容文档声明"只做副作用，回复被忽略"（装配层实现选择 = **恒回信封**）；WeChat/WeCom 保持既有回显（handler reply / 200 空兜底） |

**放行设计（P1-2 闭环）**：`DingTalkEventChannel.AuthenticateAsync` 对**解密成功但无 FromUserId** 的入站（check_url / user_add_org 等组织事件——`ExtractFromUserId` L141-163 缺字段抛 `InvalidOperationException`）由既有「解密失败 400」改返回 **`SsoChannelAuthResult(true, null, null, 0)` 放行**（不再 400）——组织变更事件实机回执可达（200 加密信封，不再必败）。**披露**：无 FromUserId 事件进编排后 `HandleEventAsync` 返回 NotApplicable（无身份不可 handler 编排）——「钉钉端到端可编排」= 有 FromUserId 事件（SCAN 等）+ check_url；组织变更类 = 回执可达、编排跳过（记录在 §〇使用场景 + 本节）。`AuthenticateAsync` 其余语义（验签/解密/receiveid/身份提取）零变化；密文参数名双键兼容（`encrypt` 主/`msg_encrypt` 别名）随 P0-1 同步（其 L71 `msg_encrypt` 直取改双键双位探查）。**装配层 >1 通道选区语义核对**：段 0-4 全部基于**同一** `cfg.ChannelId → ISsoChannelFactory.CreateAsync` 选出的通道实例（`FederationWebExtension.cs:251` 同款）——门扩展不引入二次选区。

**开销声明（P1-5 校正，替代原"多一次解密——可接受"轻描淡写）**：每笔 DingTalk 业务事件经段 1 `VerifyUrlAsync` **多一次全链验签+解密**（段 1 三闸门 + 段 2 `AuthenticateAsync` 三闸门 + 段 3 `HandleEventAsync` 验签解密——CPU-only 无出站，单次 <50ms，链式 ~150ms 量级）；**性能边界**：事件订阅入站频率低（秒级~分钟级，非 API 热路径），相对钉钉 1200ms 硬约束余量充足，可接受。EventType 判定基于解密 JSON（段 1 内读取）：check_url → 恒回信封应答；业务事件 → 落回 `AuthenticateAsync`→`OrchestrateAsync` 编排路径（P1-5 分派语义）。

> 影响面：`FederationWebExtensionTests.cs` + `FederationContractDimTests`（DIM 断言零变化——`DefaultChannel` 独立最小类）。**行为变化清单**：WeChat/WeCom POST 增加一次 `VerifyUrlAsync` 调用（无 echostr → 守卫 null → 零行为变化——`Extension.Federation.WeChat.Tests` **141 用例全量回归锁定**，P1-4）；DingTalk POST check_url → 200 加密信封（P0-2 修复）；DingTalk POST 业务事件 → 200 恒回加密信封（handler 回复忽略——P1-1）；DingTalk 无身份组织事件 → 放行 200 信封（P1-2，原 400 消除）。
> **备选 B（不依赖主包改动）**：DingTalk `AuthenticateAsync` 特判 check_url 返回 `SsoChannelAuthResult(true, null, ...)` + `HandleEventAsync` 产出 check_url 事件 + 编排器回复加密信封——需信封构造进编排器契约（侵入编排语义——编排器应处理的是业务事件）——**否决**，选主包小改。

---

## 四、事件归一与 Attributes 键表

### 4.1 DingTalk `EventType` 归一（对 JSON `EventType` 字段）

> 原则（对齐微信先例 `NormalizeWechatEvent` 的"记事实不翻状态"分支——`WeChatEventChannel.cs:476-479`）：钉钉事件订阅语义 = 组织通讯录/扫码事实，无"关注"状态机——**EventType = 平台事件值原文透传**（原文归一仅做大小写保留 + 空值哨兵 "unknown"）。归一表：

| 平台事件值 | 归一 EventType | 说明 |
|---|---|---|
| `check_url` | `url_verify` | URL 验证（§3.2 VerifyUrlAsync 恒回信封应答 + §3.0 无 FromUserId → NotApplicable 短路——**本方法实际不产**；表内保留文档完整性，P2-1 标注避免实施误写分支） |
| `SCAN` | `scan` | 扫码成功（FromUserId 携带 openId） |
| `user_add_org`/`user_modify_org`/`user_leave_org` 等 | 原文透传 | 通讯录事实（不翻关注状态） |
| `org_dept_create`/`org_dept_modify`/`org_dept_remove` | 原文透传 | 部门事实 |
| `org_admin_add`/`org_admin_remove`/`org_remove` 等其余 | 原文透传 | 组织变更事实 |
| 缺 EventType | `unknown` | 空值哨兵（不 reject——解密已过信任根，记事实） |

> `EventKey`/`MessageId` 钉钉事件订阅无此概念 → 恒 null；`Source` = 事件体 `CorpId`（企业维度事实来源，可空）。

### 4.2 DingTalk `Attributes` 键清单（`dingtalk.` 前缀——对齐 `wechat.` 先例 `WeChatEventChannel.cs:417-428`）

| 键 | 值 | 说明 |
|---|---|---|
| `dingtalk.EventType` | JSON EventType 原文 | 诊断/统计 |
| `dingtalk.FromUserId` | FromUserId 原文（openId，可空） | 身份事实（同 ExternalUserId 归一值） |
| `dingtalk.CorpId` | 事件体 CorpId（可空） | 企业维度 |
| `dingtalk.UserId` | 事件体 UserId（可空——钉钉 userid 企业维度辅助，非 external_uid，对齐 README §四 userid 定位） | 组织维度 |
| `dingtalk.CreateAt` | CreateAt 原文（秒级字符串，可空） | OccurredAt 原始值；**P2-2 登记**：字段名（CreateAt vs 官方 `TimeStamp`）与单位（秒 vs 毫秒）实机对拍——见 §十实机封包核实清单 |
| `dingtalk.base_url` | `context.Parameters["base_url"]`（装配层注入，可空） | 透传 handler 供链接组装（对齐 `wechat.base_url` 先例） |

### 4.3 WeCom `EventType` 归一（对 XML `Event` / `ChangeType` 字段）

| 平台值 | 归一 EventType | 说明 |
|---|---|---|
| `Event=change_contact` + `ChangeType=create_user`/`update_user`/`delete_user`/`create_party`/`update_party`/`delete_party`/`update_tag`/`user_leave_org` 等 | **原文透传**（如 `change_contact:update_user`——复合保留子类型）或单取 `ChangeType` 原文（记事实不翻状态——对齐微信先例"其余事件原文保留"模式） | 通讯录事实；**决策悬置点**：复合 vs 单值留 Oracle 评审（建议复合 `{Event}:{ChangeType}`——同时保留事件面与子类型面，Statistics 维度不丢） |
| `Event=subscribe`（成员关注应用） | **原文透传 `subscribe`**（不产 follow——**P1-3 定案选项 b**） | **不翻状态**：企微成员关注应用事件按"记事实不翻状态"处理（对齐 §2.2 边界——能力面不声明 `Follow`；`FollowStateService` 以事件类型判据仅对 follow/unfollow/scan_follow 翻状态，`subscribe` 只落事实行） |
| `Event=unsubscribe` | **原文透传 `unsubscribe`** | 同上（不产 unfollow、不翻状态） |
| `Event=enter_agent`/`click` 等其余 | 原文透传 | 应用内事实 |
| 缺 Event/ChangeType | `unknown` | 空值哨兵 |

> **身份解析注意事项**（实现时锁定）：关注事件（subscribe/unsubscribe）的 `FromUserName` = 成员标识（openid/userid 形态按应用语境）；通讯录事件身份 = `UserID`/`OpenUserID`（双策略）。**统一解析链**：`OpenUserID` → `UserID`（复合 `{CorpId}:{userid}`）→ `FromUserName` 最后兜底——三源取一，全空 → `NotApplicable`。
> `EventKey`（点击事件 key，可空）透传；`MessageId`：企微无 MsgId 概念 → null；`Source` = `ChangeType` 或事件来源（可空）。

### 4.4 WeCom `Attributes` 键清单（`wecom.` 前缀）

| 键 | 值 | 说明 |
|---|---|---|
| `wecom.Event` | XML Event 原文（可空） | 诊断 |
| `wecom.ChangeType` | XML ChangeType 原文（可空） | 通讯录子类型（统计维度） |
| `wecom.ToUserName` | ToUserName（CorpId，可空） | 消息目标企业 |
| `wecom.FromUserName` | FromUserName（可空——关注事件触发者） | 诊断 |
| `wecom.UserID` | UserID 原文（可空） | 自建成员 userid |
| `wecom.OpenUserID` | OpenUserID 原文（可空） | 三方成员 open_userid |
| `wecom.CreateTime` | CreateTime 原文（秒级字符串，可空） | OccurredAt 原始值 |
| `wecom.base_url` | 装配层注入 base_url（可空） | 对齐微信先例 |

---

## 五、信任根与异常哨兵（复用既有，零新原语）

- **三道闸门不改**：验签（一票否决）→ AES 解密 → receiveid/ReceiveId 恒定时间校验——全部复用两库 `DingTalkEventCrypto`/`WeComEventCrypto` 既有方法（`DecryptMsg:73` / `DecryptAndVerify:74` / `VerifyAndDecryptEcho:152`）；**不引入任何新 crypto 代码**（离线评审可对拍既有 crypto 测试——`DingTalkEventCryptoTests.cs`/`WeComEventCryptoTests.cs` 已覆盖正负路径）。
- **哨兵契约**（对齐微信先例与 `ChannelEvent.cs` 语义）：`Rejected` = 信任根/解密失败（编排短路：不记录、不回复、不回退 handler）；`NotApplicable` = 非业务事件（无身份标识）——编排跳过；`Parsed` = 业务事实（编排链消费）。
- **1200ms 硬约束（钉钉）/ 5s（企微）**：`HandleEventAsync`/`VerifyUrlAsync` 全 CPU-only 无出站调用（对齐两库 `AuthenticateAsync` 既有承诺与微信先例 `WeChatEventChannel.cs:296-298` 注释）；微信先例"重复验签+解密链"已知开销（Oracle P2-2）在钉钉场景同价接受（事件入站低频）；**§3.6 门扩展的 VerifyUrlAsync 先行额外一次验签+解密亦同价**（P1-5 开销声明——CPU-only <50ms × 链式，见 §3.6）。
- **隐私**：日志 PII 脱敏（对齐 FW-09 先例——企微 `FromUserName`/`UserID` 非敏感但入参日志保持截断纪律；钉钉事件体仅诊断字段）。

---

## 六、测试计划

> 位置：`_Tests/Extension.Federation.DingTalk.Tests/` + `_Tests/Extension.Federation.WeCom.Tests/`（既有 `{平台}EventChannelTests.cs` 生产路径宿主：经 `ISsoChannelFactory` 按 channelId 构造真实实例 + 既有 `EncryptMsg` 样本构造——测试宿主零新增）。

### 6.1 DingTalk（≥7 新用例）

| # | 用例 | 断言 |
|---|------|------|
| D-1 | `HandleEventAsync_ValidSignatureAndCipher_ParsesChannelEvent`（正向：`{"EventType":"SCAN","FromUserId":...)` 样本——密文参数名 `encrypt` 主键） | 状态=Parsed；`EventType="scan"`；`ExternalUserId=FromUserId`；`OccurredAt`=CreateAt→UTC 精确秒；`Attributes["dingtalk.EventType"]=="SCAN"`（原文）；`RawPayload`=明文 JSON |
| D-2 | `HandleEventAsync_TamperedSignature_Rejected`（篡改签名样本） | 状态=Rejected（一票否决，绝不解密） |
| D-3 | `HandleEventAsync_MissingFromUserId_NotApplicable`（`{"EventType":"user_add_org"...、无 FromUserId` 业务事件——含 check_url 样本同判，P0-1/P1-2） | 状态=NotApplicable（不造半个事件——契约 L12-13；§3.0 守卫统一判据） |
| D-4 | `VerifyUrlAsync_ValidInbound_ReturnsEncryptedEnvelope`（check_url 与业务事件正向各一样本——P0-2 加密信封断言重写） | 返回加密信封 JSON 四字段（`encrypt`/`msg_signature`/`timeStamp`/`nonce`——字段名以官方为准）；`encrypt` 经 `DecryptMsg` 解密 = `"success"`；`msg_signature` 与 `ComputeSignature(token, timeStamp, nonce, encrypt)` 对拍；**非明文 success** |
| D-5 | `VerifyUrlAsync_TamperedSignature_Null`（check_url 篡改签名） | 返回 null（400 语义） |
| D-6 | `ChannelSource_Capabilities_DeclaresEventOAuth` | `DingTalkChannelSource.Capabilities` 含 `Event|OAuth`（能力面锁定） |
| D-7 | `HandleEventAsync_MissingCipher_ParamsRejected`（query 与 `__raw` 均无密文（`encrypt`/`msg_encrypt`）且 `__raw` 非 JSON） | 状态=Rejected（参数缺失——兜底提取负路径） |
| D-8 | `HandleEventAsync_EncryptParamName_Compatibility`（body JSON 单键 `{"encrypt":...}` 样本——P0-1 双键双位实证；`msg_encrypt` 别名样本同型） | 状态=Parsed（`encrypt` 主参数路径实证——query 优先 + body 兜底；别名分支 Theory 双样本） |
| D-9 | `AuthenticateAsync_IdentityLessEvent_Releases`（check_url / user_add_org 无 FromUserId 事件——P1-2 放行回归锁） | `SsoChannelAuthResult(true, null, null, 0)`（不再 `DINGTALK_EVENT_DECRYPT_FAILED` 400）；带 FromUserId 事件行为不变（既有断言保持） |

### 6.2 WeCom（≥3 新用例——评审门槛：验签正/负 + 事件产出 + echostr 握手）

| # | 用例 | 断言 |
|---|------|------|
| W-1 | `HandleEventAsync_ValidEvent_ParsesChannelEvent`（`change_contact` + `ChangeType=update_user` + UserID 样本——复用现有 `BuildValidEvent` XML 构造） | 状态=Parsed；`EventType` 归一（复合 `change_contact:update_user` 或 `update_user`——随 §4.3 裁定）；`ExternalUserId` 双策略值（自建 `{CorpId}:{userid}` / 三方 `OpenUserID` 两分支）；`OccurredAt` UTC；`RawPayload`=明文 XML |
| W-2 | `HandleEventAsync_TamperedSignature_Rejected` | 状态=Rejected |
| W-3 | `VerifyUrlAsync_ValidEcho_EchoesPlain`（echostr 握手正向——`VerifyAndDecryptEcho` 委托：验签+解密回显明文） | 返回解密明文 echostr（对齐微信会话）；篡改签名 → null（负向同用例 Theory 或并列 W-4） |
| W-4 | `VerifyUrlAsync_Failures_Null`（缺参数/验签失败/解密失败 → null） | 全 null（装配层 400 语义） |
| W-5 | `HandleEventAsync_SubscribeEvent_Passthrough`（`Event=subscribe` 样本——P1-3 原文透传回归锁） | `EventType="subscribe"`（原文透传——**不产** follow、不翻关注状态；能力面不声明 `Follow`——§4.3 选项 b） |
| W-6 | `ChannelSource_Capabilities_DeclaresEventOAuth` | `WeComChannelSource.Capabilities` 含 `Event|OAuth` |

### 6.3 Federation 编排链可达性（受影响面直验，≥4 用例）

| # | 用例 | 断言 |
|---|------|------|
| F-1 | `EventEndpoint_DingTalk_CheckUrl_ReturnsEncryptedEnvelope`（`FederationWebExtensionTests.cs`——POST 事件端点注入 check_url 样本） | 200 且 body = **加密应答信封**（`application/json`——四字段对拍：`encrypt` 解密 = `"success"` 且 `msg_signature` 验签通过；**非明文 success**——P0-2 断言重写；钉钉后台 URL 校验闭环） |
| F-2 | `Orchestrator_DingTalkEvent_ReachesHandler`（编排链集成：注册测试 `IEventReplyHandler`（ChannelType=dingtalk_event）→ `EventOrchestrator.OrchestrateAsync` 走 GET event 端点或直调编排器） | handler 被迭代（first-non-null-wins 命中——双桶对钉钉可达 W3 语义）；对照 WeCom 同型用例 |
| F-3 | `EventEndpoint_WeCom_Echostr_Verify`（GET echostr 正向——现端点 + 新 channel 实现） | 200 回显明文（`WECHAT_URL_VERIFY_FAILED` 400 根治回归锁） |
| F-4 | `EventEndpoint_DingTalk_BusinessEvent_ReturnsEnvelopeIgnoringHandlerReply`（POST 注入 SCAN 业务事件 + 注册测试 `IEventReplyHandler`——P1-1 恒回信封回归锁） | 200 恒回加密信封（handler 回复被忽略——钉钉不承载业务自由文本）；测试 handler 副作用已执行（编排链仍达——uid 映射/handler 双桶迭代未被门扩展短路） |

> 除上述新用例，两库既有 `AuthenticateAsync` 用例**带身份路径全量保持**（回归零破坏）；**惟 DingTalk 无身份事件（check_url/组织事件）失败断言按 P1-2 放行设计更新**（`DINGTALK_EVENT_DECRYPT_FAILED` 400 → `success(true, null)`，D-9 同款语义）——既有断言盘点于实施期对齐。

---

## 七、受影响面与验证

| 变更 | 受影响项目 | 验证 |
|------|-----------|------|
| `Federation.DingTalk`（两方法 + Capabilities + AuthenticateAsync 双键/放行） | `Extension.Federation.DingTalk.Tests`（7-9 新用例 + 既有全量——含 D-9 放行断言替换） | L1：DingTalk.Tests 全绿 |
| `Federation.WeCom`（两方法 + Capabilities） | `Extension.Federation.WeCom.Tests`（6 新用例 + 既有全量） | L1：WeCom.Tests 全绿 |
| `Federation` 主包（§3.6 门扩展） | `Extension.Federation.Tests`（`FederationWebExtensionTests`/`FederationContractDimTests`/`EventOrchestratorI2Tests` + F-1~F-4） | L1：Federation.Tests 全绿（含端点半黑盒） |
| **`Federation` 主包门扩展的 WeChat 侧影响（P1-4——每笔 POST 增一次 `VerifyUrlAsync` 调用，守卫 null 秒退）** | **`Extension.Federation.WeChat.Tests`（141 用例全量回归——行为零变化锁定）** | **L1：WeChat.Tests 141 用例全绿** |
| 编排链消费方（WeChatOperations 等——不经本改） | 无代码依赖（`ChannelEvent.ChannelType` 键不变；两库事件产出仅新路径） | slnx 构建 0 错误（L0 编译门）+ 受影响面定向测试；发布前 L2 全量回归（按测试纪律矩阵——契约包无变更则非强制，本立项为库内新增实现 + 主包门扩展，L1（含 WeChat 141 回归）即达标；若评审要求可上探 L2） |

> 依赖拓扑：两库 → `Federation.Abstractions`（契约——**零变更**，仅消费既有契约成员）；`Federation` → FA（零契约变更）。无 Abstractions 契约变更 → 消费方零适配零重编译。

---

## 八、文档同步

| 文档 | 更新 |
|------|------|
| `_Framework/Federation.DingTalk/README.md` | §四双通道契约 `dingtalk_event` 行补 `HandleEventAsync`/`VerifyUrlAsync`（编排层映射声明由"矛盾"转"落地"——删 DT-1 矛盾注记）；§四/§五回调回执改**加密 JSON 应答信封**（P0-2/P1-1——恒回信封 + handler 回复忽略声明，非明文 success）+ 密文参数名 **`encrypt` 主/`msg_encrypt` 别名双键**（P0-1）+ AuthenticateAsync 无身份事件放行注记（P1-2）；§三组件清单 `DingTalkEventChannel` 行 + `DingTalkChannelSource` 行补 I2-2 能力面；§九版本记录补 V0.4.2 行（I2-2 契约跟进：两方法 + Capabilities `Event\|OAuth`；测试数） |
| `_Framework/Federation.WeCom/README.md` | 同型：§四 `wecom_event` 行补两方法（echostr 握手从"归装配层调原语"改为"装配层调 channel.VerifyUrlAsync（内委托 Crypto）"——WC-2 声称修正）；§三组件清单；§九版本记录 V0.4.2 行 |
| `docs/Federation/联邦互联-使用指南.md` | 平台矩阵表（§一/§五区域若含两平台事件能力描述）补"事件编排可达（I2-2 契约跟进）"；故障排查（§七）若含 `WECHAT_URL_VERIFY_FAILED` 条目注明"WeCom V0.4.2+ 已实现 VerifyUrlAsync，配置期校验闭环" |
| 根 `README.md` 扩展一览表 | 两库行描述尾注补 I2-2（DingTalk/WeCom 事件编排链可达 + URL 验证握手）；版本列（如有 tag 更新）同步 |
| `docs/变更记录.md` | 追加 V0.4.2 × 2（+ 可能的主包 V0.18.1）条目（含评审闭环注记） |

---

## 九、版本与发布

| 包 | 版本 | 类型 | 说明 |
|---|---|---|---|
| `TKWF.Federation.DingTalk` | **V0.4.2**（patch） | 非破坏（新增覆写 + 能力声明；公共 API 仅增） | MinVerTagPrefix `Federation.DingTalk/v` |
| `TKWF.Federation.WeCom` | **V0.4.2**（patch） | 非破坏 | `Federation.WeCom/v` |
| `TKWF.Ext.Federation`（主包，§3.6 门扩展——v0.2 起为**必做**：P0-2 check_url 信封可达 + P1-1 业务事件恒回信封） | **V0.18.1**（patch） | 非破坏（端点分派顺序调整 + DingTalk 专用直达分支，对外契约零变化） | 先决发布（两库 tag 依赖主包行为——装配层门扩展） |

> **发布顺序**：主包 V0.18.1 → 两库 V0.4.2（本地 DLL 模式联调验证 → 用户同意后逐个 tag + push——逐个 push 防丢事件，AGENTS §5 tag 纪律；在线发布按双轨节奏——扩展发布依赖框架组先行发布，且未收到框架最新包在线发布通知前保持本地模式）。本地全量部署 `build\build-ext-deployment.ps1 -Cleanup` 收口 refs。

---

## 十、风险与回滚

| 风险 | 等级 | 缓解 |
|------|:---:|------|
| DingTalk 密文参数名与实机封包不符（官方 `encrypt` vs 兼容 `msg_encrypt`；query vs body JSON `{"encrypt":...}`）——P0-1 | 中 | §3.1-1 双键双位探查（query `encrypt`→`msg_encrypt` + `__raw` JSON 双键任一）——按官方口径为主、别名兜底，fail-closed（均无 → Rejected/null，不误判业务）；测试 D-8 锁定正路径 + D-7 锁定负路径；**实机封包核实清单**（下表）核实后收敛 |
| 钉钉应答信封四字段名/大小写约定与响应 Content-Type 与官方实现有出入（`msg_signature` vs `signature`、`timeStamp` 大小写）——P0-2 | 中 | §3.2 按官方构造规则实施，字段别名/大小写/Content-Type **以官方回调文档为准**；`encrypt` 解密 = `"success"` + `msg_signature` 验签对拍（D-4）；**实机冒烟在消费者侧验证兜底**（钉钉后台保存回调 URL + 推送一条 SCAN 事件验证重试不触发） |
| `AuthenticateAsync` 放行（无身份事件不再 400）引入行为变化——P1-2 | 低 | 仅 check_url/组织事件无身份分支变化（`DINGTALK_EVENT_DECRYPT_FAILED` 400 → success(null)），带身份路径零变化；D-9 回归锁 + 既有断言实施期盘点替换；放行不复用旧错误码（防监控误报警） |
| 企微 `subscribe` 事件语义与实机 `FromUserName` 形态差异（用户级 openid vs userid）→ `ExternalUserId` 取值错位 | 中 | §4.3 统一三源解析链（OpenUserID → UserID 复合 → FromUserName 兜底）fail-closed；W-5 用例（原文透传回归锁）+ 消费者实机验证 |
| 装配层门扩展（§3.6）引入 WeChat/WeCom POST 额外一次 `VerifyUrlAsync` 调用——性能/行为回归 | 低 | 守卫先退（无 echostr → null 秒退）；`WeChatEventChannel.VerifyUrlAsync` 无 echostr 时 L243-250 参数缺失守卫 null；F-3 集成用例 + **`Extension.Federation.WeChat.Tests` 141 用例全量回归锁定（P1-4）** |
| DingTalk 事件入站链式三次验签+解密开销（段 1 VerifyUrlAsync + 段 2 AuthenticateAsync + 段 3 HandleEventAsync）超出钉钉 1200ms 硬约束——P1-5 | 低 | CPU-only 无出站，单次 <50ms、链式 ~150ms 量级，相对 1200ms 余量充分；事件入站低频（秒级~分钟级）；实施期在装配层冒烟测端点响应时间留存证据 |
| 主包改动（§3.6）被评审否决 → 采用备选 B 或缩小范围（仅 WeCom 两方法） | 中 | §3.6 已列备选陈述；本方案 C 档先行文档化，实施按 Oracle 评审裁定 |
| 版本/tag 联动错位（主包 vs 两库） | 低 | §九发布顺序 + 逐个 push；`docs/versions.json` 同步 |

**实机封包核实清单（实施期/消费者侧冒烟必核，P0-1/P0-2/P2-2 登记）**：

- [ ] 钉钉回调 body 密文参数名：`encrypt`（单键）实证——`msg_encrypt` 别名是否仍有平台侧发送（按双键实现，核实后收敛注释）；
- [ ] 验签参数位置：`signature`/`timestamp`/`nonce` 在 **query**（官方口径）实证；
- [ ] check_url 请求体 EventType 字段名与取值（`check_url`）实证；
- [ ] 应答信封四字段（`msg_signature`/`timeStamp`/`nonce`/`encrypt`）与 Content-Type 按**官方回调文档**对拍——island 赋值（`timeStamp` vs `timestamp`、`msg_signature` vs `signature`）实测钉钉是否接受；
- [ ] 事件时间戳字段名与单位对拍（`CreateAt` vs 官方 `TimeStamp`，秒 vs **毫秒**）——P2-2：若实机为毫秒 → `OccurredAt` 换算改 `FromUnixTimeMilliseconds`（§3.1-4 实现点），`dingtalk.CreateAt` 属性键保留事件原文；
- [ ] 业务事件回执不触发钉钉重试风暴（SCAN 事件推送一次、后台无重复重试日志）——P1-1 实机闭环；

**回滚策略**：全部改动为**新增覆写 + 能力声明 + 端点分派顺序调整**——回滚 = revert 对应提交（两库方法删除回 DIM 默认，主包门顺序回退），无数据迁移、无契约破坏、无表结构变更。WeCom 回滚后 echostr 回到 400（评审前状态），DingTalk 回滚后事件编排回到跳过（评审前状态）——均不劣于基线。

---

## 十一、验收标准（完成定义）

1. `DingTalkEventChannel` 实现 `HandleEventAsync`（§3.1——`encrypt` 主/`msg_encrypt` 别名双键双位，P0-1）+ `VerifyUrlAsync`（§3.2——**加密应答信封**，非明文 success，P0-2）；`DingTalkChannelSource.Capabilities` = `Event|OAuth`；`AuthenticateAsync` 无身份事件**放行**（P1-2）。
2. `WeComEventChannel` 实现 `HandleEventAsync`（§3.3）+ `VerifyUrlAsync`（§3.4 委托 `VerifyAndDecryptEcho`），`WeComChannelSource.Capabilities` = `Event|OAuth`；WeCom subscribe/unsubscribe **原文透传**（P1-3 选项 b——§2.2/§3.5/§4.3/W-5 全局一致）。
3. `FederationWebExtension.HandleEvent` 门扩展生效（§3.6）——DingTalk check_url POST → 200 **加密信封**（`application/json`）；DingTalk 业务事件 →200 恒回加密信封（**handler 回复被忽略**——P1-1）；DingTalk 无身份组织事件 → 放行 200（P1-2）；WeChat/WeCom 既有行为零变化（门扩展开销已声明——P1-5）。
4. 测试：DingTalk.Tests ≥7 新用例（含 D-8 双键/D-9 放行）/ WeCom.Tests ≥6 新用例 / Federation.Tests ≥3 新用例（F-1 信封 + F-4 业务回执 + F-2/F-3）**+ `Extension.Federation.WeChat.Tests` 141 用例全量回归（P1-4）** + 三项目既有全量全绿；slnx L0 构建 0 错误。
5. 文档同步（§八五件）+ 版本记录（§九）；DingTalk/WeCom README §四"编排层映射"矛盾消除（DT-1 述评闭环）。
6. DT-1/WC-1/WC-2 三条评审问题逐条闭环登记（`阶段性评审记录` / 修正计划 C 档勾除）；**oracle-pro1 2026-11-09 评审条件闭环自检**：P0-1/P0-2（§3.1/§3.2/§3.6/§10）+ P1-1~P1-5（§3.6/§3.5/§4.3/§6/§7）+ P2 三要点（§4.1/§4.2/§3.1 示例清理 + §10 实机封包核实清单）逐条可追溯。

---

## 变更记录

| 版本 | 日期 | 变更 |
|------|------|------|
| v0.1 | 2026-11-09 | C 档立项首稿（仅方案文档）：评审问题 DT-1/WC-1/WC-2 收口设计——两库契约方法实现细节 + 能力声明 + 装配层门扩展（备选陈述）+ 测试计划 + 版本（两库 V0.4.2 / 主包 V0.18.1 联动）+ 风险回滚；对齐微信先例 `WeChatEventChannel.cs` 形态 |
| v0.2 | 2026-11-09 | **oracle-pro1 评审（PASS WITH CONDITIONS）条件逐条闭环入正文**——P0-1（§3.1/§3.2 密文参数名 `encrypt` 主/`msg_encrypt` 别名双键双位 + 官方签名字段口径；§10 实机封包核实清单）、P0-2（§3.2 VerifyUrlAsync 重设计为**加密应答信封**——复用 `EncryptMsg` 生产原语 + `ComputeSignature` 四字段；§6 F-1/D-4 断言重写；§10 Content-Type/字段名实机兜底）、P1-1（§3.6 业务事件恒回加密信封 + handler 回复忽略声明 + F-4 回归锁）、P1-2（§3.6/§〇 组织变更无身份**放行**设计 + 端到端可编排披露 + D-9）、P1-3（§4.3/§3.5 WeCom subscribe 定案**原文透传**——选项 b，§2.2/§3.5/§4.3/W-5 全局统一 + RecordAsync 能力门事实校正）、P1-4（§7 `Extension.Federation.WeChat.Tests` 141 用例全量回归锁定）、P1-5（§3.6/§5 门扩展开销如实声明 + EventType 解密 JSON 分派）、P2 三要点（§4.1 check_url 行标注、§4.2/§10 CreateAt vs TimeStamp 毫秒对拍登记、§3.1 代码示例占位残留清理） |

<!-- EOF -->