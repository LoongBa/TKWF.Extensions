# WeChatOperations V0.10.1 —— 反向治理：3 方法补 REST 面（DMP AdminUI CS1061 裁决落地）

> **状态**：✅ 评审已过（oracle-pro6 裁决 PASS WITH CONDITIONS——本方案即裁决落地，条件见下）· **日期**：2026-11-10 · **扩展**：WeChatOperations（V0.10.0 → **V0.10.1**，非破坏性补丁）
> **关联**：`WARN009契约形状纠正-开发方案.md`（V0.10.0——8 方法补 `ExposeGraphQL=false` 的纠正本身正确）；ADR `ADR-WeChatOperations-内部编排入口不驻契约面.md`（wire 契约纪律——本迭代为 3 方法按纪律显式开 REST）；`转达回复-v0.10.6` §三（standing ruling）
> **Oracle 评审**：oracle-pro6（2026-11-10）——PASS WITH CONDITIONS：V0.4.0「不扩暴露面」对 3 个被 DMP AdminUI 真实业务消费的方法是**历史错误决策**（制定时 AdminUI 不存在，设计前提已变）；P13 关键论证（AdminUI=表现层→REST wire 消费=正确分层；C# 直调=旁路 API 层违反 P13）；CS1061 最可能机制 = SG3 生成客户端面裁剪（V0.10.0 全通道关闭 → SG3 代理剔除 3 方法；扩展 C# 接口反射铁证无缺失）
> **实施结果**：✅ 已实施（V0.10.1 功能线，2026-11-10——slnx 0 错误 + WeChatOperations 208 + Consumer 5 全绿 + B4 生成物 3 端点实证 + 文档同步）

---

## 一、背景与问题

### 1.1 项目组反馈

DMP-Lite AdminUI 3 处 `.razor.cs` 调用报 **CS1061**：
1. `MenuClickMapping.razor.cs:98` → `IMenuService.GetClickKeysAsync(ChannelId)`（click 映射页数据源——头注释"无 REST 端点，保留手动输入占位"= 既有降级）
2. `MaterialLibrary.razor.cs:141` → `IMaterialService.GetReferencesAsync(id)`（素材引用反查——真实功能）
3. `MaterialLibrary.razor.cs:430` → `IMaterialService.DeleteMaterialAsync(materialId)`（素材删除——真实功能）

### 1.2 机制诊断（oracle-pro6 + 扩展侧铁证）

- 扩展侧 C# 接口**机械不可能 CS1061**：refs v0.10.0.0 反射证实 `IMenuService`（7 方法）/`IMaterialService`（4 方法）完整——V0.10.0 未删任何成员（8 处仅补 `ExposeGraphQL=false`）
- 最可能机制：**(b) SG3 生成客户端面裁剪**——V0.10.0 全通道关闭（`ExposeRest=false && ExposeGraphQL=false`）→ 方法退出 ApiMetadata → SG3 生成客户端代理剔除 → DMP（v4.10.92 后已切生成客户端）编译 CS1061
- DMP 证据清单（5 项，#1 硬判据 = CS1061 错误文本含类型全限定名）已交付 DMP 索取

### 1.3 裁决（谁修什么）

**扩展放行（补建 REST 端点）——非"项目自行适配"**。V0.4.0「15 端点映射不扩暴露面」对这 3 方法设计前提已变（AdminUI 已实现），结论须跟进（元原则三问）。P13：AdminUI=表现层 → REST wire 消费=正确分层；C# 直调=旁路 API 层。

---

## 二、目标

1. **3 方法补 REST 面**（P0）：显式 `[ApiExpose(RestRoute/RestMethod)]` + **显式保留 `ExposeGraphQL=false`**（只开 REST 通道——DMP 消费路径为 SG3/REST 客户端，GraphQL 无需求，保持最小暴露面；oracle-pro6 行动 #1 精确化）
2. **Consumer 断言补强**（P0）：3 方法契约级断言（反射 `[ApiExpose]` RestRoute 存在）+ 扩展侧生成物 grep 断言（`_GeneratedEndpointRegistrations.g.cs` 3 新端点）
3. **文档同步**（P1）：扩展 README 端点表 22→25 + 使用指南 3 方法 REST 路径
4. **P14 验证**（P1）：`DeleteMaterialAsync` 引用保护 `InvalidOperationException` → wire 错误映射——**先例验证**（`DeleteEndpointAsync` 同型：REST 端点 + 引用保护异常 L350，框架 B4 执行层映射链路已被 DMP 网关页消费），无需新机制
5. **ADR 延伸议题登记**（P2）：其余 5 方法（GetSnapshotAsync/DeleteAsync/DeleteReplyRuleAsync/GetWelcomeAsync/GetDefaultAsync）评估撤出 wire 契约面——后续迭代

---

## 三、修复设计（3 属性变更 + 断言 + 文档）

### 3.1 端点设计

| 文件 | 方法 | 现状 | 改为 | 权限 |
|---|---|---|---|---|
| `MenuService.cs` L78 | `GetClickKeysAsync` | `[ApiExpose(ExposeRest=false, ExposeGraphQL=false)]` | `[ApiExpose(RestRoute="/channels/{channelId}/menu/click-keys", RestMethod="GET", ExposeGraphQL=false)]` | Query（L77 已有） |
| `MaterialService.cs` L44 | `GetReferencesAsync` | 同上 | `[ApiExpose(RestRoute="/materials/{materialId}/references", RestMethod="GET", ExposeGraphQL=false)]` | Query（L43 已有） |
| `MaterialService.cs` L37 | `DeleteMaterialAsync` | 同上 | `[ApiExpose(RestRoute="/materials/{materialId}", RestMethod="DELETE", ExposeGraphQL=false)]` | Manage（L36 已有） |

- 路由前缀：`/api/wechat-ops`（ExtensionModule 前缀，对齐既有 `/materials`、`/channels/{channelId}/menu` 路由族）
- 参数形态：单路径参数（channelId / materialId）——wire-compatible，无敏感字段
- **显式 `ExposeGraphQL=false`**：只开 REST（SG3 客户端面恢复），GraphQL 维持关闭（最小暴露面——DMP 无 HotChocolate）
- B4 端点发射自动接挂（`_GeneratedEndpointRegistrations.g.cs` 重生成）；WebExtension `MapGenerated` 挂载不变

### 3.2 Consumer 断言补强（P0）

`ConsumerContractShapeTests.cs` 增 1 断言：
```csharp
/// 三方法 REST 面恢复（V0.10.1 反向治理）：[ApiExpose] RestRoute 存在 + ExposeGraphQL=false 保持
[Fact] public void Contract_DisputedMethods_HaveRestRoutes() { ... 反射 IMenuService.GetClickKeysAsync /
  IMaterialService.GetReferencesAsync / DeleteMaterialAsync 的 ApiExposeAttribute → RestRoute 非空 + ExposeGraphQL==false ... }
```
扩展侧生成物 grep 断言（脚本化，构建后）：`_GeneratedEndpointRegistrations.g.cs` 含 `menu/click-keys`、`materials/{materialId}/references`、`/materials/{materialId}` DELETE。

### 3.3 文档（P1）

- 扩展 README：§三端点表注（22→25：+click-keys GET / +references GET / +materials DELETE）+ §六版本记录 V0.10.1 行
- 使用指南：3 方法 REST 路径 + 消费形态（SG3 客户端恢复）
- 变更记录：V0.10.1 行
- 根 README：版本行 V0.10.1

### 3.4 ADR 延伸议题（P2，登记不执行）

`ADR-WeChatOperations-内部编排入口不驻契约面.md` 追加：5 方法（GetSnapshotAsync/DeleteAsync(IMenuService)/DeleteReplyRuleAsync/GetWelcomeAsync/GetDefaultAsync）——无 wire 消费证据、全通道关闭无漏洞——后续迭代评估撤出 `[GenerateController]` 契约面（对齐 ADR"wire 契约只承载 wire 方法"理想；有 wire 替代路径者如 GetWelcomeAsync→ListReplyRulesAsync(sceneType) 优先撤出）。

---

## 四、验证计划

1. slnx 0 错误（L0）
2. WeChatOperations 208 用例全绿（3 方法仅属性变更零行为影响——既有测试锁定）
3. Consumer 断言 4→5 全绿（契约级 RestRoute 断言）
4. 扩展侧生成物 grep：3 新端点出现在 `_GeneratedEndpointRegistrations.g.cs`（B4 生效实证）
5. 双模式构建（DLL refs + NuGet）— 部署脚本链路
6. DMP 复验（发布门禁）：V0.10.1 发布后 CPM lockstep + SG3 重生成 → CS1061 消除 + WARN009 复验（顺带闭环 V0.10.0 门禁）

---

## 五、变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-10 | 初稿——oracle-pro6 裁决落地立项（3 方法补 REST 反向治理 + ExposeGraphQL=false 显式保持 + P14 先例验证 + 5 方法 ADR 延伸登记） |