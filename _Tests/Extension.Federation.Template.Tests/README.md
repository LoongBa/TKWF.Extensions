# Federation.Template.Tests — 平台网关库测试模板（复制起点）

> 本目录 = N1 平台网关通用模板的**测试宿主模板**（组件 8）。权威说明见 `docs/Federation/平台网关模板-开发指南.md` §四 + `_Framework/Federation.Template/README.md` 复制用法。
> **本目录不参与构建**（占位符名非法 C#）——复制替换后注册进 slnx 才构建。

## 文件清单

| 文件 | 内容 | 可选 |
|------|------|------|
| `TKWF.Federation.{Namespace}.Tests.csproj` | 测试项目（xunit.v3 + C 基座 + 双模式引用） | — |
| `{Platform}TestHost.cs` | 测试设施（CreateChannelConfig/Stub Handler/CreateContext）+ 探针门面 `IChannelProbe` + 宿主 `{Platform}ChannelTestHost : TestHostBase`（public BindScope 包装 + 生产路径注册） | — |
| `{Platform}OauthChannelTests.cs` | OAuth 正负用例（ChannelType/ChannelId + 合法 code + 缺 code + API 错误） | — |
| `{Platform}EventChannelTests.cs` | 信任根正负用例（合法验签通过 / 篡改签名拒 / 参数缺失拒 / 密文损坏拒） | ✅ 有入站回调平台 |
| `{Platform}EventCryptoTests.cs` | 验签逻辑正负用例（VerifySignature/DecryptMsg/ExtractFromUserName 正负） | ✅ 有入站回调平台 |

## 复制步骤

1. 复制目录 → `_Tests/Extension.Federation.{平台}.Tests/`；
2. 全局替换 `{Platform}`/`{Namespace}`/`{platform}`（见 `_Framework/Federation.Template/README.md` 占位符表）；
3. 无入站回调平台：删除 `{Platform}EventChannelTests.cs` + `{Platform}EventCryptoTests.cs`；
4. slnx 注册 + `dotnet test` 验证。

> 铁律：测试走**生产路径**（真实 DI + `Add{Platform}FederationChannels` + `BindTestScope` + `User.Use<IChannelProbe>()` AOP 帧内枚举），不手写宿主桩绕过守卫（tkwf-test）。

<!-- EOF -->
