using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FileManagement;

/// <summary>受管文件实体——业务文件元数据（目录内同名唯一 + SHA256 去重 + 物理存储委托 BlobStoring，F4-F10）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("ManagedFile")]</c>；列映射用 FreeSql <c>[Column]</c>（全限定）。</para>     <para>StoredPath 为 <see cref="T:TKWF.Ext.BlobStoring.IBlobStorageService"/> 相对路径（形如 {guid}/{name}）——     物理文件操作全部经 Blob 契约委托，本实体为<b>唯一业务元数据</b>（P2 裁定：不注入 IBlobRecordStore）。</para>     <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）；     删除 = 元数据删除 + Blob 删除（Blob 删失败日志警告，孤儿容忍）。</para>     <para>索引：<c>UX_ManagedFile_Folder_Name</c>（目录内同名唯一——上传/重命名/移动的唯一约束冲突源）     + <c>IX_ManagedFile_Sha256</c>（去重查询）。</para>     <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明；不声明 IsDeleted（物理删除）。</para></summary>
public partial class ManagedFileEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查) 
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}