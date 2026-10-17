using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>菜单基准实体（表 <c>TKWF_WeChatMenuSnapshot</c>）——本地权威基线（菜单结构的本地基准，与 MenuVersion 同一事务同步）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>每 channel 一行（唯一索引 ChannelId）——<see cref="MenuJson"/> 为微信菜单完整 JSON（大文本）；      发布成功 / 拉取确认后更新（方案 §3.3.1 Oracle P2：MenuSnapshot 与 MenuVersion 一致性——发布成功后同步更新两者，同一事务保证一致）。</para> 的手写 DTO 扩展</summary>
public partial record MenuSnapshotEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}