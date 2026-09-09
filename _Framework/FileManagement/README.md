# TKWF.Ext.FileManagement

> TKWF 扩展：**文件管理**——目录树（FileFolder 物化路径）+ 文件元数据（ManagedFile SHA256/去重）+ 上传/下载/删除/重命名/移动门面。
> 安全校验链：防穿越 + 扩展名白名单 + 大小限制 + SHA256 去重 + ContentType 服务端推导；物理存储委托 `IBlobStorageService`（BlobStoring.Abstractions 契约，不引实现）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.FileManagement` |
| 版本 | v0.1.0（独立起点） |
| 依赖 | `TKWF.Domain` + `TKWF.Ext.BlobStoring.Abstractions`（契约，ADR50 L2）+ SG1（框架既有） |
| 数据 | 表 `FileFolder` + `ManagedFile`（框架 `SyncTables` 统一建表） |

## 架构分层

```
FileFolderEntity / ManagedFileEntity                     # SG1 声明式实体（partial + [DomainGenerateCode]）
├── FileFolderEntityDataService / ManagedFileEntityDataService  # SG1 DataService 骨架（可空 FolderId 双分支谓词 SQL 下推）
├── IFileFolderStore / IManagedFileStore                 # internal 存储抽象（委托 DataService，异常自然传播）
└── IFileManager / FileManager                           # 公开门面（安全校验链 + SHA256 去重 + 事务包裹 + Blob 委托）

TKWF.Ext.BlobStoring.IBlobStorageService                 # Abstractions 契约（消费方启用 BlobStoring 或自定义实现提供）
```

- **数据访问红线**：Store/Manager 不注入 `IFreeSql`/`IEntityDAC`——全部经 SG1 DataService 委托；事务由 Manager 层统一管理。
- **依赖倒置（C1/ADR50 L2）**：只引用 `TKWF.Ext.BlobStoring.Abstractions`（`IBlobStorageService`/`BlobInfo`，namespace `TKWF.Ext.BlobStoring`）——不引用 BlobStoring 实现项目。物理文件字节进出全部经契约委托；`ManagedFileEntity` 为唯一业务元数据（P2 裁定，不注入 `IBlobRecordStore`）。
- **事务包裹**：`CreateFolder`/`DeleteFolder`/`UploadFile`/`DeleteFile` 等多步写经 `ITransactionManager` `BeginAsync → CommitAsync / 失败 RollbackAsync`。

## 核心能力

### 目录（`IFileManager` 目录段）

| API | 说明 |
|-----|------|
| `CreateFolderAsync(code, name, parentId?, sortOrder?)` | 创建目录——Code 白名单（`A-Za-z0-9_.-`）+ 防穿越 + 父存在守卫 + `Level`/`Path` 物化路径（根=`/code/`）+ `Path ≤ 1024` 守卫 + 同级 `SortOrder` 默认末尾 max+1 + 事务 |
| `UpdateFolderAsync(id, name?, sortOrder?)` | 更新（**Code 不可改**——Path 由 Code 构建，C3 裁定；子树 Path 不重算） |
| `RenameFolderAsync(id, newName)` | 重命名（只改 `Name`，子树 Path 不变，O(1)） |
| `DeleteFolderAsync(id)` | **删除保护**：事务内二次确认（子目录计数 + 文件计数均为 0）→ 删；否则 `InvalidOperationException`（D17） |
| `GetFolderTreeAsync()` | 全树组装（内存 nodeMap → childrenMap → 递归；SortOrder 升序；**孤儿检测**——父缺失抛异常，fail-fast） |
| `GetSubFoldersAsync(parentId?)` | 按父目录查直接子目录（null = 根） |

### 文件（`IFileManager` 文件段）

| API | 说明 |
|-----|------|
| `UploadFileAsync(folderId?, fileName, content, contentType?)` | **10 步上传流程**（见下） |
| `DownloadFileAsync(id)` | 元数据定位（不存在 → `InvalidOperationException`）→ Blob 读流（缺失 → `FileNotFoundException`，fail-fast）→ 返回 `(Stream, ManagedFileEntity)` |
| `DeleteFileAsync(id)` | 事务：删元数据 → 删 Blob（失败/`false` → 日志警告，**孤儿容忍**） |
| `RenameFileAsync(id, newName)` | 新名安全校验 + 目录内唯一约束冲突 → `InvalidOperationException("该目录下已存在同名文件")`（P3） |
| `MoveFileAsync(id, newFolderId?)` | 目标目录存在守卫 + 目标重名检查 + `FolderId` 更新（null = 移到根级） |

### 上传 10 步流程（D4/D5，评审 C4/C5/C6 修订）

```
1. fileName 空/空白 → ArgumentException（先于防穿越——C6：GetFileName("")=="" 可绕过）
2. 防穿越（F7）：拒绝 . / .. 段 + 路径分隔符 + 盘符/冒号 + 路径形式（fail-closed）
3. 扩展名白名单（Path.GetExtension + ToLowerInvariant；".JPG" 大小写归一通过；AllowAnyExtension=false 时白名单外拒绝，F6）
4. 大小校验（C4 双路径）：CanSeek → Length 预检 fail-fast（不读流）；非 seekable → 边复制边计数（超限中断，含实际/限制值）
5. SHA256：对限长副本流式计算（DataPort 先例——副本 MemoryStream 受 Max 有界）
6. 去重（Options.Deduplicate）：GetBySha256Async 命中同目录同名 → 幂等返回既有文件（仅预查优化，UX 唯一约束仍兜底，D11）
7. ContentType 服务端推导（C5）：扩展名 → FileManagementMimeMap（~35 项，小写含点）；未知 → application/octet-stream；客户端值仅作提示
8. 上传 Blob：IBlobStorageService.UploadAsync(name, stream, contentType) → BlobInfo（先写物理；返回 null → fail-fast）
9. 元数据落库：事务内 ManagedFileEntity 创建（FolderId 引用守卫 + UX_ManagedFile_Folder_Name 唯一约束冲突 → InvalidOperationException("该目录下已存在同名文件")，P3）
10. 失败补偿（D16）：best-effort DeleteAsync(storedPath) 清理 Blob + 日志警告 → rethrow
```

### 查询（`IFileManager` 查询段）

| API | 说明 |
|-----|------|
| `GetFileAsync(id)` | 按 Id 读文件元数据（不存在返回 null） |
| `GetFilesAsync(folderId?, skip, take?)` | 按目录分页（folderId 可空 → 根级文件；take 缺省 `Options.DefaultPageSize`） |
| `SearchFilesAsync(keyword, skip, take?)` | 按文件名关键字模糊分页（`Name.Contains` 下推） |

## Options 配置（`TKWF:FileManagement`）

| 键 | 默认 | 说明 |
|----|------|------|
| `AllowedExtensions` | `[".jpg",".jpeg",".png",".gif",".pdf",".txt",".csv",".xlsx",".docx",".zip"]` | 扩展名白名单（小写含点） |
| `AllowAnyExtension` | `false` | true = 跳过白名单校验 |
| `MaxFileSizeBytes` | `10485760`（10MB） | 单文件大小上限 |
| `Deduplicate` | `true` | SHA256 去重开关 |
| `DefaultPageSize` | `50` | 查询分页默认页大小 |

```jsonc
// appsettings.json
{
  "TKWF": {
    "FileManagement": {
      "MaxFileSizeBytes": 52428800,        // 50MB
      "AllowedExtensions": [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx" ]
    }
  }
}
```

## 约束与语义

- **Code 不可变（C3）**：目录 `Path` 由 `Code` 构建（根=`/code/`，子=父 Path+Code+`/`）；重命名只改 `Name`，子树 Path 不重算（O(1)），文件按 `FolderId` 引用不受影响。
- **可空 FolderId/父目录**：根级文件 = `FolderId` null；DataService 谓词双分支（`HasValue ? 等值 : IS NULL`）——FreeSql 对 null 参数不自动生成 IS NULL，SQL 下推显式处理。
- **唯一约束即并发边界（P3/P5）**：目录 Code（`UX_FileFolder_Code`）与目录内文件名（`UX_ManagedFile_Folder_Name`）的唯一性由数据库约束兜底；预查仅优化，冲突统一转业务异常（败者补偿清理 Blob）。目录树低频写（百级）——并发 Create 基于过期父快照可能产生 Level/Path 偏差，依赖 `UX_FileFolder_Code` + 最后写入者生效（文档化，对齐 OU）。
- **SQLite 时间语义**：`DateTime`（UTC）显式声明；FreeSql SQLite 存本地墙钟、读出 `Unspecified`——本扩展文件元数据时间仅记录用途（无范围计算），消费方如需严格 UTC 请自行归一（P4）。
- **MIME 字典演进（P6）**：`FileManagementMimeMap` 为内部静态字典，新增扩展名在字典 + 白名单各追加一项即可。
- **BlobStoring 静默模式限制（P7）**：`UploadAsync` 返回 null 表示存储可用性失败 → 本扩展 fail-fast（`InvalidOperationException`），不做静默重试；消费方如启用了 BlobStoring 的静默降级需自行权衡（v0.1.0 无感知）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.FileManagement;
using TKWF.Ext.BlobStoring;   // IBlobStorageService 契约

// 1. 白名单声明（FileManagement + 提供 Blob 存储的 BlobStoring 扩展）
[TKWFEnabledExtension(typeof(FileManagementExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(BlobStoringExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

// 2. 注入使用
public class UploadService(IFileManager fileManager)
{
    public async Task<ManagedFileEntity> UploadAsync(long folderId, string fileName, Stream content)
        => await fileManager.UploadFileAsync(folderId, fileName, content);
}
```

三钩子自动接线。DI 一律 `TryAddScoped`——消费方可自定义 `IFileManager`/`IFileFolderStore`/`IManagedFileStore`/`IBlobStorageService` 实现优先。

## 数据模型

```sql
FileFolder(id BIGINT PK, code VARCHAR(128) UNIQUE, name VARCHAR(128), parent_id BIGINT NULL,
           level INT, path VARCHAR(1024), sort_order INT, create_time TIMESTAMP, update_time TIMESTAMP)
ManagedFile(id BIGINT PK, folder_id BIGINT NULL, name VARCHAR(128), stored_path VARCHAR(1024),
            extension VARCHAR(32), content_type VARCHAR(128) NULL, size BIGINT, sha256 VARCHAR(64),
            uploader_name VARCHAR(128) NULL, create_time TIMESTAMP, update_time TIMESTAMP)
-- 索引：UX_FileFolder_Code / UX_ManagedFile_Folder_Name（folder_id,name 联合唯一）/ IX_ManagedFile_Sha256
```

- 删除语义：**物理删除**（不声明 `IsDeleted`，`hasSoftDelete:false`）——目录删除保护（子目录+文件计数）、文件删除（元数据+Blob）由 Manager 层强制/协调。
- `StoredPath` 为 BlobStoring 相对路径（`{guid}/{name}`），直接透传 Download/Delete；`Sha256` 十六进制小写。
- 生产建表：框架 `SyncTables` 统一托管（ADR49），**无 VEntity/无 DBA 手工 DDL 前置**。

## 后续演进（v0.2.0+ 候选）

文件版本/历史；配额总量（目录/用户）；目录移动（改挂——Code 变更语义，需子树 Path 重算方案）；分片/断点续传（BlobStoring V0.2.0 协同）；缩略图/病毒扫描（管道扩展点）；管理 UI。