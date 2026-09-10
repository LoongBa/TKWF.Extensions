using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FileManagement;

/// <summary>受管文件版本实体——版本历史（file_id + version 唯一 + stored_path 指针 + append-only 不可变）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("ManagedFileVersion")]</c>；列映射用 FreeSql <c>[Column]</c>（全限定）。</para>     <para>版本模型：int 版本号递增（首版 1，后续 max+1）；正文在 Blob——版本行存 <c>StoredPath</c> 指针不复制字节；     删除 = 主表 + 全部版本行 + 版本 Blob 清理（Distinct 去重，回滚共享 StoredPath）。</para></summary>
public partial class ManagedFileVersionEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查)
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则（版本行 append-only——通常无验证逻辑）
    }
}