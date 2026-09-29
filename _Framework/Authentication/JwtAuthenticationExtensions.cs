using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// JWT 认证 Web 接线扩展（路径 B——装配方 opt-in，方案 §5.8 / §5.13 #5）。
/// <para>对齐 RateLimiting <c>AddTkfwRateLimiting</c> / HealthCheck <c>AddTkfwHealthChecks</c> 先例——消费方 Program.cs 显式调用。</para>
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// 注册 JWT 认证服务：<see cref="ITokenVerifier"/> 默认本地公钥实现（TryAdd——装配层可替换为远程 introspection）+ Options 绑定。
    /// </summary>
    public static IServiceCollection AddJwtAuthentication<TUserInfo>(
        this IServiceCollection services,
        Action<JwtAuthenticationOptions>? configure = null)
        where TUserInfo : class, IUserInfo, new()
    {
        services.TryAddScoped<ITokenVerifier, LocalJwtTokenVerifier>();
        services.AddOptions<JwtAuthenticationOptions>();
        if (configure != null)
            services.Configure(configure);
        return services;
    }

    /// <summary>
    /// 注册 JWT 认证中间件（Bearer JWT → 验签 → 恢复 DomainUser → HttpContext.Items[DomainUserKey]）。
    /// <para>接线位置（Oracle C5 实现裁定）：<c>app.UseWebSession&lt;TUserInfo&gt;()</c>（主框架 ContextExtractionMiddleware
    /// SessionKey 会话恢复）<b>之后</b>、<c>app.UseHttpAuthentication&lt;TUserInfo&gt;()</c>（主框架 HttpAuthenticationMiddleware）<b>之前</b>
    /// ——SessionKey 先到短路（浏览器场景），无 SessionKey 走 Bearer JWT（API 场景），两路径互斥不叠加。见 <see cref="JwtAuthenticationMiddleware{TUserInfo}"/>。</para>
    /// <param name="restoreUser">消费方恢复委托：验签结果 → 已认证 DomainUser（UserHelper 子类内经 CreateUserInstance() 构建——需 Host 上下文）。</param>
    /// </summary>
    public static IApplicationBuilder UseTkfwJwtAuthentication<TUserInfo>(
        this IApplicationBuilder app,
        Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>> restoreUser)
        where TUserInfo : class, IUserInfo, new()
        => app.UseMiddleware<JwtAuthenticationMiddleware<TUserInfo>>(restoreUser);
}
