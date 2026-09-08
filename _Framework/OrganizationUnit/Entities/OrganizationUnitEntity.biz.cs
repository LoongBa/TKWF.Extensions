using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>组织单元实体——树形部门/团队/分组（物化路径 Level/Path，写入维护）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("OrganizationUnit")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>     <para>删除语义（C2 裁定）：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类     <c>hasSoftDelete:false</c>；删除保护（无子节点 + 无关联用户）从源头防误删；已删 Code 可复用。</para>     <para>Code 白名单（C3）：<c>[A-Za-z0-9_.-]</c> 正则校验（Manager 层强制），拒绝 LIKE 通配符与空白——     保证 <c>Path LIKE '{ou.Path}%'</c> 前缀查询无字符歧义。</para></summary>
public partial class OrganizationUnitEntity
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