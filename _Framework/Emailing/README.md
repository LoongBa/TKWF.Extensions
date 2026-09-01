# TKWF.Ext.Emailing 邮件发送扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (邮件发送与记录存储) | **框架**: .NET 10

**核心约束**: SMTP 邮件发送、FreeSql 记录持久化、异常静默处理、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对系统邮件进行发送与记录——如通知邮件、密码重置、营销邮件等。

- **发送耦合**：邮件发送直接依赖特定 SMTP 库（System.Net.Mail / MailKit），扩展无法做到发送方式无关。

- **记录缺失**：邮件发送后无持久化记录，无法审计发送历史、追踪失败原因。

- **配置割裂**：SMTP 配置散落在代码中，无法通过 `appsettings.json` 统一管理。

- **异常阻塞**：邮件发送失败时抛出异常，阻塞业务调用。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"发送抽象 + ORM 无关持久化 + 异常静默"** 架构。

### 1. 结构分层

- **发送抽象 (`IEmailSender`)**：定义 `SendAsync(EmailMessage)` 操作。扩展提供 SMTP 默认实现。

- **SMTP 实现 (`SmtpEmailSender`)**：使用 MailKit 的 `SmtpClient` 发送邮件。异常静默处理（不阻塞业务）。

- **记录存储抽象 (`IEmailRecordStore`)**：定义邮件记录的 CRUD 操作。扩展提供 FreeSql 默认实现。

- **持久化实现 (`FreeSqlEmailRecordStore`)**：将 `EmailRecordEntity` 持久化到数据库。异常静默处理。

- **声明式实体 (`EmailRecordEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

### 2. 安全语义

- **异常静默**：发送失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IEmailSender` / `IEmailRecordStore` Scoped，自动参与当前请求上下文。

### 3. 与主框架的关系

- 本扩展提供 `SmtpEmailSender` + `FreeSqlEmailRecordStore` 实现 + `EmailingExtensionInitializer` 注册。
- 消费方通过 `IEmailSender` 发送邮件，无需关心 SMTP 细节。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Emailing` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(EmailingExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`IEmailSender`（默认 `SmtpEmailSender`）+ `IEmailRecordStore`（默认 `FreeSqlEmailRecordStore`）。

### 2. 发送邮件

```csharp
// 注入 IEmailSender
public class MyService(IEmailSender emailSender)
{
    public async Task SendWelcomeEmailAsync(string userEmail)
    {
        await emailSender.SendAsync(new EmailMessage
        {
            To = userEmail,
            Subject = "Welcome!",
            Body = "<h1>Welcome to our platform!</h1>",
            IsHtml = true
        });
    }
}
```

### 3. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "Emailing": {
      "SmtpHost": "smtp.example.com",
      "SmtpPort": 587,
      "SmtpUser": "user@example.com",
      "SmtpPassword": "your-password",
      "DefaultFrom": "noreply@example.com",
      "IsEnabled": true
    }
  }
}
```

### 4. 自定义 IEmailSender

若需替换 `SmtpEmailSender`（如 SendGrid、Amazon SES）：

```csharp
// 消费方 ConfigureServices 中
services.AddScoped<IEmailSender, SendGridEmailSender>();
```

TryAdd 语义确保消费方实现优先。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IEmailSender`** | 邮件发送抽象 | `SmtpEmailSender`（本扩展） |
| **`IEmailRecordStore`** | 邮件记录存储抽象 | `FreeSqlEmailRecordStore`（本扩展） |
| **`EmailMessage`** | 邮件消息模型 | record 类型 |
| **`EmailRecordEntity`** | 邮件记录表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`EmailingUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`EmailingOptions`** | 配置选项（`TKWF:Emailing` 节） | 内置 |
| **`EmailingExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现（能力清单）+ 消费方 `[TKWFEnabledExtension]` 白名单启用 |

---

## 五、实体表结构 (Entity Schema)

`EmailRecordEntity` 映射到 `EmailRecord` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| To | NVARCHAR(512) | 收件人（多个以逗号分隔） |
| From | NVARCHAR(256) | 发件人地址 |
| Subject | NVARCHAR(512) | 邮件主题 |
| Body | NVARCHAR(MAX) | 邮件正文 |
| IsHtml | BIT | 是否为 HTML 格式正文 |
| Status | NVARCHAR(32) | 发送状态（Pending / Sent / Failed） |
| ErrorMessage | NVARCHAR(MAX) | 错误信息（发送失败时记录） |
| RetryCount | INT | 重试次数 |
| CreateTime | DATETIME | 记录创建时间 |
| SendTime | DATETIME | 实际发送时间（发送成功时记录） |

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- SMTP 邮件发送（MailKit）
- FreeSql 邮件记录存储
- 异常静默处理

### V0.2.0（规划）
- 发送重试策略（指数退避）
- 邮件模板引擎
- 批量发送支持

### V0.3.0（规划）
- 多 SMTP 提供者支持
- 邮件发送统计分析
- 附件支持
