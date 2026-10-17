# ADR-Federation.WeChat-菜单出站API与凭证解析可插拔

> **版本**：v0.2（Oracle 评审 PASS WITH CONDITIONS 意见吸收——P1×2）
> **状态**：草稿（Oracle 评审已完成，P1 已吸收——待实施）
> **关联**：开发方案 `docs/WeChatOperations/微信公众号运营扩展-开发方案.md`（平台库增强 §3.2）；`ADR-Federation-平台特定事件回复Handler基类与二级分发.md`（事件回复底座，既有）；`docs/AuthCenter/ADR/ADR-AuthCenter-归层与命名.md`（四层归层——`TKWF.Federation.{平台}` = 大使馆/纯库）
> **Oracle 评审**：PASS WITH CONDITIONS（2026-10-17）——P1×2（DI 注册缺省非 ctor fallback / CompositeChannelRegistry 聚合机制）已吸收
> **日期**：2026-10-17

---

## 目的与目标

为 `TKWF.Federation.WeChat` 平台库（大使馆/纯库）新增**公众号运营出站能力**与**凭证解析可插拔契约**，支撑运营扩展（WeChatOperations）的菜单管理与多公众号配置：① `WeChatApiClient` 新增菜单出站 API（`CreateMenuAsync`/`GetCurrentSelfMenuInfoAsync`/`DeleteMenuAsync`——**自实现，不引 Senparc**，借鉴其 JSON 结构设计自有 poco）；② `IWeChatCredentialResolver` 可插拔契约（**DI 注册缺省 Options 实现** + 扩展注册 DB 实现——**凭证来源演进 Options → DB 加密 → 未来 KMS**）；③ 两源并存分场景裁定（启用带 DB 的运营扩展后 **DB 数据为权威**，Options 退为引导/降级）。读者 3 句话内明白：平台库出站面新增菜单 3 API（自实现不引第三方 SDK），凭证解析从"Options 唯一源"演进为"可插拔契约（DI 缺省 Options）"，微信 channel 配置事实源在两源并存下由 DB 权威。

## 问题

### 问题现象

1. **菜单管理出站能力缺失**：运营扩展需读取公众号当前生效菜单（`get_current_selfmenu_info`）、全量覆盖发布（`menu/create`）、删除菜单（`menu/delete`）——`WeChatApiClient` 仅有 OAuth/用户信息三方法，无菜单 API。
2. **凭证解析不可插拔**：`WeChatApiClient.ResolveCredential(appId)` 是 private 方法，只读 `WeChatOptions.Channels`（Oracle P2-4 凭证自持）——多公众号运营场景凭证若落 DB（AES-GCM 密文），**DB 凭证无法到达出站调用**。
3. **channel 配置事实源分裂**：现状 `WeChatOptions.Channels`（配置节）+ Federation `SsoChannelRegistryEntity`（channel 注册 DB 权威）+ 运营扩展新 DB 实体——三处可能配置同一公众号，谁权威无裁定。
4. **template handler 仅单图文**：需求"图文 pattern（标题/正文/链接/图片地址）+ **支持多图文**"——现状 `WechatTemplateEventReplyHandler` 构造 `News: [单篇]`。

### 触发场景

- 运营扩展（WeChatOperations）管理多公众号：按 ChannelId 配置凭证/回复规则/菜单，发布菜单到微信。
- 多公众号凭证落 DB（密文）——出站调用需从 DB 解密取凭证。
- 未来凭证来源演进（KMS/Secret Manager）——无需改动 `WeChatApiClient` 调用面。

### 现有方案的不足

- `WeChatOptions.Channels` 静态配置节：只适合少数静态账号，无法承载多公众号运营配置（增删改/启用禁用/运营字段）。
- `ResolveCredential` private 硬编码 Options：凭证来源演进必须改平台库内部（每换一个来源改一次）。
- **ctor 内 fallback 反模式**（Oracle P1）：`IWeChatCredentialResolver?` 可空注入 + ctor 内 `?? new OptionsResolver(options)` 绕过 DI 生命周期管理（即使 Singleton 也应走容器）——应 DI 注册缺省实现 + ctor 非空。
- 引 Senparc 代价：`Senparc.Weixin.MP` 16.25.4 引入四层传递依赖（CO2NET/NeuChar/Weixin/Newtonsoft.Json——与本仓库 System.Text.Json 栈相悖），破坏平台库"轻量纯库"定位（QQ/支付宝均自实现零第三方协议库）；且运营扩展出站面实际仅菜单 3 API，自实现面小而可控。

## 使用场景

### 场景清单

| 场景 | 使用方式 |
|---|---|
| 菜单发布/读取/删除（运营扩展） | `WeChatApiClient.CreateMenuAsync(token, json)` / `GetCurrentSelfMenuInfoAsync(token)` / `DeleteMenuAsync(token)`——平台库出站 |
| 多公众号凭证落 DB（密文） | 运营扩展注册 `DbWeChatCredentialResolver`——DB 命中解密返回，未命中回退 Options |
| 纯静态少数账号（未启用运营扩展） | 缺省 `OptionsWeChatCredentialResolver`——现状语义零破坏 |
| 未来凭证来源 KMS/Secret Manager | 注册新 resolver 实现——调用面零改动 |
| template 多图文（默认回复） | `Channel.Extra["news_reply"]` 模板 `articles` 数组（1..8 篇）——单图文兼容回退 |

### 不适用边界

- **非微信平台**（QQ/Alipay/OIDC/Google/Microsoft）：无公众号菜单/运营 API 面——不适用。
- **微信永久素材上传**（add_material 拿 media_id）：阶段一人工维护 JSON 无此需求——列二期可选评估，不进入本 ADR 范围。
- **群发/模板消息/客服消息**：运营扩展未来按需自实现或再评估 Senparc——不影响本 ADR 的 poco 分层（协议细节隔离于平台库 internal）。

---

## 关键决策记录

| # | 决策 | 理由 |
|---|------|------|
| D1 | **不引 Senparc**——菜单 API 自实现（手写 HttpClient + System.Text.Json），**借鉴 Senparc `Entities.Menu` JSON 结构**设计自有 poco | 平台库"轻量纯库"全局定位（`TKWF.Federation.{平台}` 全自实现）；运营出站面仅 3 API 面小可控；Senparc 四层依赖（含 Newtonsoft.Json）与本仓库 STJ 栈相悖；零新增第三方协议依赖（AGENTS §4 外部依赖变更需 ADR——引入 vs 不引入均记录） |
| D2 | **`IWeChatCredentialResolver` 契约落平台库**（可空注入——缺省 Options 实现，向后兼容零破坏） | 凭证来源演进是平台库演进方向（Options→DB→KMS），契约归平台库、实现归扩展（对齐 AuthCenter E4 keyed `ISymmetricKeyProvider` 先例——抽象在框架/平台，实现在装配层） |
| D3 | **两源并存分场景**：启用带 DB 的运营扩展后 **DB 数据为权威**，Options 退为引导/降级（未启用扩展 / DB 无记录时兜底） | 用户裁定 2026-10-17；对齐 `CompositeChannelRegistry` 既有"DB 优先 → 静态回退"组合语义（Federation v0.4.0）——语义零冲突 |
| D4 | **template handler 多图文升级在平台库**（V0.4.1 小版本，`articles` 数组优先 + 单图文兼容回退） | 多图文是通用能力（运营扩展需要，其他消费方也可能需要）——平台库增强而非运营扩展自建；V0.4.0 刚发布版本线干净 |
| D5 | 凭证三字段（AppSecret/Token/EncodingAESKey）入 DB 用 **AES-GCM 单段密文**（base64(nonce‖cipher‖tag)） | 对齐 AuthSurface V0.2.0/TrustCenter `SsoClientEntity` 既有规范；密文 + 密钥文件分离消解"secret 入 DB"泄露面（用户裁定：secret 可入 DB，加密解决） |

---

## 验收（方案 §九 摘要）

- [ ] 平台库菜单 3 API 落地（自实现不引 Senparc，JSON 结构借鉴）——出站 URL/错误码/JSON 透传测试全绿
- [ ] `IWeChatCredentialResolver` 契约 + 缺省 Options 实现 + 扩展 DB 实现（DB 命中优先，未命中回退）
- [ ] template handler 多图文升级（articles 数组 + 单图文兼容回退）
- [ ] 两源并存集成：DB 权威 + Options 降级（组合语义测试）
- [ ] 凭证 AES-GCM 密文落库 + 解密仅出站瞬时（明文不落库/日志）
- [ ] slnx 构建 0 错误 + 受影响面测试全绿

---

## 生命周期注记

- 本 ADR 为平台库演进记录（新增菜单出站面 + 凭证解析契约 + 两源并存裁定）——不废弃既有 ADR；`ADR-Federation-平台特定事件回复Handler基类与二级分发.md` 保持活跃（事件回复底座不变）。
- 若未来引入 Senparc（群发/模板消息面扩大时重新评估），须新 ADR 废弃 D1 并记录演进理由。

<!-- EOF -->
