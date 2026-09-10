# TKWF.Ext.Emailing.Abstractions 契约抽象

**状态**: 契约抽象（ADR48 D7 依赖倒置） | **框架**: .NET 10 | **依赖**: 零框架引用（纯 BCL）

## 定位

Emailing 扩展的公共契约——**实现项目与消费方共用**，替代"扩展直接引用另一扩展实现项目"（ADR48 D7 依赖倒置）。

## 内容

| 类型 | 说明 |
|------|------|
| `IEmailSender` | 邮件发送抽象（`SendAsync(EmailMessage, CancellationToken)`） |
| `EmailMessage` | 邮件消息模型（To / From / Subject / Body / IsHtml） |

## 零破坏铁律

- **命名空间保持 `TKWF.Ext.Emailing`**——既有消费方 `using TKWF.Ext.Emailing;` 不变（对齐 Account.Abstractions 迁移先例）。
- 接口/类签名与 Emailing V0.1.0 完全一致，仅程序集从 `TKWF.Ext.Emailing` 迁至 `TKWF.Ext.Emailing.Abstractions`。

## 引用关系

```
Emailing（实现）──ProjectReference──▶ Emailing.Abstractions（契约）
```
