using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.SecurityLog;

/// <summary>安全日志表实体——记录认证/授权相关安全事件（登录成功/失败、登出、改密、密码重置、账户锁定、注册、挑战）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType，ADR42 D4）。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("SecurityLog")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>     <para><b>只增不改语义（Oracle C2）</b>：本实体无 Update/Delete 业务方法、无     <c>[GenerateController(FromDataService=true)]</c>——安全日志只经 DataService <c>EntityCreateAsync</c> 追加写入。</para></summary>
public partial class SecurityLogEntity
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