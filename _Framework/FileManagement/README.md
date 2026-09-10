# TKWF.Ext.FileManagement

> TKWF 扩展：**文件管理**——目录树（FileFolder 物化路径）+ 文件元数据（ManagedFile SHA256/去重）+ **文件版本化 + 配额（V0.2.0）** + 上传/下载/删除/重命名/移动门面。
> 安全校验链：防穿越 + 扩展名白名单 + 大小限制 + SHA256 去重 + ContentType 服务端推导；物理存储委托 `IBlobStorageService`（BlobStoring.Abstractions 契约，不引实现）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.FileManagement` |
| 版本 | v0.2.0（版本化 + 配额；v0.1.0 零迁移） |
| 依赖 | `TKWF.Domain` + `TKWF.Domain.FreeSql`（V0.2.0 配额 SQL 聚合）+ `TKWF.Ext.BlobStoring.Abstractions`（契约，ADR50 L2）+ SG1（框架既有） |
| 数据 | 表 `FileFolder` + `ManagedFile` + `ManagedFileVersion`（V0.2.0；框架 `SyncTables` 统一建表） |

## 架构分层

```
FileFolderEntity / ManagedFileEntity / ManagedFileVersionEntity   # SG1 声明式实体（partial + [DomainGenerateCode]）
├── FileFolderEntityDataService / ManagedFileEntityDataService / ManagedFileVersionEntityDataService  # SG1 DataService（版本/聚合业务方法）
├── IFileFolderStore / IManagedFileStore / IManagedFileVersionStore  # internal 存储抽象（委托 DataService，异常自然传播）
└── IFileManager / FileManager                           # 公开门面（安全校验链 + SHA256 去重 + 版本化 + 配额 + 事务包裹 + Blob 委托）

TKWF.Ext.BlobStoring.IBlobStorageService                 # Abstractions 契约（消费方启用 BlobStoring 或自定义实现提供）
```

- **数据访问红线**：Store/Manager 不注入 `IFreeSql`/`IEntityDAC`——全部经 SG1 DataService 委托；事务由 Manager 层统一管理。
- **依赖倒置（C1/ADR50 L2）**：只引用 `TKWF.Ext.BlobStoring.Abstractions`（`IBlobStorageService`/`BlobInfo`，namespace `TKWF.Ext.BlobStoring`）——不引用 BlobStoring 实现项目。物理文件字节进出全部经契约委托；`ManagedFileEntity` 为唯一业务元数据（P2 裁定，不注入 `IBlobRecordStore`）。
- **配额 SQL 聚合（V0.2.0）**：`SumSizeByFolderIdAsync`/`SumSizeAllAsync` 经 `TKW.Framework.Domain.FreeSql.FreeSqlQueryableExtensions.SumAsync`（SQL SUM 下推，ADR15 聚合分层）——对齐 BackgroundJobs GetStatsAsync 先例；每聚合独立 `QueryForUser()` 起新查询（ISelect 原地可变陷阱）。
- **事务包裹**：`CreateFolder`/`DeleteFolder`/`UploadFile`/`DeleteFile`/`RollbackFile` 等多步写经 `ITransactionManager` `BeginAsync → CommitAsync / 失败 RollbackAsync`。

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
| `UploadFileAsync(folderId?, fileName, content, contentType?)` | **10 步上传流程**（见下）；V0.2.0 版本化：同名不同内容 → 新版本；配额检查（Blob 落盘前） |
| `DownloadFileAsync(id)` | 元数据定位（不存在 → `InvalidOperationException`）→ Blob 读流（缺失 → `FileNotFoundException`，fail-fast）→ 返回 `(Stream, ManagedFileEntity)` |
| `DeleteFileAsync(id)` | 事务：删主表 + **删全部版本行 + Distinct 去重删全部版本 Blob**（V0.2.0；失败/`false` → 日志警告，孤儿容忍） |
| `RenameFileAsync(id, newName)` | 新名安全校验 + 目录内唯一约束冲突 → `InvalidOperationException("该目录下已存在同名文件")`（P3） |
| `MoveFileAsync(id, newFolderId?)` | 目标目录存在守卫 + 目标重名检查 + `FolderId` 更新（null = 移到根级） |

### 版本（`IFileManager` 版本段，V0.2.0）

| API | 说明 |
|-----|------|
| `GetFileVersionsAsync(fileId)` | 版本历史（Version 升序；上限 1000——防高频版本文件全量拉取） |
| `GetFileVersionAsync(fileId, version)` | 单版本详情（精确命中；不存在返回 null） |
| `RollbackFileAsync(fileId, version)` | 回滚——主表指针切目标版本（StoredPath/Sha256/Size）+ **版本表插入新行（Version=max+1，指针复用不复制字节）**；目标版本不存在 → `InvalidOperationException` |

### 上传 10 步流程（D4/D5，评审 C4/C5/C6 修订；V0.2.0 版本化/配额扩展）

```
1. fileName 空/空白 → ArgumentException（先于防穿越——C6：GetFileName("")=="" 可绕过）
2. 防穿越（F7）：拒绝 . / .. 段 + 路径分隔符 + 盘符/冒号 + 路径形式（fail-closed）
3. 扩展名白名单（Path.GetExtension + ToLowerInvariant；".JPG" 大小写归一通过；AllowAnyExtension=false 时白名单外拒绝，F6）
4. 大小校验（C4 双路径）：CanSeek → Length 预检 fail-fast（不读流）；非 seekable → 边复制边计数（超限中断，含实际/限制值）
5. SHA256：对限长副本流式计算（DataPort 先例——副本 MemoryStream 受 Max 有界）
6. 去重（Options.Deduplicate）：GetBySha256Async 命中同目录同名 → 幂等返回既有文件（仅预查优化；V0.2.0：Deduplicate=false 同内容亦幂等返回——P1-3 分支 C）
7. 目录存在守卫（P5）+ **配额检查（V0.2.0，Blob 落盘前预检前移）**：文件数 CountByFolderId / 目录容量 SumSizeByFolderId + 增量 / 全局容量 SumSizeAll + 增量——超限 → InvalidOperationException（含配额信息）
8. ContentType 服务端推导（C5）：扩展名 → FileManagementMimeMap（~35 项，小写含点）；未知 → application/octet-stream；客户端值仅作提示
9. 上传 Blob：IBlobStorageService.UploadAsync(name, stream, contentType) → BlobInfo（先写物理；返回 null → fail-fast）
10. 元数据落库（事务内，V0.2.0 Upsert 三分支——P1-3）：
    新文件 → 主表 Create + 版本表 Version=1；
    已有文件 + 内容变化 → 主表指针更新（StoredPath/Sha256/Size/UpdateTime）+ 版本表 Version=max+1；
    Sha256 相同（Deduplicate=false）→ 幂等返回既有（补偿删新 Blob，不产生冗余版本行）
11. 失败补偿（D16）：best-effort DeleteAsync(storedPath) 清理 Blob + 日志警告 → rethrow
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
| `MaxFilesPerFolder` | `null`（不限制） | **V0.2.0** 目录文件数上限 |
| `MaxFolderSizeBytes` | `null`（不限制） | **V0.2.0** 目录容量上限（字节） |
| `MaxTotalSizeBytes` | `null`（不限制） | **V0.2.0** 全局总容量上限（字节） |

```jsonc
// appsettings.json
{
  "TKWF": {
    "FileManagement": {
      "MaxFileSizeBytes": 52428800,        // 50MB
      "AllowedExtensions": [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx" ],
      "MaxFilesPerFolder": 100,            // V0.2.0：目录最多 100 个文件
      "MaxFolderSizeBytes": 1073741824,    // V0.2.0：目录容量 1GB
      "MaxTotalSizeBytes": 10737418240     // V0.2.0：全局容量 10GB
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
- **版本化语义（V0.2.0）**：上传同名不同内容 → 新版本（`ManagedFileVersionEntity` 版本行，`UX_mfv_file_version` FileId+Version 唯一）；版本行存 `StoredPath` 指针不复制字节（BlobStoring 每次新 guid 路径——旧版本 Blob 天然保留）；回滚 = 主表指针切目标版本 + 新版本行（指针复用，回滚可追溯）；**删除文件清理全部版本行 + Distinct 去重删全部版本 Blob**（回滚共享 StoredPath 不重复删）。
- **配额语义（V0.2.0）**：三配额键可空默认不限制；检查在上传链 Blob 落盘前（预检前移——超限不产生 Blob/补偿）；**并发竞态"先到先得"**（读-比-写非原子，超限最终一致可接受——对齐 ABP 同级缺陷显式声明）。
- **去重与版本交互**：SHA256 去重是内容级（同目录同名同内容幂等返回，Deduplicate=false 亦幂等——P1-3 分支 C），版本是文件级（不同内容才新版本）——语义不冲突，去重命中不产生版本。

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
ManagedFileVersion(id BIGINT PK, file_id BIGINT, version INT, stored_path VARCHAR(1024),   -- V0.2.0
            extension VARCHAR(32), content_type VARCHAR(128) NULL, size BIGINT, sha256 VARCHAR(64),
            uploader_name VARCHAR(128) NULL, create_time TIMESTAMP)                          -- append-only 不可变
-- 索引：UX_FileFolder_Code / UX_ManagedFile_Folder_Name（folder_id,name 联合唯一）/ IX_ManagedFile_Sha256
--      UX_mfv_file_version（file_id,version 联合唯一）/ IX_mfv_file（file_id）
```

- 删除语义：**物理删除**（不声明 `IsDeleted`，`hasSoftDelete:false`）——目录删除保护（子目录+文件计数）、文件删除（元数据+全部版本行+全部版本 Blob）由 Manager 层强制/协调。
- `StoredPath` 为 BlobStoring 相对路径（`{guid}/{name}`），直接透传 Download/Delete；`Sha256` 十六进制小写。
- 版本表 `CreateTime` `CanUpdate=false`（append-only 不可变记录）；版本行 `StoredPath` 可被多行共享（回滚指针复用）——删除时 Distinct 去重。
- 生产建表：框架 `SyncTables` 统一托管（ADR49），**无 VEntity/无 DBA 手工 DDL 前置**。

## 后续演进（v0.3.0+ 候选）

用户级配额（`OwnerId` 列 + `IDomainUser` 注入）；权限/ACL（Permissions.Abstractions D7 接线）；目录移动（改挂——子树 Path 重算方案）；版本保留策略（保留 N 版/定期清理旧 Blob——GC）；版本差异（diff）查看；分片/断点续传（BlobStoring 协同）；缩略图/病毒扫描（管道扩展点）；管理 UI。