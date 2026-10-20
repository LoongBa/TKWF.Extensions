using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>认证因子事件实体——append-only 审计事实表（<b>只增不删改</b>：写入即永久，无 Update/Delete 业务语义）。      <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>EventType：1=Bind / 2=Unbind / 3=Verify / 4=Reset；Source：1=Manual / 2=Business / 3=Policy / 4=Provisioning。</para>      <para>Identifier 保留标识快照——因子解绑（物理删除）后仍可追溯该标识的历史事件。</para>      表名 <c>TKWF_AuthFactorEvent</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。 的手写 DTO 扩展</summary>
public partial record AuthFactorEventEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}