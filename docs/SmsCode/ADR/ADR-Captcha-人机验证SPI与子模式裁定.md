# ADR-Captcha-人机验证 SPI 与子模式裁定

## 状态

活跃

## 一、目的与目标

确立 `TKWF.Ext.Captcha`（人机验证）模块的 SPI 形状、拼写约定与子模式演进边界。读者应在 3 句话内明白：CAPTCHA 能力**独立模块**（`TKWF.Ext.Captcha` + `.Captcha.Abstractions` 契约包），源于第二消费者（AuthCenter 注册/找回匿名面）在设计期即出现——不能内嵌为 SmsCode 的私有件；`ICaptchaProvider` 三面 SPI **锁形状不锁媒介**（挑战签发/校验 + 不透明载荷），图形/滑块/点选/行为/评分/音频均为 Provider 类型而非接口维度；C# 类型/命名空间/配置键一律 `Captcha`，**wire 层保留 `chaptcha`** 跟随消费方既有拼写。

## 二、问题

### 问题现象

- DMP 需求要求"图形/滑块/点选/行为验证至少其一可配 + 后续支持外部接口（极验/腾讯云）"——若 SPI 设计为"每种验证类型一个接口"，未来每加一种类型即改接口（接口膨胀、消费方破坏）。
- SmsCode 门禁需要"取题/校验"两端点语义——但 CAPTCHA 不应绑定短信场景（"哪些场景要门禁"归消费方场景注册表，CAPTCHA 场景无关）。
- 命名漂移：DMP 前端按转达文档预留 `chaptcha` 拼写（端点路径/错误码/body 字段）；若 C# 类型也用 `Chaptcha` 则与正确拼写（Captcha）不一致，社区/文档/第三方 SDK 均用 `captcha`。

### 触发场景

- SmsCode 门禁内联（场景注册表 `RequiresCaptcha=true`）→ 需经契约包调 CAPTCHA 校验。
- 未来 AuthCenter 注册/找回匿名面启用门禁——CAPTCHA 的第二消费者（触发即日拆分）。
- 未来极验/腾讯云/阿里接入——经 `ICaptchaProvider` SPI 纯增量（Provider Type 字符串匹配选中）。

### 现有方案不足

- 单一模块方案（CAPTCHA 内嵌 SmsCode）：SmsCode 主包携带人机验证能力 → AuthCenter 消费时被迫引用 SmsCode（域错配）或复制实现（漂移）。
- 多接口 SPI 方案（`IImageCaptchaProvider`/`ISliderCaptchaProvider`/...）：接口维度和实现维度纠缠；Provider 集合注册（`TryAddEnumerableConstructible`）变为多接口多集合——装配与选中逻辑爆炸。
- `Chaptcha` 拼写（跟随 DMP wire）：C# 类型与正确拼写背离——未来消费方/第三方适配器全部继承错误拼写，改名即破坏。

## 三、使用场景

### 适用场景

- SmsCode 门禁内联（M3 接线：`SmsCodeService` 经 `Captcha.Abstractions` 验 captchaToken，缺失/无效 → fail-closed，未装配 → 守卫异常）。
- AuthCenter 注册/找回等匿名面加门禁（第二消费者——设计期已出现，故拆分）。
- 第三方 Provider 接入（极验/腾讯云/阿里）：实现 `ICaptchaProvider` + 注册 + `TKWF:Captcha:EnabledProviderType` 切换。

### 不适用边界

- 评分型/滑块/行为/音频/点选子模式——不做独立接口（Provider 实现维度），**defer 记录**：评分型 `CaptchaVerifyResult.Score` 字段现在就入形状（`double?`，v1 本地图形恒 null），阈值裁决归消费方场景策略；音频变体 LATER（载荷不透明形状不阻断）。
- 场景归属（"哪些场景要门禁"）——归消费方场景注册表，CAPTCHA 本身场景无关。
- 挑战态存储——v1 in-memory（单实例局限写入使用指南；DB 化 v1.1 候选；第三方 Provider 无状态可水平扩展）。

## 四、选项

### 选项 A（采纳）：独立模块 + 三面 SPI（锁形状不锁媒介）+ `Captcha` 拼写

- 描述：`TKWF.Ext.Captcha` 主包 + `.Abstractions` 契约包（零实体零 SG1——纯内存型扩展）；`ICaptchaProvider`（`Type`/`IssueAsync`/`VerifyAsync`）+ `CaptchaChallenge(ChallengeId, Payload, TtlSeconds)`（Payload=Provider 自渲染不透明串）+ `CaptchaVerifyResult(Passed, Score?)`；Provider 集合 `TryAddEnumerableConstructible`（ADR92）；`CaptchaService` 门面负责 Provider 选中（`EnabledProviderType` 精确匹配）+ 取题 IP 频控 + 全局开关 fail-closed（m7：`Enabled=false` 恒守卫异常禁静默放行）；v1 内置 `ImageCaptchaProvider`（SVG 零第三方依赖——System.Drawing 不可跨平台）。
- 优点：媒介与形状解耦（换 Provider 零消费方改动）；第二消费者（AuthCenter）零域错配复用；C# 拼写正确（社区/文档/SDK 一致）；wire 契约跟随消费方既有拼写（DMP 前端按转达预留）。
- 缺点：v1 图形码为弱实现（SVG 基础图形，无抗OCR噪声渲染）；in-memory 挑战态多实例局限（写入指南）。

### 选项 B：内嵌 SmsCode 或沿用 `Chaptcha` 拼写

- 描述：CAPTCHA 作为 SmsCode 私有件；C# 类型用 `Chaptcha` 对齐 DMP wire。
- 优点：短期代码量最小；wire/C# 拼写表面一致。
- 缺点：第二消费者（AuthCenter）被迫引 SmsCode 主包（域错配）；未来正确拼写修正即全量破坏；与社区/第三方 SDK 命名背离。评审否决——b+ 共识即"CAPTCHA 即日拆分"。

## 五、结论

采纳选项 A：独立模块 `TKWF.Ext.Captcha` + `.Captcha.Abstractions`；三面 SPI 锁形状不锁媒介；拼写约定 `Captcha`（类型）vs `chaptcha`（wire）；评分型 `Score` 字段入形状、音频/滑块 defer 记录；m7 fail-closed（`Captcha.Enabled=false` + 场景要求门禁 = 配置冲突，恒守卫异常）。

## 六、关联

- 开发方案：`docs/SmsCode/v0.1.0-SmsCode-Captcha-开发方案.md`（§5 全节——SPI/门面/Provider/Options + M4 拼写约定闭环）
- 配套 ADR：`ADR-SmsCode-模块边界与验证引擎收敛裁定`（模块边界 + X' 裁定链）
- 评审链：oracle-pro6 + oracle3（b+ 共识——CAPTCHA 即日拆分）→ oracle-pro8（M0 方案评审 PASS WITH CONDITIONS，M4 拼写统一闭环）