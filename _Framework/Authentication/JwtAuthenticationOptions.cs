namespace TKWF.Ext.Authentication;

/// <summary>JWT 认证中间件配置（路径 B——Bearer JWT 恢复 DomainUser；预留扩展位）。</summary>
public class JwtAuthenticationOptions
{
    /// <summary>Bearer 无效时返回 401 并短路（默认 true）；false = 验签失败仅记录日志、透传匿名（由下游 AuthorityFilter 判定）。</summary>
    public bool RejectInvalidToken { get; set; } = true;
}
