# WeChatOperations V0.10.0 —— WARN009 契约形状纠正开发方案（内部编排入口不驻契约面）

> **状态**：✅ Oracle 评审已过（oracle-pro5 GLM-5.3，2026-11-10——"附条件通过，条件即 (b) 四步行动序列"）· **日期**：2026-11-10 · **扩展**：WeChatOperations（V0.9.1 → **V0.10.0**，破坏性 minor bump，V0.8.0 门面改名先例）
> **关联**：`转达回复-v0.10.6-网关开关边界与SG2a警告定性-扩展组裁定.md`（standing ruling，本方案按 (d) 修订其 §三）、`微信公众号运营扩展-开发方案.md`（主方案历次迭代）
> **ADR**：`ADR-WeChatOperations-内部编排入口不驻契约面.md`（配套，本迭代新增）
> **Oracle 评审**：PASS WITH CONDITIONS ×2——① oracle-pro5（2026-11-10，问题架构评审）：WARN009 ×3 为**真实契约缺陷的真阳性检出**（非收集面伴随现象）；发现**潜在 GraphQL 暴露面漏洞**（`RouteAsync` `ExposeRest=false` 但 `ExposeGraphQL` 默认 true + 无 `[RequirePermission]`）；最优解 = 契约形状纠正（治本）+ 消费方一行止损（治标）+ 全仓 Expose 语义审计（防扩散）+ 框架转达修正（加固）。**② oracle-pro3（2026-11-10，本方案文档评审，加载 tkwf-design-principles skill）**：PASS WITH CONDITIONS——**7 条条件（P0×1 / P1×3 / P2×3）**：C1[P0] Step 2 核心机制缺陷（`[GenerateController]` Service 目标按**类**提取 public 方法 + B4 发射仅排除显式 `ExposeRest=false` → 迁移后 `RouteAsync` 无标注将**默认暴露 REST 端点** + 生成 handler 委托 `Use<IMessageGatewayService>().RouteAsync()` **CS1061 断编**）→ 类方法补 `[GenerateIgnore]`；C2[P1] 审计范围措辞改"扩展内 9 处"（全仓 18 处：WeChatChannels.Abstractions ×3 + AuthCenter ×6 含 `IFederatedLoginOrchestrator` 同型隐患，登记跟进）；C3[P1] 接口声明顺序 = 契约锚定（`IMessageGatewayService` 居首，`FindFacadeInterface` 取首个非框架接口）；C4[P1] 变异实弹四步化 + 模拟消费方载体明示；C5[P2] 权限字符串修正；C6[P2] 测试适配 9 文件清单；C7[P2] 文档同步四项。**7 条条件全部闭环后进入实施**（本方案已完成条件闭环修订，见 §三/§四/§五/§六）。
> **实施结果**：✅ 已实施（V0.10.0 功能线，2026-11-10——证据见 §五验证计划 + code-reviewer 附条件通过闭环 + 变更记录 2026-11-10）

---

## 一、背景与问题定性

### 1.1 现象复述（项目组转达，事实链已全部核实）

DMP-Lite 消费方双模式编译：

| 模式 | 结果 |
|---|---|
| Project 模式（DMP-Lite.Dev.slnx） | 0 错误，仅 4 CS1574，无 WARN009 |
| DLL 模式（DMP-Lite.slnx，强制 Rebuild） | 0 错误，**3 × `TKWF_SG2a_WARN009`**：`ForwardAction` / `ForwardDegradeStage` / `MessageRouteRuleDescriptor` |

WARN009 语义（框架 v4.10.105）：SG2a 契约接口签名枚举 → DTO 图收集 → 类型符号不可解析（`GetTypeByMetadataName` null）→ 此前静默跳过，现显式诊断（非 Error）。

**触达路径（已核实）**：`IMessageGatewayService.RouteAsync`（`[GenerateController]` 门面 + `[ApiExpose(ExposeRest=false)]`）返回 `GatewayRouteResult`（WeChatOperations 主包 record）→ 其三个字段恰为 `ForwardAction`/`ForwardDegradeStage`/`MessageRouteRuleDescriptor`（`TKWF.Federation.WeChat.MessageGateway` 运行库类型）→ 警告数 ×3 与字段数精确吻合。`ForwardDecision`/`RouteRuleCriteria` 不出现于任何契约签名可达图 → 未被警告。

**根因（引用传递性）**：raw `<Reference>` 非传递；DMP WebApi 显式引用清单（Federation.WeChat/WeChatOperations/WeChatChannels±Abstractions/Federation.Abstractions）缺失 MessageGateway.dll；Project/NuGet 模式传递可见（0 警告）——同一契约因引用模式而异。

### 1.2 原 standing ruling 及其证伪（2026-11-06 裁定 v0.10.6 §三）

原裁定（commit da78aa7）："WARN009 = 生成器收集面**伴随现象**、DMP **零影响**、DMP 侧可 NoWarn 或忽略，**无行动项**；扩展组不将运行库内部模型纳入契约面；等框架收集面精化（`TKWF.Federation.*` 前缀跳过）"。

**oracle-pro5 评审证伪两点**：

1. **"伴随现象/零影响"错误**——WARN009 是**真阳性**：真实缺陷 = "内部编排入口 + 运行库内部模型泄漏进 `[GenerateController]` 契约签名"。DLL 模式复现证伪"零影响"（P2 事实源漂移）。
2. **新发现（比 WARN009 严重一个量级）——潜在 GraphQL 暴露面漏洞**：`RouteAsync` 是 `ExposeRest=false` 但 **`ExposeGraphQL` 默认 true**（`ApiExposeAttribute.cs` L66-72）的方法。HotChocolate 消费方中：扩展门面 ADR114 让路**仅免 REST**（`SG生成器.md` L25 "GraphQL resolver 与 ApiMetadata 照常"）；resolver 发射按 `m.ExposeGraphQL` 过滤（默认 true → 发射）；`RouteAsync` 无 `[RequirePermission]`（L106 刻意"handler 帧内调用"）→ **以无权限 GraphQL query 暴露**，输入含 `WechatEventContext`（`__decrypted_xml` **明文解密报文**）——已认证用户可伪造完整回调上下文驱动幂等键占用 / SyncProxy 出站透传 / 熔断推进 / 日志污染。今日未爆仅因 DMP-Lite 无 HotChocolate（P5 隐式魔法）。

### 1.3 原理判定（设计原则对表）

| # | 判定 | 证据 | 修正方向 |
|---|---|---|---|
| P2 单源 | 冲突 | 契约在三种引用模式三种解析真相 | 契约签名类型任何模式同为契约类型 |
| P4 承诺决定管辖 | 冲突 | `[GenerateController]` = wire 契约信号，却携带非 wire 方法；SG2a 收集面不受 expose 承诺门控 | 契约上只放 wire 方法 |
| P5 显式优于自动 | 冲突（最严重） | `ExposeRest=false` 被当全局关闭开关用，`ExposeGraphQL` 默认 true | 显式声明真实意图 |
| P7 缺省硬失败 | 分裂 | WARN009 本身符合；`RouteAsync` 暴露面 fail-open | 入口不进契约 |
| P13 分层单向 | 冲突证据 | 运行库（下层）内部模型在扩展（上层）契约签名 | 边界投影不上提 |

---

## 二、目标

1. **治本（Step 2，核心）**：`RouteAsync` 撤出 SG2a 扫描的契约面——从 `IMessageGatewayService` 移入独立进程内路由接口 `IWeChatMessageRouter : IDomainService`（**非** `[GenerateController]` catalog 契约），一次修正三种引用模式所有消费方 + 按构造关闭 GraphQL 暴露面。
2. **防扩散（Step 3）**：**WeChatOperations 扩展内** `ExposeRest=false` 语义审计（实测 **9 处**：MessageGatewayService 1 + MenuService 3 + MaterialService 2 + ReplyRuleService 3）——内部-only 撤出契约或显式补 `ExposeGraphQL=false` + 权限核验；**仓内其余 9 处**（WeChatChannels.Abstractions ×3 + AuthCenter ×6）按同款处置规则**登记跟进**（§六.4，不扩本迭代范围、不得静默遗漏）。
3. **验证（P11）**：slnx 0 错误 + 双模式 0 WARN009 + **变异实弹**（临时放回运行库类型方法到契约 → WARN009 必须复燃）。
4. **修正对外口径**：standing ruling §三 amend（伴随现象/零影响/无行动项 strike）+ 框架转达候选修正（`Federation.*` 前缀跳过 → expose 承诺门控）+ 对 DMP 回执修正（框架精化**不**消除根因，正确归属 = 本迭代）。

---

## 三、修复设计

### 3.1 Step 2：`RouteAsync` 撤出契约面（核心）

**现状**（`MessageGatewayService.cs`）：`IMessageGatewayService : IDomainService` 承载 10 个 wire 管理方法（全部 `[RequirePermission]` + `[ApiExpose]`）+ 1 个非 wire 编排方法 `RouteAsync`（`[ApiExpose(ExposeRest=false)]`、无权限标注）。

**改造**：

```csharp
// ① 管理契约保持（wire 面，9 方法不变——删除 RouteAsync；原 10 = 9 管理 + RouteAsync）
public interface IMessageGatewayService : IDomainService { /* ListRouteRulesAsync ... ListForwardLogsAsync */ }

// ② 新增进程内编排接口（非 [GenerateController] catalog 契约；IDomainService 标记供 User.Use 解析；不实现 IAopContract）
public interface IWeChatMessageRouter : IDomainService
{
    Task<GatewayRouteResult> RouteAsync(WechatEventContext ctx, CancellationToken ct = default);
}

// ③ 单实现双接口（零状态迁移：_gatewayEnabledOverride 静态开关仍由管理面翻转、路由面读取，拆类需跨类共享静态状态——否决拆服务类形态）
// ⚠️ 类实现接口顺序 = 契约锚定：FindFacadeInterface 取【首个】非框架直接接口（ControllerGenerator.cs:1683-1696）——
//    IMessageGatewayService 必须居首（其 9 管理方法进 catalog + SG2a），IWeChatMessageRouter 居次（逃逸枚举、不进任何生成面）。
//    顺序倒转 = 9 管理方法整体逃逸 catalog（端点/DTO 消失）+ RouteAsync 重入 SG2a（WARN009 复燃）——双侧皆伤，注释钉死禁止调换。
[DiContractIgnore]
[GenerateController]   // 仅对 IMessageGatewayService 契约生效（控制器转发契约接口方法，路由接口非契约 → 不生成）
public sealed class MessageGatewayService : DomainServiceBase, IMessageGatewayService, IWeChatMessageRouter
{
    /// <summary>⚠️ 必标 [GenerateIgnore]（oracle-pro3 C1[P0] 硬闸门）：
    /// [GenerateController] Service 目标按【类】提取 public 方法（ControllerGenerator.cs:832-841，仅排除 [GenerateIgnore]），
    /// B4 端点发射 [ApiExpose] 回查链 = 类方法特性（L1467）→ 仅 facade 接口（L1452-1455）；发射过滤只排除显式 ExposeRest=false（L1472）。
    /// 无标注 → RouteAsync 落入默认暴露（AuthCenter V0.12.0"防默认暴露"实证）+ 生成 handler 委托 Use<IMessageGatewayService>().RouteAsync → CS1061 断编。
    /// 否决项①：标在 IWeChatMessageRouter 成员上无效（B4 不读非 facade 接口）；否决项②（次选不取）：类方法 [ApiExpose(ExposeRest=false, ExposeGraphQL=false)]
    /// 机械可行但违反"契约注解驻契约接口"惯例。</summary>
    [GenerateIgnore]
    public Task<GatewayRouteResult> RouteAsync(WechatEventContext ctx, CancellationToken ct = default) { ... }
    ...
}
```

**Initializer 双注册**（`AddConstructibleService` 同实现双接口）——**机械可行性已由 oracle-pro3 实证消解**（`DomainServiceCollectionExtensions.cs:229-261`：`TryAdd` 实现类 throw-factory 去重 + 独立 `AddScoped<TInterface>` 守卫工厂，同实现双接口无冲突；每作用域两实例行为无差，唯一共享态为静态 `_gatewayEnabledOverride`）：

```csharp
services.AddConstructibleService<IMessageGatewayService, MessageGatewayService>();
services.AddConstructibleService<IWeChatMessageRouter, MessageGatewayService>();
```

**handler 改道**（`WeChatOperationsReplyHandler.cs` L57-58 属性 + 11 处分支）：`User.Use<IMessageGatewayService>()` → `User.Use<IWeChatMessageRouter>()`。handler 只消费 `Handled`/`Result` 两字段，零逻辑变化。

**保留**：`GatewayRouteResult`/`ForwardAction`/`ForwardDegradeStage`/`MessageRouteRuleDescriptor` 定义位置不动（运行库内部决策模型 + 扩展编排结果类型，测试合法消费的内部可观测性）；`WechatEventContext` 参数随方法离契约。

**同时根除三件事**：① 运行库三类型 + 平台库 `WechatEventContext`（第四类潜在 WARN009——管理型-only 消费方不引 Federation.WeChat 时同炸）全部退出 DTO 图；② GraphQL 漏洞按构造消除（不在任何契约上）；③ 契约变诚实——`IMessageGatewayService` = 权限标注齐全的 wire 管理面 / `IWeChatMessageRouter` = 进程内编排点（2026-11-06 裁定"内部模型不进契约面"意图的**实现版**：裁定说到的，代码没做到，本迭代让代码追上裁定）。

**框架机制风险（实施期复验，评审已实证两大项）**：① ~~SG1b catalog 多接口门面行为~~——oracle-pro3 实证：`FindFacadeInterface` 取**首个**非框架直接接口（ControllerGenerator.cs:1683-1696；ApiServiceGenerator.cs:700-713 catalog 按此枚举）→ **契约锚定 = 声明顺序**，方案 ③ 已注释钉死 `IMessageGatewayService` 居首（C3）；实施期复验 catalog 产物 `InterfaceNames` 断言。② ~~`AddConstructibleService` 双注册形态~~——评审实证无冲突（`DomainServiceCollectionExtensions.cs:229-261`），实施期仅常规验证。③ 未决项收敛为：B4 生成产物（端点路由表）复验——`RouteAsync` 不进端点表（C1 标注后应零命中）。**若仍受阻** → 退路：`RouteAsync` 留契约 + 显式双关 `[ApiExpose(ExposeRest=false, ExposeGraphQL=false)]`（oracle-pro3 实证该形态机械可行——B4 过滤链命中接口方法名回查，退路不踩 F1）+ 依赖 Step 4 框架收集面修正（次优：契约上仍留死方法，且修不到 `WechatEventContext` 参数泄漏）。

### 3.2 Step 3：全仓 `ExposeRest=false` 语义审计（Fix 3）

| 位置 | 方法 | 现状注释意图 | 处置（本迭代裁定） |
|---|---|---|---|
| `MessageGatewayService.cs` L114 | `RouteAsync` | 内部编排入口 | **撤出契约**（3.1 主流程） |
| `MenuService.cs` L52 | `GetSnapshotAsync` | 快照原子读，click key 收集数据源 | **显式补 `ExposeGraphQL=false`**（确在契约上：管理面查询能力；权限核验 **`[RequirePermission("WeChatOperations.Query")]`**（MenuService.cs:51）已有） |
| `MenuService.cs` L70 | `DeleteAsync` | 高操作权限能力，不新增端点 | 同上补 `ExposeGraphQL=false`（权限核验 Manage 已有） |
| `MenuService.cs` L78 | `GetClickKeysAsync` | click key 收集内部能力 | 同上补 `ExposeGraphQL=false` |
| `MaterialService.cs` L37 | `DeleteMaterialAsync` | 高操作权限能力 | 同上补 `ExposeGraphQL=false` |
| `MaterialService.cs` L44 | `GetReferencesAsync` | 素材引用反查内部能力 | 同上补 `ExposeGraphQL=false` |
| `ReplyRuleService.cs` L37 | `DeleteReplyRuleAsync` | 对齐既有端点映射 | 同上补 `ExposeGraphQL=false` |
| `ReplyRuleService.cs` L43 | `GetWelcomeAsync` | 规则列表内部语义 | 同上补 `ExposeGraphQL=false` |
| `ReplyRuleService.cs` L49 | `GetDefaultAsync` | 同内部语义 | 同上补 `ExposeGraphQL=false` |

> **审计口径**：9 处中 8 处留在契约上的价值 = C# 门面访问（内部调用/消费方编程访问），非 wire 暴露；REST 已关（`ExposeRest=false`），GraphQL 必须显式关（`ExposeGraphQL=false`）——"REST 开关 × GraphQL 开关"两个独立开关的语义显式化（P5）。8 处全为无参/简单类型参数 + **权限标注齐全**（逐行核验：GetSnapshotAsync=Query(L51)/DeleteAsync=Manage(L69)/GetClickKeysAsync=Query(L77)/DeleteMaterialAsync=Manage(L36)/GetReferencesAsync=Query(L43)/DeleteReplyRuleAsync=Manage(L36)/GetWelcomeAsync=Query(L42)/GetDefaultAsync=Query(L48)——oracle-pro3 F5 修正后全对），无 `RouteAsync` 的协议上下文入参 + 无权限 + 编排入口问题，补开关即收敛，无需撤出契约（避免无谓破坏面扩大）。
> **README 同步**：README 中"15 端点映射/不新增暴露面"注释语义补注 GraphQL 通道等价关闭。
> **范围声明（C2）**：本表范围 = **WeChatOperations 扩展内**。全仓另有 9 处同型（WeChatChannels.Abstractions `IChannelConfigService.cs` L34/41/59 + AuthCenter `ITokenService.cs` L66/71/92、`IOAuthTicketService.cs` L74/93、`IFederatedLoginOrchestrator.cs` L38）——按同款处置规则登记各扩展跟进（§六.4），**不扩本迭代范围、不静默遗漏**（P2/P7）。

### 3.3 Step 1（DMP 侧止损，非本迭代代码）与 Step 4（框架转达）——不作本迭代代码，登记见 §六

---

## 四、破坏性评估

| 面 | 影响 | 处置 |
|---|---|---|
| `IMessageGatewayService.RouteAsync` 移除（C# 契约） | 破坏性（接口成员删除）——DMP-Lite **从不直调**（handler 在扩展内部运行），`grep` 实证外部零调用 | minor bump（V0.9.1 → V0.10.0，V0.8.0 先例）+ 适配通知 |
| REST/GraphQL 端点面 | **零变化**（前提：C1 的 `[GenerateIgnore]` 标注到位——`RouteAsync` 本就不生成 REST 端点（`ExposeRest=false`），迁出契约后以 `[GenerateIgnore]` 阻断 B4 默认暴露（oracle-pro3 F1 证：无标注将默认发射端点 + 生成 handler CS1061 断编）；`ExposeGraphQL=false` 本就是"内部能力"意图的显式化） | — |
| handler 行为 | 零逻辑变化（换接口取同一实现） | — |
| 测试 | **9 文件全部引用处适配（oracle-pro3 F6/C6 清单）**：`MessageGatewayRoutingTests`（16 处 Use → `IWeChatMessageRouter`）+ `MessageGatewayIdempotencyTests`（4 处 → 同上）+ `WeChatOperationsEndpointHost`（L78 Scoped Replace + L270 `FakeMessageGatewayService : IMessageGatewayService` 假实现 → 双接口适配或路由面换假实现）+ `WeChatOperationsExtensionInitializerTests`（L44 `[InlineData(typeof(IMessageGatewayService))]` → 补 `[InlineData(typeof(IWeChatMessageRouter))]` 注册断言 + **catalog `InterfaceNames` 含 `IMessageGatewayService` 断言**（C3））+ `MessageGatewayServiceTests`（10 处管理面 Use **不动**，仅文件头注释核正）+ `MessageGatewayHandlerTests`/`MessageBusinessSwitchTests`/`WeChatOperationsTestHost`/`WeChatOperationsWebExtensionTests`（纯 doc 注释/端点挂载引用——`WeChatOperationsWebExtension.cs` L38 `MapGenerated<IMessageGatewayService>()` **保持不动**（10 管理端点不变），路由接口非契约不挂载，注释补注） | 本迭代适配 |

---

## 五、验证计划（P11 硬条件）

1. **slnx 构建 0 错误**（L0 编译门——C1 生效的直接证据：若有任何生成面仍引用 `IMessageGatewayService.RouteAsync` 将 CS1061 断编，0 错误即证明路由方法已完全撤出生成面）；
2. **内部测试宿主模拟消费方**：WeChatOperations 206 用例全绿（路由/幂等测试切新接口）+ `WeChatOperationsExtensionInitializerTests` 补 `IWeChatMessageRouter` 注册解析断言 + catalog `InterfaceNames` 断言（C3）；
3. **双模式 0 WARN009（载体明示，C4）**：⚠️ WARN009 在**消费方编译**（SG2a）才触发——观察点必须是 **DLL 模式模拟消费方**。WeChatOperations **无专用 Consumer 项目**，本迭代**新建微型 Consumer 项目**（对齐 `Extension.Permissions.Consumer` 先例：DLL 引用 WeChatOperations + 引用集内**不含** MessageGateway.dll——复现 DMP 引用形态）→ 重编断言 0 WARN009（构建输出 grep `TKWF_SG2a_WARN009`，脚本化）；DMP 侧由其复验真实链路；
4. **变异实弹（C4 四步化纪律）**：① 临时把一个运行库类型返回方法放回 `IMessageGatewayService` 契约 → ② 重编第 3 项 DLL 模式模拟消费方 → ③ 断言构建输出**含** `TKWF_SG2a_WARN009`（证明变异被诊断捕获）→ ④ 还原变异 → 再断言归零（证明是根除非掩蔽）；**变异物不提交**（验证清单显式纪律）；
5. **GraphQL 通道等价核验**：仓库内无 HotChocolate 载体（SG生成器.md L20：消费方无包则 GraphQL 产物全不生成）→ 编译级验证不可行；gate = 声明式特性（`ExposeGraphQL=false` 编译期门，P12 边界接受——oracle-pro3 裁定）；README/使用指南明确"扩展本身无 HotChocolate，GraphQL 门控以 `ExposeGraphQL=false` 显式声明为准"；
6. L1 受影响面：Federation.WeChat（平台库——`WechatEventContext` 定义方）**141** + MessageGateway（运行库）57 + 直接消费方零回归（计数以实施时实际为准）。

---

## 六、转达/登记清单（本迭代交付物之外的对外动作）

1. **standing ruling 修订**：更新 `转达回复-v0.10.6` §三（或以本方案 + 新回执为修订附录）——维持并强化"不将运行库内部模型纳入契约面"、"WARN009 保留不 NoWarn"（金丝雀，这次报对了）；**strike**"伴随现象 / DMP 零影响 / 无行动项"。
2. **框架转达（修正版，替换原"`Federation.*` 前缀跳过"候选）**：SG2a DTO 收集面按 **expose 承诺门控**（P4）——`ExposeRest=false && ExposeGraphQL=false`（全通道关闭）的方法不参与 DTO 图收集；附带建议 `ApiExpose` XML doc 注明"`ExposeRest=false` ≠ 不暴露（GraphQL 默认开）"脚注（本次 9 处误读实证）。
3. **对 DMP-Lite 回执（修正）**：方案 A 一行 Reference **仅作止损**（标注"V0.10.0 后可移除"，且**不 NoWarn**）；**修正此前转达要点第 3 条**——"待框架收集面精化后该场景在所有模式天然消除"不成立：根因消除归本迭代契约形状纠正（零框架依赖、一次修所有模式），框架精化仅是加固（辅助性）；若 DMP 系未来引入 HotChocolate，V0.10.0 落地前须先临时补 `ExposeGraphQL=false` 堵漏洞。
4. **仓内其余 9 处同型登记（C2 新增）**：WeChatChannels.Abstractions `IChannelConfigService.cs` L34/41/59（均带 `[RequirePermission]`）+ AuthCenter `ITokenService.cs` L66/71/92、`IOAuthTicketService.cs` L74/93、**`IFederatedLoginOrchestrator.cs` L38**（"共享子编排为内部服务…不生成端点"——与 `RouteAsync` **完全同型**的"内部编排入口驻契约面"，且属匿名签发链，安全敏感）——按本迭代同款处置规则（显式补 `ExposeGraphQL=false` 或撤出契约）分属 WeChatChannels/AuthCenter 各扩展迭代跟进，**不扩本迭代范围、不得静默遗漏**（P2/P7）。
5. **文档同步四项（C7 新增）**：根 README 版本行（V0.9.1 → V0.10.0 + 功能线说明）+ `_Framework/WeChatOperations/README.md`（版本记录行 + §三端点表注） + `docs/变更记录.md` + 使用指南（`RouteAsync` 移除破坏性说明 + `IWeChatMessageRouter` 新接口消费形态）。

---

## 七、变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-10 | 初稿——oracle-pro5 评审裁定落地立项（Step 2 治本 + Step 3 防扩散 + P11 验证三件套）；修正 standing ruling 证伪结论与对 DMP 转达口径 |
| 2026-11-10 | **oracle-pro3 方案评审 7 条条件闭环**（PASS WITH CONDITIONS）——C1[P0] §3.1 类方法补 `[GenerateIgnore]` 阻断 B4 默认暴露（含否决项/次选明示、§四端点面重述）；C2[P1] 审计范围措辞改"扩展内 9 处" + §六.4 登记仓内其余 9 处（WeChatChannels.Abstractions ×3 + AuthCenter ×6 含 IFederatedLoginOrchestrator 同型）；C3[P1] 接口声明顺序契约锚定（`IMessageGatewayService` 居首 + 实现类注释 + InitializerTests catalog 断言）；C4[P1] §五.4 变异实弹四步化 + 新建微型 Consumer 载体 + 变异不提交纪律；C5[P2] GetSnapshotAsync 权限改 Query；C6[P2] §四测试 9 文件适配清单；C7[P2] §六.5 文档同步四项；双注册机械可行性经评审实证消解（DomainServiceCollectionExtensions.cs:229-261）；计数 9 vs 8 以 9 为准（先前裁定自列清单即 9，自相矛盾）；Federation.WeChat 计数改 141 |
| 2026-11-10 | **code-reviewer 迭代级审核附条件通过 + 条件闭环**（CONDITIONAL PASS）——**P0 #1 闭合**：Consumer csproj NuGet 模式零扩展引用 → CI 必断（实测 `-getItem:Reference` 0 引用实证）——补条件化 ProjectReference 块（DLL 模式保持 raw Reference 复现形态）+FeatureManagement 白名单（TKWF0032 门控实证）+ 双模式构建通过（DLL 0 错误 0 警告 / NuGet 0 错误）；**P1 #2 闭合（选项 b）**：SG2a 仓内观察点确认不存在（框架实证 `ApiServiceGenerator.Initialize` L112-123 检测 `TKW.Framework.Domain.ApiService.MinimalApi.Hosting.MinimalApiEndpointExtensions`——已补 MinimalApi.dll + ApiService.SG.dll 引用实测仍不激活，普通 SDK 测试形态生成器不运行；nuget.org 亦无该包（CPM 未收录））——**「DMP WebApi 重编 0 WARN009（原 3 条消失）」登记为发布门禁必闭项**，机制级门（契约签名断言/catalog 断言/生成物双向验证）已闭环；**P2 #3-#8 文档卫生闭合**（README L128/L58、使用指南 L389/L51、EndpointHost/WebExtensionTests 注释、接口摘要/实现可见性、方案状态行、ADR 端点 32 口径）；**P2 #11 双注册同实现断言新增**（`ManagementAndRouter_ResolveSameImpl_SharedStaticOverride`——同实现 GetType + 静态覆盖共享行为验证，WeChatOperations 207→208）；**P2 #9 登记**（生成物 Service meta 残留 RouteAsync——`EntityMetadataGenerator` 不认 `[GenerateIgnore]`，当前无暴露路径（SG2 读 catalog 不读 Service meta），框架演进议题低优先转达）；**P2 #10 沿用例登记**（ConsumerMetaContext 手写空桩 = Permissions.Consumer 既有反例，本载体不启动真实宿主无即时风险） |