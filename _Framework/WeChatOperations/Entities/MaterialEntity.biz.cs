using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>素材实体（表 <c>TKWF_WeChatMaterial</c>）——素材库（全局共享——无 ChannelId，规则按 <see cref="MaterialEntity.Id"/> 引用，一个素材多规则复用）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>Kind：1=Text（纯文本，<see cref="Content"/>）/ 2=News（图文，Title/Description/Url/PicUrl）。</para></summary>
public partial class MaterialEntity
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