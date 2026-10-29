using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>运营配置·弱身份策略域（表 <c>TKWF_OpsConfig_WeakIdentity</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>弱身份清理策略（能力归 AuthCenter，策略归运营配置面——D03 O2 定稿）：      <see cref="Enabled"/> 总开关 / <see cref="InactiveDays"/> 无交互判定天数 /      <see cref="Action"/>（SoftDelete | PhysicalDelete）/      <see cref="RecoveryWindowDays"/> 恢复窗口（软删后期限内可恢复）/      <see cref="ExcludeBound"/> 排除已绑定账号。</para>      <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 409；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para> 的手写 DTO 扩展</summary>
public partial record WeakIdentityOpsConfigEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}