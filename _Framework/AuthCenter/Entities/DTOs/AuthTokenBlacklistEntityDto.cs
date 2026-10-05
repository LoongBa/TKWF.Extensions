using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>令牌黑名单实体——持久化黑名单（摒弃 DMP 内存 ConcurrentDictionary：重启丢失/多实例不一致）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>条目 TTL = 该 token 自然过期时间；验签时查 jti 命中 → 拒绝（经 IMemoryCache 短 TTL 前置过滤）。</para> 的手写 DTO 扩展</summary>
public partial record AuthTokenBlacklistEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}