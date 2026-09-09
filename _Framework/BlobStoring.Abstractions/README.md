# TKWF.Ext.BlobStoring.Abstractions 契约抽象

**状态**: 契约抽象（ADR50 L2 依赖倒置） | **框架**: .NET 10 | **依赖**: 零框架引用（纯 BCL）

## 定位

BlobStoring 扩展的公共契约——**实现项目与 FileManagement 等消费方共用**，替代"扩展直接引用另一扩展实现项目"（ADR50 L2 门控 TKWF0022 Error）。

## 内容

| 类型 | 说明 |
|------|------|
| `IBlobStorageService` | Blob 存储抽象（UploadAsync / DownloadAsync / DeleteAsync / ExistsAsync） |
| `BlobInfo` | Blob 元数据信息模型（Name / Path / ContentType / Size） |
| `BlobStoringOptions` | 配置选项（RootPath / IsEnabled，绑定 `TKWF:BlobStoring` 节） |

## 零破坏铁律

- **命名空间保持 `TKWF.Ext.BlobStoring`**——既有消费方 `using TKWF.Ext.BlobStoring;` 不变（对齐 Account.Abstractions 迁移先例）。
- 接口/属性签名与 BlobStoring V0.1.0 完全一致，仅程序集从 `TKWF.Ext.BlobStoring` 迁至 `TKWF.Ext.BlobStoring.Abstractions`。

## 引用关系

```
BlobStoring（实现）──ProjectReference──▶ BlobStoring.Abstractions（契约）
FileManagement（消费方）──ProjectReference──▶ BlobStoring.Abstractions（契约）
```
