# Federation.Template — 平台网关库复制脚手架

> 本目录 = N1 平台网关通用模板的**可复制脚手架**（复制起点，非运行时组件）。权威模板文档见 `docs/Federation/平台网关模板-开发指南.md`；模板只固化已实证形态（WeChat 先例为权威事实源，对齐提炼非重构——F5）。
> **本目录不参与构建**（占位符名非法 C#）——复制替换后注册进 slnx 才构建。

---

## 复制用法（30 分钟起步目标）

1. **复制目录**：
   ```
   _Framework/Federation.Template/  →  _Framework/Federation.{平台}/
   _Tests/Extension.Federation.Template.Tests/  →  _Tests/Extension.Federation.{平台}.Tests/
   ```
2. **全局替换占位符**（三个，勿漏）：
   | 占位符 | 含义 | 示例（QQ） | 示例（微信） |
   |--------|------|-----------|------------|
   | `{Platform}` | 平台类前缀（PascalCase） | `Qq` | `WeChat` |
   | `{Namespace}` | 命名空间段 / 包名段（品牌缩写平台保持全大写） | `QQ` | `WeChat` |
   | `{platform}` | 平台小写前缀（channel_type 字符串） | `qq` | `wechat` |
3. **形态裁剪**（无入站回调平台——QQ/支付宝出站面/B站/小红书）：
   - 删除 `{Platform}EventChannel.cs` + `{Platform}EventCrypto.cs`；
   - 删除 `{Platform}FederationServiceCollectionExtensions.cs` 中 EventChannel 注册行（注释标记处）；
   - 删除 `_Tests/Extension.Federation.{平台}.Tests/` 中 `{Platform}EventChannelTests.cs` + `{Platform}EventCryptoTests.cs`；
   - 信任根降级为出站换取链（见模板文档 §五）。
4. **协议实现**：按模板文档 §2 组件差异点填各平台协议细节（先 librarian 官方文档核实协议事实）；
5. **接线**：slnx 注册两项目 + 根 README 一览表加行；
6. **验证**：`dotnet build` 0 错误 + 测试全绿 + 全量回归。

## 组件清单（完整形态 8 + 可选第 9）

| 文件 | 组件 | 可选 |
|------|------|------|
| `TKWF.Federation.{Namespace}.csproj` | 0 csproj（依赖接线双模式） | — |
| `{Platform}ApiClient.cs` | 1 出站客户端 | — |
| `{Platform}OauthChannel.cs` | 2 OAuth 通道 | — |
| `{Platform}EventChannel.cs` | 3 事件/回调通道 | ✅ 有入站回调平台 |
| `{Platform}EventCrypto.cs` | 4 验签逻辑类 | ✅ 有入站回调平台 |
| `{Platform}Options.cs` | 5 配置（含 `{Platform}ChannelConfig`——按先例同文件） | — |
| `{Platform}UserInfo.cs` | 6 用户信息 DTO | — |
| `{Platform}FederationServiceCollectionExtensions.cs` | 7 注册扩展 | — |
| `{Platform}AuthorizeUrlBuilder.cs` | 9 authorize URL 构造辅助（WeCom 先例） | ✅ authorize 构造复杂时 |

> 组件 8（测试宿主模板）在 `_Tests/Extension.Federation.Template.Tests/`——见该目录 README 对应说明（或模板文档 §四）。

## 铁律（复制后不得违反）

- **纯库判据**：无 Initializer 无 `[TKWFExtension]` 零持久化零 Store——平台网关纯协议逻辑 = 库；
- **注册扩展禁用 BuildServiceProvider**（临时根容器 + Scoped 捕获异常）——一律 `AddOptions<{Platform}Options>().Configure(o => configure?.Invoke(o))` 延迟应用；
- **通道继承 `DomainServiceBase` + `[DiContractIgnore]`**；channel 业务参数经 Options 注入不占 ctor `IDomainUser` 槽；
- **信任根一票否决**（有入站回调平台）：验签失败即拒，恒定时间比对；漏验 = 可伪造任意 external_uid = 体系崩溃；
- **凭证自持**：生产 AES-GCM 密文或装配注入，永不明文进配置库；
- **不引 AspNetCore**：库纯逻辑 + HttpClient（`Microsoft.Extensions.Http`），入站端点归装配层；
- **单向依赖**：库 → `TKWF.Ext.Federation`（ISsoChannel 契约）——扩展不引库。

<!-- EOF -->
