using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>菜单版本实体（表 <c>TKWF_WeChatMenuVersion</c>）——菜单发布历史（内容版本——before/after JSON + 发布人/结果，回滚数据源）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>职责分离（Oracle P1）：<b>内容版本</b>（before/after 完整 JSON + 发布审计人/结果）归本表；      操作审计（who/when/action，不含 JSON）归 <c>MenuPublishLogEntity</c>——发布动作两者同时记录，非发布操作只记 PublishLog。</para>      <para>Result：1=Success / 2=Fail——回滚时加载旧版本 AfterJson 重新发布。</para> 的手写 DTO 扩展</summary>
public partial record MenuVersionEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}