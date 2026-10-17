using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>交互日志实体（表 <c>TKWF_WeChatInteractionLog</c>）——运维排查刚需（每次回复记录，含触发类型/回复内容/所引素材）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>TriggerType（int）：1=Subscribe / 2=Keyword / 3=Click / 4=Image / 5=Voice / 6=Video / 7=ShortVideo /      8=Location / 9=Link / 10=Scan / 11=Default / 12=None（未命中规则）。</para>      <para>Oracle P2：<see cref="ReplyMaterialId"/> 可空——追溯回复了哪个素材（全局素材 <see cref="MaterialEntity"/> 引用）。</para> 的手写 DTO 扩展</summary>
public partial record InteractionLogEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}