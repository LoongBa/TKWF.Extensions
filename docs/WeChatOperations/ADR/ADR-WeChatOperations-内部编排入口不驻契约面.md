# ADR-WeChatOperations-内部编排入口不驻契约面

> **版本**：v0.1
> **状态**：已实施（V0.10.0，2026-11-10）
> **关联**：开发方案 `docs/WeChatOperations/WARN009契约形状纠正-开发方案.md`（本 ADR 配套——oracle-pro3 方案评审 PASS WITH CONDITIONS，C1[P0]/C3[P1] 落点）；转达回复 `docs/WeChatOperations/转达回复-v0.10.6-网关开关边界与SG2a警告定性-扩展组裁定.md` §三（standing ruling——本 ADR 按裁定修订（d）执行）；`ADR-WeChatOperations-回复模板与通道级Handler.md`（既有，消息运营回复模型）
> **Oracle 评审**：oracle-pro5（2026-11-10 问题架构评审）——"RouteAsync 撤出 SG2a 扫描的契约面 = 全局最优解"；oracle-pro3（2026-11-10 方案评审）——"C1 类方法 `[GenerateIgnore]` 硬闸门 + C3 声明顺序契约锚定"
> **日期**：2026-11-10

---

## 目的与目标

确立扩展侧契约形状纪律：**`[GenerateController]` 门面接口 = wire 契约声明面——只能承载 wire 方法（权限标注 + expose 声明齐全）；进程内编排入口（同包 handler 帧内调用、无权限标注、签名含平台协议上下文）不得驻留契约面**。落地形态：微信消息网关转发编排核心 `RouteAsync` 自 `IMessageGatewayService` 撤出，移入独立进程内路由接口 `IWeChatMessageRouter : IDomainService`（非 catalog 契约），单实现双接口 + `AddConstructibleService` 双注册，handler 改 `User.Use<IWeChatMessageRouter>()`。读者 3 句话内明白：契约的每一处签名都会被消费方编译期生成链（SG2a DTO 收集 / GraphQL resolver / SG3 客户端）消费——契约上多放一个非 wire 方法 = 给所有消费方所有引用模式永久记账（WARN009 现象 + 潜在无权限 GraphQL 暴露面）。

## 问题

### 问题现象

1. **DMP-Lite DLL 模式强制 Rebuild 出现 3 × `TKWF_SG2a_WARN009`**（`ForwardAction`/`ForwardDegradeStage`/`MessageRouteRuleDescriptor`——`TKWF.Federation.WeChat.MessageGateway` 运行库内部决策模型）：`IMessageGatewayService.RouteAsync`（`[GenerateController]` 门面 + `[ApiExpose(ExposeRest=false)]`）返回 `GatewayRouteResult`（WeChatOperations 主包 record），其三个字段恰为运行库类型 → SG2a 沿契约签名收集 DTO 图 → raw `<Reference>` 非传递 + 消费方 WebApi 引用集缺 MessageGateway.dll → `GetTypeByMetadataName` null → 警告 ×3。Project/NuGet 模式传递可见（0 警告）——**同一契约三种引用模式三种真相**。
2. **潜在 GraphQL 暴露面漏洞**（比 WARN009 严重）：`RouteAsync` 是 `ExposeRest=false` 但 **`ExposeGraphQL` 默认 true** 的方法，无 `[RequirePermission]`——在任意 HotChocolate 消费方中 ADR114 让路仅免 REST、GraphQL resolver 照常发射（`TypeNameConvention` 归类 Query），输入 `WechatEventContext` 含 `__decrypted_xml` 明文报文——已认证用户可伪造完整回调上下文驱动幂等键占用 / SyncProxy 出站透传 / 熔断推进 / 日志污染。
3. **系统性通道语义误读**：`ExposeRest=false` 被 9 处当"全局不暴露"开关用，而 `ApiExpose` 是"REST 开关 × GraphQL 开关"两个独立开关（GraphQL 默认开）——P5 显式优于自动的背离。

### 触发场景

- 任何**消费方 DLL 模式**（raw Reference）编译含 `[GenerateController]` 门面的扩展，且门面契约签名含未显式引用程序集类型 → WARN009 复现（DMP-Lite.slnx 实证）。
- 任何**HotChocolate 消费方**编译含该门面的扩展 → `RouteAsync` 以无权限 GraphQL query 暴露（当前无实际受害方——暴露面语义靠"消费方恰好没装 HotChocolate"成立，P5 隐式魔法）。
- 扩展演进：任何"进程内编排入口顺手放上门面契约"的后续开发——本 ADR 为此类提供判据与形态样板。

### 现有方案的不足

- **方案 A（消费方补一行 Reference）**：最小努力偏差——修解析缺口不修泄漏本体；固化每个 DLL 模式消费方的永久税 + 引入幻影依赖 + 止损后 SG2a 将三个内部决策模型正式物化进 ApiMetadata Types 表（泄漏加深）+ GraphQL 漏洞原样保留。仅可作过渡止损。
- **B1 类型上提（运行库类型进契约包）**：架构性错误——依赖倒置（运行库反向依赖扩展或双份定义 P2 双源）+ 与 2026-11-06 裁定正确内核冲突（内部决策模型 ≠ wire DTO）+ 扩大 wire 面。
- **B2 ExcludeMethods**：机制混同——ExcludeMethods 是 SG1b 控制器生成过滤（ADR129），SG2a `CollectDtoTypes` 自行枚举契约接口全部方法——不同生成器不同阶段；即便有效也是用补丁修"方法本不该在契约上"的错误语义。
- **B3 框架降级匹配（已解析程序集导出类型）**：脆弱启发式替代契约（错误模式表 ADR94 判例）——全局弱化 WARN009 对真实引用断裂的检出。
- **已登记框架候选（`TKWF.Federation.*` 前缀跳过）**：方向对（收集面精化）但键错——命名前缀是启发式非语义契约，将掩蔽未来真实契约断裂；正确键 = expose 承诺（已修正转达）。

## 使用场景

| 场景 | 使用方式 |
|------|---------|
| 网关事件链路帧内转发编排（生产唯一路径） | handler 经 `User.Use<IWeChatMessageRouter>().RouteAsync(ctx, ct)`——路由/幂等/降级/熔断逻辑不变 |
| 网关管理面（规则/端点/状态/日志/开关） | `IMessageGatewayService` 9 管理方法不变（REST 端点面零变化） |
| 消费方编程访问网关编排（未来需求） | 新需求新决策——届时以权限标注 + wire DTO 形状重新设计公开面，不经本接口直接暴露 |
| 测试消费 `GatewayRouteResult` 内部字段（Action/DegradeStage/MatchedRule） | 测试切 `IWeChatMessageRouter`——内部可观测性保留，非 wire 契约 |
| 后续扩展开发（判据样板） | "进程内同包调用、无权限标注、签名含协议上下文" → 专属非契约接口 + `[GenerateIgnore]`（C1），不驻 `[GenerateController]` 契约 |

**不适用边界**：REST/GraphQL 暴露方法仍走 `[GenerateController]` 契约（expose 声明 + 权限标注）；跨扩展消费契约继续走 `.Abstractions` 契约包（L2 门控）；本 ADR 不改变 wire DTO 定义位置（实体内联 / DTO 文件不变）。

---

## 决策

1. **`RouteAsync` 撤出 `IMessageGatewayService`**——新接口 `IWeChatMessageRouter : IDomainService`（**非** `[GenerateController]` catalog 契约，不实现 IAopContract）；`MessageGatewayService : DomainServiceBase, IMessageGatewayService, IWeChatMessageRouter` 单实现双接口；`AddConstructibleService` 双注册（`DomainServiceCollectionExtensions.TryAdd` 实现类 throw-factory 去重 + 独立 `AddScoped<TInterface>` 守卫工厂——同实现双接口机制实证无冲突）。
2. **`[GenerateIgnore]` 类方法标注（C1 硬闸门）**：`[GenerateController]` Service 目标按**类**提取 public 方法（ControllerGenerator 仅排除 `[GenerateIgnore]`），B4 发射仅排除显式 `ExposeRest=false`——无标注将默认发射 REST 端点 + 生成 handler `Use<IMessageGatewayService>().RouteAsync` CS1061 断编。否决"标在 `IWeChatMessageRouter` 成员上"（B4 不读非 facade 接口）；否决类方法 `[ApiExpose(ExposeRest=false, ExposeGraphQL=false)]` 次选（机械可行但违反"契约注解驻契约接口"惯例）。
3. **接口声明顺序 = 契约锚定（C3）**：`FindFacadeInterface` 取首个非框架直接接口——实现类基类列表 `IMessageGatewayService` 必须居首（9 管理方法进 catalog），`IWeChatMessageRouter` 居次（逃逸枚举）；顺序倒转双侧皆伤（管理端点消失 + WARN009 复燃）。已实现类注释 + Initializer 注释钉死；Consumer 项目 catalog 断言防回归。
4. **全仓 `ExposeRest=false` 审计**（WeChatOperations 扩展内 9 处）：`RouteAsync` 撤契约（本 ADR）；其余 8 处（MenuService 3 / MaterialService 2 / ReplyRuleService 3）显式补 `ExposeGraphQL=false`（双开关语义显式化，P5）。仓内其余 9 处（WeChatChannels.Abstractions ×3 + AuthCenter ×6 含 `IFederatedLoginOrchestrator` 同型隐患）按同款规则登记各扩展跟进。
5. **WARN009 保留不 NoWarn**：金丝雀——本次报对了（真阳性）；保持响到所有同类缺陷按本 ADR 形态修复。
6. **框架转达修正**：SG2a DTO 收集面按 expose 承诺门控（全通道关闭方法不贡献 DTO 图）替换"`Federation.*` 前缀跳过"候选；`ApiExpose` XML doc 补"`ExposeRest=false` ≠ 不暴露（GraphQL 默认开）"脚注。

---

## 影响面

| 面 | 影响 | 处置 |
|---|------|------|
| `IMessageGatewayService.RouteAsync` 移除 | 破坏性（C# 接口成员删除）——DMP-Lite 从不直调（handler 在扩展内） | minor bump（V0.10.0）+ 适配通知 |
| REST/GraphQL 端点面 | 零变化（前提：`[GenerateIgnore]` 到位——无默认暴露） | 网关 9 端点不变（总注册面 32 条不变——B4 生成端点表实测，README 同口径；`/gateway/route` 404 弱门 + 生成物 grep 守卫） |
| handler 行为 | 零逻辑变化 | WeChatOperations 207 全绿 |
| 测试 | 路由/幂等/宿主 9 文件适配 + Consumer 4 断言 | 全绿 |

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-11-10 | 初稿——V0.10.0 实施完成（oracle-pro5 裁定落地 + oracle-pro3 条件闭环；P11 验证：slnx 0 错误 / WeChatOperations 207 / Federation.WeChat 145 / MessageGateway 62 / Consumer 4 断言全绿；变异实弹观察点限制记录——SG2a 需 DMP WebApi 形态，仓库内以契约签名机制断言闭环，DMP 复验委托） |
| 2026-11-10 | **V0.10.1 延伸议题登记（反向治理对照）**——oracle-pro6 裁决补充 wire 面边界判据：**「wire 方法（被外部消费方经 wire 消费的真实功能）应给 wire 面」vs「C#-only 内部能力应撤出契约面」**。本 ADR 已按此修正 3 方法（GetClickKeysAsync/GetReferencesAsync/DeleteMaterialAsync——补 REST，见 `反向治理-3方法补REST面-开发方案.md`）。**延伸议题（不阻塞，后续迭代）**：其余 5 方法（`GetSnapshotAsync`/`DeleteAsync`(IMenuService)/`DeleteReplyRuleAsync`/`GetWelcomeAsync`/`GetDefaultAsync`——均全通道关闭、无 wire 消费证据、有 C# 门面或 wire 替代路径如 `ListReplyRulesAsync(sceneType)`）评估**撤出 `[GenerateController]` 契约面**（对齐本 ADR「wire 契约只承载 wire 方法」理想；核准形态 = 契约接口外独立 C# 接口或收编为内部能力）——不开放 REST（反向治理仅针对有真实 wire 消费证据的方法） |