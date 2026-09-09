using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 值实体（表 FeatureValue）——分层值存储（Global/Tenant/Role/User）。     <para>唯一约束 UX_FeatureValue_Name_Provider（Name+ProviderName+ProviderKey）——注意 SQLite/PostgreSQL     可空唯一索引 NULL 互不相同：Global 层（ProviderKey=null）唯一性由 <see cref="!:FeatureManager.SetValueAsync"/>     应用层保证（预检 + 事务内二次校验，对齐 FileManagement C2 教训）。</para>     <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）。</para></summary>
public partial class FeatureValueEntity
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