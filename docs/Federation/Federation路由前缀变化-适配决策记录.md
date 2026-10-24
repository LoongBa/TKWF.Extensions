# Federation 端点路由前缀变化（/feberation → /api/feberation）——适配决策记录

> 日期：2026-10-25 ｜ 状态：已实施（扩展 v0.12.0 / DMP v0.9.20）
> 关联：方案 docs/端点契约化与路径所有权-开发方案.md §9.18/9.19；提交 3512167（测试常量 /feberation→/api/feberation）

## 问题
框架 B1-c 端点契约化把 Web 扩展端点组路径改为 `TKWF:Api:RestBasePath`（缺省 /api）+ `[assembly: ExtensionModule]` 模块前缀。Federation 旧默认 `/feberation`（无 /api）→ 新组路径 `/api/feberation`——**破坏性路径变化**，方案文档"消费方零迁移"表述对 Federation 不成立。

## 影响面
1. 微信后台服务器 URL（外部系统，人工重配）
2. 跨服务代理调用（Merchant→Platform）
3. 图文落地链接拼接（FederationEventReplyOptions.RoutePrefix）

## 决策
- 接受路径变化（与 AuthCenter 统一 Module 前缀模型，无可行的局部逃逸口）
- 消费方迁移：三处（见影响面）+ 显式配 EventReply:RoutePrefix=/api/feberation
- DMP v0.9.20 已落地迁移；通知文档已修正"零迁移"错误表述

## 后果
正面：端点前缀统一归 ExtensionModule 单一真源；负面：外部系统（微信后台）需人工重配，须与代码迁移同窗口发布。
