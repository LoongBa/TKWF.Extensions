using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Authentication;

/// <summary>令牌黑名单实体——持久化黑名单（摒弃 DMP 内存 ConcurrentDictionary：重启丢失/多实例不一致）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>条目 TTL = 该 token 自然过期时间；验签时查 jti 命中 → 拒绝（经 IMemoryCache 短 TTL 前置过滤）。</para></summary>
public partial class AuthTokenBlacklistEntity
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