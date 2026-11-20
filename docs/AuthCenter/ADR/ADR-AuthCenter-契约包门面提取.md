# ADR-AuthCenter-契约包门面提取

> **版本**：v1.1（实施阻断后机制修订，Oracle5 裁定）
> **状态**：待重审（v1.0 已批准；v1.1 因实施阻断修订机制，须 Oracle 重审）
> **关联**：开发方案 `docs/AuthCenter/v0.3.0-AuthCenter.Abstractions契约包门面提取-开发方案.md`；EduPlatform 反馈 B1/B2 核实裁定 `docs/转告/2026-11-20-B1B2三方处置转告.md`；G17A §7.6/7.8、G17B §10.2、ADR03（消费方仓）

## 目的与目标

把 AuthCenter 的登录/票据门面契约从主包迁至既有的 `TKWF.Ext.AuthCenter.Abstractions`（V0.2.0 → V0.3.0），使消费方在"契约型成对引用"下可只依赖契约包完成 `User.Use<I门面>()` 门面引用，不再被"契约只存在于实现包"结构性阻塞。目标态：契约面与实现面物理分离、namespace 与行为零变化、fail-closed 语义不削弱。

## 问题

- **消费方债务**：EduPlatform 12 处 F-06 类 TODO 中 3 处属 AuthCenter 侧（AuthRelayRegistry / AuthTicketLoginService / EduPlatformUserHelper），被迫写进程内 stand-in 以维持 Domain 零 `TKWF.Ext.*` 引用（ExtensionBoundaryTests 回归契约）。
- **契约包内容缺口**：`AuthCenter.Abstractions` V0.2.0 仅含 5 个外部身份/SSO 契约，不含登录门面；`IPasswordLoginService`/`IExternalTicketLoginService` 仍在主包。
- **安全语义缺口**：早期 stand-in 的 fail-open（匿名端点 + 按 UId 映射授角色）虽由消费方个案修复，但"先认证后断言"的契约语义未被固化——本次迁移必须同步写入文档与测试，不引入匿名端点一刀切角色守卫（会破坏合法管理员登录）。

## 使用场景

| 场景 | 口径 |
|---|---|
| 消费方经契约包调用登录/票据门面 | `User.Use<IExternalTicketLoginServiceContract>()` / `User.Use<IPasswordLoginServiceContract>()`；引用形态随消费方边界口径决议（Domain 只引契约包或 Web 层成对引用 + 本地端口），本决策不预设 |
| AuthCenter 主包端点生成 | **`[ApiExpose]` 端点承载恒留主包门面**（MapGenerated 程序集不变量）；契约基接口为纯业务签名（无 `[ApiExpose]`）；主包门面继承契约接口并 `new` 重声明带端点标记的方法；端点冒烟（含新增 `POST /login/password`）锁定 |
| 不适用 | AuthSurface 门面（A6 自包含型，禁止建 Abstractions）；明文凭据校验专用契约（另立专项安全评审）；`IExternalTicketRedeemService`（无点名消费调用点，YAGNI 排除） |

## 决策

1. **载体**：既有 `TKWF.Ext.AuthCenter.Abstractions` V0.2.0 → V0.3.0 minor（`AuthCenter.Abstractions/v0.3.0` tag 须用户同意）；namespace 保持 `TKWF.Ext.AuthCenter`，消费方 using 零迁移。
2. **机制（v1.1 修订）**：契约包新增**业务签名基接口** `IExternalTicketLoginServiceContract` / `IPasswordLoginServiceContract` + wire DTO `PasswordLoginRequest` / `ExternalTicketLoginRequest` / `TokenResponse`（namespace 不变）；主包门面继承对应基接口并 `new` 重声明带 `[AllowAnonymousFlag]`+`[ApiExpose]` 的端点方法（标记原文照抄）；Initializer 增 `AddConstructibleService<I*Contract, *Service>()`（既有先例 `AuthCenterExtensionInitializer.cs:115/171/179-180`）；主包实现与端点行为零改动。
3. **依赖接线（v1.1 修订）**：契约基接口仅承载业务签名 + `[AllowAnonymousFlag]`（TKWF.Domain 既有依赖）；`[ApiExpose]` 留主包 → **契约包依赖零变化**（v1.0 拟增的 `TKWF.CodeGeneration.Abstractions` 引用取消）。
4. **安全语义**：门面契约"先认证后断言"；不引入匿名端点一刀切角色守卫；凭据校验专用契约另立专项（频控/尝试记录/防枚举）。
5. **ADR 三问齐备**，与方案/代码同迭代交付；tag/发布待框架包上架 + 用户同意后走 workflow_dispatch。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| 新建 `AuthCenter.Abstractions.V2` 独立包 | 契约型已成立（Federation 活跃消费）；存量包 minor 演进零 using 迁移，新包徒增版本面 |
| 消费方 Domain 直接引主包 | 违反 ADR03/ExtensionBoundaryTests（Domain 零 `TKWF.Ext.*` 引用回归契约） |
| 为 AuthSurface 同步建 Abstractions | 违反 A6 裁定（终端应用不算跨扩展消费方；空包 = 禁止的第三形态） | 的接口整体迁契约包 + 框架 registry 跨程序集定位 | 已实证不可行且非本方案可修：`DefaultExtensionEndpointRegistry.cs:167/:238-243` 按门面程序集找 SG1b 特性（契约包按 G17A §7.8 永不产出）；路由组/模块前缀同样按该程序集取键（`:80/:189-198/:220-231`）——只修查找会致 `/api/auth/*` 静默漂移；SG2 让路链亦按门面程序集判定。WeChatChannels v0.9.0 同型 BLOCKER。已登记非阻塞转告框架组，不作本方案前置 |

## 后果

**正面**：消费方契约引用不再受主包物理位置阻塞；契约面/实现面分离对齐 A6；安全语义随契约包发布传达到所有消费方。

**负面**：契约包 minor 发布需独立 tag（版本面 +1）；每个门面两接口声明（契约基接口 + 主包门面 `new` 重声明），新增方法需同步三处——但实现类必须同时满足两接口，签名漂移编译期即失败（有界代价）。

**缓解**：AuthCenter 全量用例 + Federation（活跃契约消费方）+ 端点冒烟（含新增 `POST /login/password`）+ `Use<I*Contract>` 游客帧/帧外守卫用例 + 仅引契约包编译验证 + 48 项目 L2；失败则单接口回退（独立提交）。
## 修订记录

- v1.1（2026-11-20）：实施首轮命中 MapGenerated 程序集不变量阻断（门面整体迁契约包后端点启动 fail-closed），Oracle5 裁定改契约基接口模式——决策 2/3、使用场景、备选方案、后果均按修订稿执行；v1.0 的"带 `[ApiExpose]` 的接口整体迁契约包 + 框架 registry 跨程序集定位"方案作废。