# ADR-Authentication-令牌契约与密钥管理

## 状态

活跃

## 一、目的与目标

冻结认证中心**令牌契约（JWT 最小化 + 生命周期 + 回调承载）**与**密钥管理方案（持久化 RSA + kid 轮换 + 黑名单落库）**，确立「参考业界成熟设计 + 摒弃 DMP-Lite 内存态/未闭环缺陷」的技术基线。读者应在 3 句话内明白：JWT 只带 `sub`（平台内部 id）+ 认证声明、**不含业务角色**；RSA 密钥持久化 + `kid` 轮换 + 黑名单落库（多实例一致、重启不丢）；Refresh Token rotation 落库 + `TokenVersion` 闭环（DMP 未消费缺口补上）。

## 二、问题

### 问题现象

- DMP-Lite 现役 `TokenService`（Platform.Domain 版）**三缺陷**：①RSA 密钥 `static Lazy<RSA>` **内存态**（重启丢密钥 → 全部令牌失效；多实例部署各自生成密钥 → 验签不一致）；②黑名单 `ConcurrentDictionary` **内存态**（重启丢撤销记录 → 已撤销令牌复活）；③`AuthRefreshToken` 直注入 `IEntityDAC`（**违反 TKWF 扩展数据访问红线规则 2**）。
- DMP `PlatformAdmin.TokenVersion` 字段存在但 `RefreshTokenAsync` **未消费**——密码修改后旧 Refresh Token 仍可刷新（未闭环）。
- 需求文档 §三冻结的令牌契约（JWT 最小化）在 DMP 现役实现**未完全落实**——DMP JWT 含 `mid`/`role`（业务角色进令牌，违反"令牌只回答你是谁"裁定）。
- 主框架**无 Bearer JWT handler**（HTTP 认证 = SessionKey 会话恢复）——令牌消费方无法开箱验签/恢复 DomainUser。

### 触发场景

- 认证中心实例部署（DMP-Lite / 教育线 auth.loongba.cn）——密钥从哪来？重启后令牌是否仍有效？
- 多实例部署（负载均衡）——各实例密钥/黑名单不一致？
- 密码修改 / 强制登出后——旧 Refresh Token 是否失效？
- 业务系统消费 JWT——如何验签（本地公钥 / 远程 introspection）？

### 现有方案不足

- DMP 内存 RSA 密钥 + 内存黑名单：重启/多实例场景**不可用**（需求 §六 约束 7 认证中心故障降级要求本地验签兜底，内存态无法满足）。
- DMP JWT 含业务角色：违反令牌最小化（Oracle N1）+ 业务角色本地映射（Oracle I4）裁定。
- DMP `TokenVersion` 未消费：安全缺口（改密后旧 refresh 仍有效）。
- 主框架无 Bearer handler：消费方要自建验签/恢复逻辑（重复建设）。

## 三、使用场景

### 适用场景

- 认证中心实例签发统一 JWT（`sub` = 平台内部 id），多业务系统消费（教育小程序 44 款 / 桃李助手 / 并行业务系统）。
- 多实例高可用部署——密钥/黑名单共享（落库）。
- 密码修改 / 账号安全事件后——TokenVersion 失效旧 refresh。
- 业务系统本地验签兜底（共享公钥，认证中心故障降级——Oracle I3）。

### 不适用边界

- 离线场景（教育小程序离线包）：无认证中心参与，`auth.js` offline 分支恒授权（需求 §1.3 红线）——令牌契约不适用。
- 离线激活码（A01/S01/D09）：独立体系，与本 ADR 无关。
- 业务角色（student/owner/parent/teacher 等）：不进令牌，由各业务系统 `IAuthorizationMapper.MapRoles(sub, claims)` 本地映射（Oracle I4）——本 ADR 只管「你是谁」。

## 四、选项

### 选项 A：持久化密钥 + kid 轮换 + 黑名单落库 + TokenVersion 闭环（选定）

- 描述：RSA 密钥 PEM 文件持久化（`SigningKeyPath`，支持多密钥 `SigningKeys` 列表 + `CurrentKid` 轮换）；撤销经 `AuthTokenBlacklistEntity` 落库（jti + TTL=token 自然过期）；Refresh rotation 校验 `TokenVersion`（不匹配拒绝）+ 重用检测（已撤销 TokenHash 再现 → 全撤销 + Warning）。
- 优点：多实例一致、重启不丢；kid 轮换支持紧急换钥；业界成熟（JWK RFC 7517 + Refresh Rotation BCP）；满足故障降级本地验签。
- 缺点：黑名单表增长（定期清理，v0.2.0 对齐 BackgroundJobs 范式）；密钥文件运维（权限 + 备份）。

### 选项 B：内存态（沿用 DMP 现状）

- 描述：`Lazy<RSA>` + `ConcurrentDictionary` 黑名单 + 不校验 TokenVersion。
- 优点：实现简单。
- 缺点：重启丢密钥/撤销（令牌全失效/已撤销复活）；多实例不一致；TokenVersion 缺口未补——**与需求"参考业界成熟设计 + 摒弃不合理设计"（用户裁定 2026-09-30）直接冲突**。

### 选项 C：引入第三方 JWT 库（System.IdentityModel.Tokens.Jwt）

- 描述：用微软 JWT 库签发/验证。
- 优点：标准实现、久经考验。
- 缺点：新增第三方依赖（扩展仓库"零新增依赖优先"原则）；DMP 两套实现（手写 RSA vs 微软库）已证明手写可行；本扩展手写 RS256（BCL RSA）足够且可控。

## 五、决策

选定：**选项 A——持久化密钥 + kid 轮换 + 黑名单落库 + TokenVersion 闭环**；JWT 编解码**手写**（BCL `System.Security.Cryptography`，零第三方依赖——DMP 新版手写语义对齐）。

理由：
1. **多实例/重启一致性**：密钥/黑名单落库是认证中心高可用部署的硬前提（需求 §11 风险"统一身份源单一故障点"→ 高可用缓解）。
2. **业界成熟**：kid 轮换（JWK RFC 7517）、Refresh Rotation（OAuth 2.0 BCP）、TokenVersion 失效（DMP PlatformAdmin 已有字段但未闭环——本方案补上）。
3. **令牌最小化落实**：JWT 载荷按需求 §3.1 冻结（sub/userId/authType/auth_level/teacher_verified/exp/iat/jti/kid，**无业务角色**）——DMP `mid`/`role` 剔除。
4. **红线合规**：`AuthRefreshTokenEntityDataService` 委托（不直注入 IEntityDAC）——消除 DMP 红线违规。
5. **零第三方依赖**：手写 RS256（对齐 DMP 新版语义 + 扩展仓库原则）。

## 六、后果

### 正面影响

- 认证中心实例可多实例高可用部署（密钥/黑名单共享）；重启后令牌持续有效。
- 密码修改/绑定变更 → TokenVersion++ → 旧 refresh 全部失效（安全闭环）。
- 令牌最小化：泄露连带面小；业务角色由各系统本地映射。
- 消费方本地验签兜底（共享公钥）满足故障降级。

### 负面影响 / 风险

- 黑名单表增长 → v0.2.0 清理任务（对齐 BackgroundJobs 保留天数范式）。
- 密钥文件运维负担（PEM 权限 chmod 600 + 备份 + 紧急轮换流程）→ 使用指南安全章节。
- 手写 JWT 编解码需测试覆盖（Base64Url 边界/载荷签名校验）→ 测试计划含负路径。
- 重用检测全撤销策略较激进 → 使用指南说明（防泄露优先，可能误伤多设备——`DeviceInfo` 列支持定向缓解）。

### 后续待办

- v0.2.0：黑名单/刷新表定期清理任务。
- DMP 迁移（§九）：存量 `AuthRefreshToken` 数据迁移（TokenVersion 初始化 = 当前账号值）+ 密钥交接（PEM 导出/导入）。
- 紧急密钥轮换流程文档化（kid 切换 + 旧密钥保留验证期）。

## 七、关联文档

- 扩展开发方案版本：`docs/Authentication/v0.1.0-Authentication-认证中心-开发方案.md`（v0.1.0，待 Oracle 评审）
- 扩展审核报告版本：待评审后生成
- 上游输入：《TKWF.Extension-认证中心与用户中心-抽取需求合并.md》§三（令牌契约冻结）+ §六 约束 4/7；《认证中心与用户中心-统一认证体系独立立项方案.md》§3.6/§8

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-09-30 | 初始版本——JWT 最小化契约冻结（无业务角色）；RSA 持久化 + kid 轮换；黑名单落库；Refresh rotation + TokenVersion 闭环；手写 RS256 零第三方依赖；摒弃 DMP 内存态三缺陷 |
