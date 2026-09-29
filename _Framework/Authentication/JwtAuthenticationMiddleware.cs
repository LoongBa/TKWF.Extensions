using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// JWT 认证中间件（路径 B——主框架无内置 Bearer handler，认证中心补齐，方案 §5.8）。
/// <para>读 <c>Authorization: Bearer &lt;jwt&gt;</c> → <see cref="ITokenVerifier"/> 验签 → 消费方恢复委托构建已认证
/// <see cref="DomainUser{TUserInfo}"/> → 放入 <c>HttpContext.Items[DomainUserKey]</c>
/// → 后续主框架 <c>HttpAuthenticationMiddleware&lt;TUserInfo&gt;</c>（TKWF.Domain.Web 程序集）读已认证 DomainUser 标记
/// <c>HttpAuthCheckPerformed</c> 通过 → AuthorityFilter 跳过重复认证检查。</para>
/// <para>管线位置（Oracle C5 实现裁定——装配方接线）：<b>主框架 ContextExtractionMiddleware（SessionKey 会话恢复）之后、
/// HttpAuthenticationMiddleware 之前</b>——SessionKey 恢复先到（Items 已有值 → 短路跳过，浏览器场景优先），
/// 无 SessionKey 时走 Bearer JWT（API 场景）——两路径互斥不叠加、零覆盖竞态（方案"先到先得"语义落地）。</para>
/// <para>恢复委托由消费方提供（UserHelper 子类内经 CreateUserInstance() 构建——需 Host 上下文，中间件不触碰 internal Host）。</para>
/// <para>⚡ 零跨程序集依赖：DomainUserKey 字面量与主框架 <c>ContextExtractionMiddleware.DomainUserKey</c> 常量值
/// （"DomainUser"）对齐——TKWF.Domain.Web 无独立 NuGet 包，扩展不引用该程序集（NuGet 模式可构建）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public class JwtAuthenticationMiddleware<TUserInfo>(
    RequestDelegate next,
    IOptions<JwtAuthenticationOptions> options,
    Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>> restoreUser,
    ILogger<JwtAuthenticationMiddleware<TUserInfo>> logger)
    where TUserInfo : class, IUserInfo, new()
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>对齐主框架 <c>ContextExtractionMiddleware.DomainUserKey</c> 常量值（"DomainUser"）。</summary>
    private const string DomainUserKey = "DomainUser";

    public async Task InvokeAsync(HttpContext context)
    {
        // 互斥不叠加：Items 已有 DomainUser（SessionKey 会话恢复先到）→ 跳过（防双向覆盖竞态）
        if (context.Items[DomainUserKey] is DomainUser<TUserInfo>)
        {
            await next(context);
            return;
        }

        // 无 Bearer 头 → 透传（SessionKey 会话恢复路径）
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader)
            || !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var token = authHeader.AsSpan(BearerPrefix.Length).Trim().ToString();
        if (string.IsNullOrEmpty(token))
        {
            await next(context);
            return;
        }

        try
        {
            var verifier = context.RequestServices.GetService<ITokenVerifier>();
            if (verifier == null)
            {
                logger.LogWarning("ITokenVerifier 未注册——JWT 恢复路径跳过（装配方须启用认证中心或注册验证器）");
                await next(context);
                return;
            }

            var tokenResult = await verifier.VerifyAsync(token, context.RequestAborted);
            var user = await restoreUser(context, tokenResult);
            if (user != null)
                context.Items[DomainUserKey] = user;
        }
        catch (AuthenticationException ex)
        {
            logger.LogWarning("Bearer 令牌验证失败：{Reason}", ex.Message);
            if (options.Value.RejectInvalidToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return; // 无效 Bearer → 401 短路（不继续管线）
            }
            // 验签失败透传匿名——由下游 HttpAuthenticationMiddleware / AuthorityFilter 判定
        }

        await next(context);
    }
}
