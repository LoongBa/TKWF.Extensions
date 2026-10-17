using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>素材实体（表 <c>TKWF_WeChatMaterial</c>）——素材库（全局共享——无 ChannelId，规则按 <see cref="MaterialEntity.Id"/> 引用，一个素材多规则复用）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>Kind：1=Text（纯文本，<see cref="Content"/>）/ 2=News（图文，Title/Description/Url/PicUrl）。</para> 的手写 DTO 扩展</summary>
public partial record MaterialEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}