# DMP-Lite 反馈处置——消费方适配通知（P0-1 阻塞解除 + P1-1/P1-2 新版本）

> **发出方**: TKWF 扩展模块组｜**日期**: 2026-10-21｜**状态**: ✅ 已修复已发布（tag 全推 + CI 发布中）
> **收件方**: **DMP-Lite（Platform.Web / Merchant）** 消费方项目组
> **关联**: 反馈清单（2026-10-21 上报 P0-1/P0-2/P0-3/P1-1/P1-2/P1-3）· I2 方案 `docs/Federation/I2-渠道关注关系域-开发方案.md`（§11.1 冒烟）· 变更记录

---

## ⚡ 一句话摘要

**P0-1（Development 启动崩溃）已修复并发布——请重新同步部署根 + 重启 Development 即解除全部阻塞（含缺表死锁 P0-2/P0-3 自动消失）；同时 4 个新版本包含 P1-1（微信事件明文模式支持）与 P1-2（免登 TTL 默认 120 对齐）。**

## 一、P0-1 阻塞解除（**必须动作**）

| # | 动作 | 说明 |
|---|------|------|
| 1 | **重新同步部署根 refs**（`F:\TKWF_FRAMEWORK_PATH\build\refs\` 或消费项目 bin） | 拿到修复后 `TKWF.Ext.FeatureManagement.dll`（**06:50 同步的是修复前版本**——`TryAddScoped<FeatureManagementApiService>()` 普通 DI 注册 ctor 注入 IDomainUser，Development ValidateOnBuild 必崩） |
| 2 | **重启 Development** | 崩溃解除 → FreeSql SyncStructure 自动补建 5 张缺表（`TKWF_FollowEvent`/`TKWF_FollowState`/`TKWF_WeChatSubscriptionStat`/`TKWF_JobRecord`/`TKWF_AuthFactor`）→ P0-2 死锁解除 → 编排器 42P01 / JobWorker 刷屏（P0-3）自动消失 |
| 3 | **清消费项目 bin/obj** | 防旧 DLL 残留（`dotnet build-server shutdown` 优雅关闭编译服务器后重 build） |

> **因果链**（P0-2/P0-3 判定）：P0-1 挡死 Development → 建表（仅 Development 注册的 SyncStructure）无法执行 → 缺表 → 微信编排异常无回复。**P0-1 修复 = 唯一解锁动作**，P0-2/P0-3 无需单独处理。

## 二、新版本发布（NuGet）

| 包 | 版本 | 变更 |
|----|------|------|
| `TKWF.Ext.FeatureManagement` | **v0.4.5** | P0-1 修复——`FeatureManagementApiService` 注册改由消费方 SG#2 自动注册（删 TryAddScoped 普通 DI） |
| `TKWF.Federation.Abstractions` | **v0.2.1** | P1-2——`FederationLoginEntryOptions.TicketTtlSeconds`/`MaxTicketTtlSeconds` 默认 **300→120**（对齐 TrustCenter 密封票据 120；校验语义不变 `TicketTtl ≤ MaxTicketTtl` fail-fast） |
| `TKWF.Federation.WeChat` | **v0.6.1** | P1-1——微信事件通道**明文/兼容模式**支持（msg_encrypt 可得=安全模式 4 参验签+AES 解密；无 msg_encrypt=明文模式 3 参验签+明文 XML 直接解析；修复明文 POST 误报 `WECHAT_EVENT_PARAMS_MISSING`） |
| `TKWF.Ext.WeChatOperations` | **v0.5.1** | `[TKWFExtensionDependency]` FA MinVersion 0.1.0→0.2.0 对齐（DEP002 门控） |

> **无破坏性 API 变更**（全部 patch 级兼容；契约签名零变化）。`TKWF.Federation.Abstractions` 为契约包——重发后消费方 NuGet restore 自动拉新版本（`>=0.2.0` 依赖自动满足）。

## 三、P1-1 微信事件推送：3 种模式支持矩阵（新行为）

| 模式 | 微信后台配置 | 通道处理 | 说明 |
|------|------------|---------|------|
| **安全模式**（推荐/默认） | 消息加解密方式=安全模式 | POST body：`<xml><ToUserName>..</ToUserName><Encrypt><![CDATA[密文]]></Encrypt></xml>`；query：`msg_signature` 4 参验签 + AES 解密 | 既有行为不变 |
| **兼容模式** | 消息加解密方式=兼容模式 | 同安全模式（密文入站） | 既有行为不变（密文消息） |
| **明文模式**（V0.6.1 新增） | 消息加解密方式=明文模式 | POST body：明文 XML 直接推送；query：`signature` 3 参验签（SHA1(sort(token,timestamp,nonce))，**消息体不参与签名**） | 无需 EncodingAESKey 解密，虽支持但**不建议生产**（明文泄漏 openid） |

## 四、P1-2 免登 TTL：默认值对齐（新行为）

- `TKWF:Federation:LoginEntry:TicketTtlSeconds` **默认 300→120**（原默认 300 > TrustCenter `SealedTicketExpirationSeconds` 默认 120——I2 文档「须 ≤ 密封票据 TTL」表述与默认值自相矛盾，现对齐消除直觉冲突）
- 校验语义不变：启动期 `TicketTtlSeconds ≤ MaxTicketTtlSeconds`（默认 120），超限 fail-fast
- **显式配置了 `TicketTtlSeconds`/`MaxTicketTtlSeconds` 的消费方零影响**（显式值优先于默认值）

## 五、P1-3 冒烟/联调指引（§11.1 摘要）

完整脚本见 I2 方案 `docs/Federation/I2-渠道关注关系域-开发方案.md` §11.1——**验证全链路**（微信后台 → 事件端点 → 验签 → 编排 → 数据落库）：

- **URL 验证 GET**（微信后台保存配置时自动触发）：明文模式 `signature` 3 参回显 echostr；安全模式 `msg_signature` 4 参回显解密明文
- **事件推送 POST**（伪造 subscribe）：明文模式 body=明文 XML；安全模式 body=`<Encrypt>` 封装 XML
- **两个必踩的坑**（已回填文档）：
  1. **AES-256-CBC 加密**：`key=Base64Decode(EncodingAESKey+"=")`，`IV=key 前 16B`，明文 `[16B 随机][4B 大端长度][XML][appid]`（安全模式伪推送必知）
  2. **SHA1 排序必须 Ordinal**：`Sort-Object -Culture InvariantCulture`（PowerShell 默认文化排序签名必错报 `WECHAT_SIGNATURE_INVALID`；代码侧 `ComputeSignature` 已 `StringComparer.Ordinal`）
- **期望结果**：`TKWF_FollowEvent` 落行（`EventType=follow`）+ `TKWF_FollowState` 更新；handler 有配置 → 微信收被动回复；无 → 200 空回复（正常）

## 六、已知遗留（无需阻塞）

- **隧道开了 ≠ 能回话**：端点 200 空回复兜底可能掩盖编排异常——排查"没回复"须看服务端日志（42P01 等），本次 P0-3 已随 P0-1 修复解除
- **后续迭代提醒**：`TransactionProbe`/`AuditLogging` 等无关此批

<!-- EOF -->