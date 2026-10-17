using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>菜单操作审计实体（表 <c>TKWF_WeChatMenuPublishLog</c>）——操作审计（who/when/action，<b>不含 JSON 内容</b>——JSON 归 MenuVersion）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>职责分离（Oracle P1）：发布动作 MenuVersion（记内容）+ MenuPublishLog（记审计）同时记录；      非发布操作（读取/删除/测试）只记本表。</para>      <para>Action（int）：1=Read（拉取当前菜单）/ 2=Create（发布）/ 3=Delete（删除）/ 4=Test（试发测试）。</para> 的手写 DTO 扩展</summary>
public partial record MenuPublishLogEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}