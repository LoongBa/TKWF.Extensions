# Federation 端点路由前缀变化（/feberation → /api/feberation）——适配决策记录

> 日期：2026-10-25 ｜ 状态：已实施（扩展 v0.12.0 / DMP v0.9.20）→ **v4.10.85 空串语义更新（推荐路径恢复 /feberation）**
> 关联：方案 docs/端点契约化与路径所有权-开发方案.md §9.18/9.19（+ §9 空串语义 v4.10.85）；提交 3512167（测试常量 /feberation→/api/feberation）；框架提交 ff1377c9（F8 前缀空串语义）

## 问题
框架 B1-c 端点契约化把 Web 扩展端点组路径改为 `TKWF:Api:RestBasePath`（缺省 /api）+ `[assembly: ExtensionModule]` 模块前缀。Federation 旧默认 `/feberation`（无 /api）→ 新组路径 `/api/feberation`——**破坏性路径变化**，方案文档"消费方零迁移"表述对 Federation 不成立。

## 影响面
1. 微信后台服务器 URL（外部系统，v4.10.84 下人工重配；v4.10.85 后零操作）
2. 跨服务代理调用（Merchant→Platform）
3. 图文落地链接拼接（FederationEventReplyOptions.RoutePrefix）

## 决策
- **初版（v4.10.84）**：接受路径变化（与 AuthCenter 统一 Module 前缀模型，无可行的局部逃逸口）→ 消费方迁移三处 + 显式配 EventReply:RoutePrefix=/api/feberation；DMP v0.9.20 已落地
- **v4.10.85 更新（用户裁定 2026-10-25）**：框架新增 F8 前缀空串语义（`UseRestWithoutBasePath()` + `TKWF:Api:RestBasePath=""` 显式空串跳过第一级）→ **Federation 可恢复 `/feberation/*`，微信后台无需人工重配**。推荐消费方 v4.10.85 发布后走此路径（配置空串 + 代码 `/api/feberation` 改回 `/feberation`）

## 后果
正面：端点前缀统一归 ExtensionModule 单一真源；v4.10.85 空串语义恢复旧路径免外部系统人工重配。负面：v4.10.84 窗口内消费方若迁移 `/api/feberation`，v4.10.85 切换时需代码改回（须与配置同窗口发布，否则微信事件推送双向断裂）。
