using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>运营配置·回复文案域（表 <c>TKWF_OpsConfig_ReplyText</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para><see cref="DisabledChannelReplyTemplate"/>：S3 通道停用提示文案（D03 §4.3 S3）；      <see cref="DefaultReplyText"/>：S5 无 handler 命中兜底文案（WeChatOperations 回复 handler 模板兜底优先）。</para>      <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 409；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para> 的手写 DTO 扩展</summary>
public partial record ReplyTextOpsConfigEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}