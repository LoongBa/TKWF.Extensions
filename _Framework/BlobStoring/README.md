# TKWF.Ext.BlobStoring 二进制存储扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (本地文件系统 + FreeSql 记录持久化) | **框架**: .NET 10

**核心约束**: 本地文件系统 Blob 存储、FreeSql 元数据持久化、异常静默处理、SG1 声明式实体、不引入外部存储 SDK

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对二进制大对象（图片、文档、视频、附件）进行上传/下载管理，同时持久化其元数据。

- **存储耦合**：文件上传直接依赖特定存储 SDK（Azure Blob / AWS S3 / MinIO），扩展无法做到存储方式无关。

- **记录缺失**：文件上传后无元数据记录，无法审计上传历史、按名称/内容类型检索。

- **配置割裂**：存储根目录散落在代码中，无法通过 `appsettings.json` 统一管理。

- **异常阻塞**：文件读写失败时抛出异常，阻塞业务调用。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"存储抽象 + 记录存储抽象 + 本地文件系统实现 + 异常静默"** 架构。

### 1. 结构分层

- **存储抽象 (`IBlobStorageService`)**：定义上传/下载/删除/存在性检查操作。扩展提供本地文件系统默认实现。

- **本地实现 (`LocalStorageService`)**：使用 `System.IO.File` / `System.IO.Directory` 在 RootPath 下读写文件，自定义子目录按 Guid 隔离，避免文件名冲突。异常静默处理（不阻塞业务）。

- **记录存储抽象 (`IBlobRecordStore`)**：定义 Blob 元数据记录的 CRUD 操作。扩展提供 FreeSql 默认实现。

- **持久化实现 (`FreeSqlBlobRecordStore`)**：将 `BlobRecordEntity` 元数据持久化到数据库。异常静默处理。

- **声明式实体 (`BlobRecordEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

### 2. 安全语义

- **异常静默**：文件读写 / 记录存取失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IBlobStorageService` / `IBlobRecordStore` Scoped，自动参与当前请求上下文。

### 3. 与主框架的关系

- 本扩展提供 `LocalStorageService` + `FreeSqlBlobRecordStore` 实现 + `BlobStoringExtensionInitializer` 注册。
- 消费方通过 `IBlobStorageService` 上传下载文件、`IBlobRecordStore` 管理元数据，无需关心存储细节。
- V0.1.0 不引入 Azure/S3 SDK——后续版本可平滑替换存储后端（接口不变）。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.BlobStoring` 包，扩展经 `[TKWFExtension]` 被 SG1 自动发现；**V4.9.85 起发现不自动启用**——消费方须在领域初始化器上声明 `[TKWFEnabledExtension]` 白名单，三钩子才接线：

```csharp
// 消费方 .csproj
<ProjectReference Include="..\Framework\BlobStoring\TKWF.Ext.BlobStoring.csproj" />

// 消费方领域初始化器
using TKWF.Ext.BlobStoring;

[TKWFEnabledExtension(typeof(BlobStoringExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // ...
}
```

自动注册：`IBlobStorageService`（默认 `LocalStorageService`）+ `IBlobRecordStore`（默认 `FreeSqlBlobRecordStore`）。

### 2. 上传 / 下载 / 删除

```csharp
// 注入 IBlobStorageService
public class FileService(IBlobStorageService blobStorage)
{
    public async Task<BlobInfo> UploadAsync(Stream stream, string fileName, string contentType)
    {
        // 上传成功返回元数据（Name / Path / ContentType / Size），Path 可直接用于后续下载/删除
        return await blobStorage.UploadAsync(fileName, stream, contentType);
    }

    public async Task<Stream?> DownloadAsync(string blobPath)
    {
        return await blobStorage.DownloadAsync(blobPath); // 不存在返回 null
    }

    public async Task<bool> DeleteAsync(string blobPath)
    {
        return await blobStorage.DeleteAsync(blobPath);
    }

    public async Task<bool> ExistsAsync(string blobPath)
    {
        return await blobStorage.ExistsAsync(blobPath);
    }
}
```

### 3. 元数据记录

```csharp
// 注入 IBlobRecordStore
public class BlobMetadataService(IBlobRecordStore recordStore)
{
    public async Task<BlobRecordEntity?> GetByNameAsync(string name)
    {
        return await recordStore.GetByNameAsync(name);
    }

    public async Task<List<BlobRecordEntity>> GetListAsync(string? contentType = null)
    {
        return (await recordStore.GetListAsync(contentType)).ToList();
    }
}
```

### 4. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "BlobStoring": {
      "RootPath": "./blobs",
      "IsEnabled": true
    }
  }
}
```

### 5. 自定义 IBlobStorageService

若需替换本地文件系统（如 Azure Blob、S3、MinIO）：

```csharp
// 消费方 ConfigureServices 中
services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
```

TryAdd 语义确保消费方实现优先。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IBlobStorageService`** | Blob 存储抽象（上传/下载/删除/检查） | `LocalStorageService`（本扩展） |
| **`IBlobRecordStore`** | Blob 元数据记录存储抽象（CRUD） | `FreeSqlBlobRecordStore`（本扩展） |
| **`BlobInfo`** | Blob 元数据信息模型（Name/Path/ContentType/Size） | 内置 |
| **`BlobRecordEntity`** | Blob 记录表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`BlobStoringUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`BlobStoringOptions`** | 配置选项（`TKWF:BlobStoring` 节） | 内置 |
| **`BlobStoringExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现 |

---

## 五、实体表结构 (Entity Schema)

`BlobRecordEntity` 映射到 `BlobRecord` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| Name | NVARCHAR(256) | Blob 名称（业务键，如文件名） |
| Path | NVARCHAR(1024) | 存储路径（相对 RootPath） |
| ContentType | NVARCHAR(128) | MIME 内容类型（如 image/png） |
| Size | BIGINT | 文件大小（字节） |
| Tags | NVARCHAR(MAX) | 标签（JSON 数组字符串） |
| UploaderName | NVARCHAR(128) | 上传者名称 |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 本地文件系统 Blob 存储（`System.IO`，无外部存储 SDK）
- FreeSql Blob 元数据持久化
- 异常静默处理

### V0.2.0（规划）
- Azure Blob / S3 / MinIO 存储实现（`IBlobStorageService` 不变）
- 分片上传 / 断点续传
- 存储配额与目录结构策略

### V0.3.0（规划）
- CDN / 公开访问 URL 支持
- 文件扫描（病毒检测）/ 图片处理（缩略图）
- 管理 UI（文件浏览/删除/审计）