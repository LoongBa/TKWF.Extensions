# ADR-SmsCode-模块边界与验证引擎收敛裁定

## 状态

活跃

## 一、目的与目标

确立 `TKWF.Ext.SmsCode`（短信验证码引擎）的模块边界、引擎原语形态与未来收敛出口。读者应在 3 句话内明白：短信验证码**独立立项**（不并入 AuthCenter、不并入 MFA），模块名 `SmsCode`（不含登录编排）；引擎按"未来统一验证核心的胚胎"编写（场景注册表驱动、Subject 不透明键控、零 DMP 逻辑），MFA/AuthCenter 的引擎收敛由**双轨触发器**驱动而非本期迁移；CAPTCHA 门禁**即日拆分**为独立模块 `TKWF.Ext.Captcha`（+ `.Abstractions` 契约包），SmsCode 经契约包可空依赖内联门禁。

## 二、问题

### 问题现象

- DMP-Lite 转达"手机号验证码登录 + CHAPTCHA 人机验证"需求（P1），UserH5 前端已对接等待契约——若不先裁定模块形状就实施，将重蹈"为单需求做一次性模块、下一需求推翻重来"的覆辙。
- 验证能力是几乎所有业务系统的日常重复工作：登录（第一因素）/注册/绑定/重置/MFA（第二因素）/找回密码等共享同一批机制原语（CSPRNG、SHA256、TTL、单次消费 CAS、常量时间比较、频控）——但**共享机制 ≠ 同一原子能力**（见 X' 判定）。
- 框架已有 AuthCenter（内置短信验证码 `SmsVerificationService`）与 MFA（`SmsMfaMethod`）两处独立实现——二者机制原语形态已发生**实证漂移**（AuthCenter `string.Equals` + 大写 hex vs MFA `FixedTimeEquals` + 小写 hex），若再新增第三处实现而不裁定收敛方向，漂移面扩大。

### 触发场景

- DMP 手机号验证码登录（v1 唯一消费方）实施前置——需要冻结契约。
- 未来任一"验证"需求（MFA 第二因素、注册/绑定/重置、找回密码）到达时——需要明确"复用哪个模块、如何演进"。

### 现有方案不足

- 并入 AuthCenter：域不匹配——AuthCenter 是全局账号域（AuthAccount/令牌/票据），DMP 验证码登录属**商户会员域**（MemberInfo/Merchant 会话），耦合两个域违背领域自治。
- 并入 MFA：MFA 挑战的是"已登记因素的持有"（subject=user×method、按 challengeId 句柄验证、per-user 频控），第一因素验证的是"手机号持有权"（subject=phone=投递目标）——**主语语义不同**，MFA 引擎无法直接表达第一因素场景。
- 本期迁移 MFA/AuthCenter：P1 关键路径上为 MFA 猜测接口（SmsCode v1 契约按唯一消费方 DMP 冻结），迁移风险大于收益——留触发器按需驱动。

## 三、使用场景

### 适用场景

- 短信验证码第一因素验证（login/register/bind/reset 族）——Subject=phone=投递目标。
- CAPTCHA 人机验证（防短信接口滥用）——场景注册表要求门禁时，SmsCode 发送前先用 `Captcha.Abstractions` 门面校验凭据（门禁序：CAPTCHA → 频控 → 生成 → 落库 → 发送）。
- 未来按**双轨触发器**收敛：
  - **主触发**：MFA 迭代触及机制路径且 SmsCode 已 v1.0+ → MFA 引擎迁移到 SmsCode 引擎原语（subject 泛化 + 句柄验证 + 每用户频控）。
  - **加速触发**：安全修复回植 ≥2 引擎（如固定时间比较修一处忘一处）→ 启动收敛评审。

### 不适用边界

- 登录编排（会员会话/IdentitySession）——归消费方（DMP `MemberSmsLoginService` 形态），SmsCode 不含。
- MFA 第二因素验证（subject=user×method）——触发器条件未满足前不迁移不猜测接口。
- 语音/邮件/TOTP 通道、外部验证服务型引擎（Twilio Verify 形态）——谱系 LATER。

## 四、选项

### 选项 A（采纳）：独立立项 `TKWF.Ext.SmsCode` + 引擎停止增长但保留收敛出口

- 描述：短信验证码独立模块（C1 裁定）；引擎按"统一核心胚胎"写（场景注册表驱动、Subject 不透明键控、零 DMP 逻辑、错误契约分层）；MFA/AuthCenter 不迁移，六项 ADR 冻结（Subject 不叫 Phone/错误契约分层/依赖方向/sender 适配/评审断言/触发器表）保未来廉价收敛；CAPTCHA 即日拆分独立模块（第二消费者 AuthCenter 注册/找回匿名面设计期即出现）。
- 优点：域自治（商户会员域 vs 全局账号域）；单一事实源（引擎原语正确形态一次写对——FixedTimeEquals/SHA256 小写 hex 直接采用）；零 DMP 逻辑保通用性；收敛路径已冻结（触发器驱动非猜测接口）。
- 缺点：MFA/AuthCenter 短中期存在三处实现（漂移风险由触发器表监控）；SmsCode v1 契约按唯一消费方 DMP 冻结，未来第二消费者可能需小幅演进。

### 选项 B：并入 AuthCenter 或 MFA

- 描述：复用既有扩展，验证码服务加进 AuthCenter（SmsVerificationService 增强）或 MFA。
- 优点：短期省一个新模块。
- 缺点：域耦合（商户会员 vs 全局账号）；MFA 主语语义不匹配（见 §二）；漂移修复方向与既有 AuthCenter 错误形态（string.Equals）纠缠；DMP 消费侧装配路径不明朗。已在 oracle-pro8 C1 评审否决。

### 选项 C：本期立即迁移 MFA/AuthCenter 到新引擎

- 描述：SmsCode 落地时同步重写 MFA/AuthCenter 的验证码实现。
- 优点：一次收敛。
- 缺点：P1 关键路径上为 MFA 猜测接口（X-now 要求 v1 即长出 subject 泛化+句柄验证+每用户频控——SmsCode 契约已冻结，无消费方验证）；迁移风险大收益不确定；与"验证码登录 P1"目标冲突。已在 X' 裁定否决（Y-forever 被实证漂移否决——AuthCenter string.Equals+大写 hex vs MFA FixedTimeEquals+小写 hex 即为漂移实证）。

## 五、结论

采纳选项 A：独立立项 `TKWF.Ext.SmsCode`；引擎按统一核心胚胎写；X' 六项 ADR 冻结保未来廉价收敛；CAPTCHA 即日拆分（拼写约定见 ADR-Captcha）；MFA/AuthCenter 不迁移，由双轨触发器驱动（主触发=MFA 迭代触及机制路径且 SmsCode v1.0+；加速=安全修复回植 ≥2 引擎）。

## 六、关联

- 设计梳理：`docs/SmsCode/验证能力谱系与模块架构-设计梳理.md`（§4.3.1 X' 裁定、§6 NOW 六项冻结）
- 开发方案：`docs/SmsCode/v0.1.0-SmsCode-Captcha-开发方案.md`（M0 评审 PASS WITH CONDITIONS 闭环）
- 配套 ADR：`ADR-Captcha`（SPI 与子模式 + 拼写约定）
- 评审链：oracle-pro8（C1 独立立项）→ oracle2（契约冻结）→ oracle-pro6+oracle3（b+ 胚胎化+CAPTCHA 拆分）→ oracle-pro5（X' 收敛裁定）→ oracle-pro8（M0 方案评审 PASS WITH CONDITIONS）