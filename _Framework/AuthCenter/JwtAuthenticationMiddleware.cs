using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

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
/// <para>⚡ DomainUserKey 字面量与主框架 <c>ContextExtractionMiddleware.DomainUserKey</c> 常量值
/// （"DomainUser"）对齐（字面量保留——防跨程序集常量内联漂移）。v4.10.45 起扩展经
/// <c>JwtAuthenticationWebExtension&lt;TUserInfo&gt;</c>（TKWF.Domain.Web 包）装配，无需零跨程序集约束。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public class JwtAuthenticationMiddleware<TUserInfo>(
    RequestDelegate next,
    IOptions<AuthCenterMiddlewareOptions> options,
    Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>> restoreUser,
    ILogger<JwtAuthenticationMiddleware<TUserInfo>> logger)
    where TUserInfo : class, IUserInfo, new()
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>对齐主框架 <c>ContextExtractionMiddleware.DomainUserKey</c> 常量值（"DomainUser"）。</summary>
    private const string DomainUserKey = "DomainUser";

    /// <summary>验签结果 Items 键（V0.6.0 T4）——登出端点取 <see cref="TokenValidationResult.Jti"/> 撤销用。</summary>
    internal const string TokenValidationResultKey = "AuthCenter.TokenValidationResult";

    public async Task InvokeAsync(HttpContext context)
    {
        // 互斥不叠加：Items 已有【已认证】DomainUser（SessionKey 会话恢复先到）→ 跳过（防双向覆盖竞态）。
        // ⚠️ v4.10.45 收敛迁移修正：匿名游客（未认证）不短路——ContextExtraction 阶段 2 恒写游客 DomainUser，
        //    原 `is DomainUser<TUserInfo>` 判定在 UseWebSession 全链下恒真 → JWT 验签永不执行（API 场景失效）；
        //    现按 IsAuthenticated 门控——认证会话先到短路（浏览器场景），游客 + Bearer 走 JWT 验签（API 场景）。
        if (context.Items[DomainUserKey] is DomainUser<TUserInfo> { IsAuthenticated: true })
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
            // V0.5.2（fcbffd1 受理终态，Oracle 裁决）：ITokenVerifier 守卫工厂（AddConstructibleService）需帧内
            // CurrentAopUser 供给 ctor IDomainUser——本中间件处于 HTTP 管线（无 User.Use<T>() AOP 帧），
            // 原裸 GetService 帧外抛守卫 → 带 Bearer 受保护端点 500（真实生产缺陷，测试 Fake 替换掩盖）。
            // 语义：验签 = 匿名请求者证明身份（与 LoginAs/RestoreFromTokenAsync 同属 Guest 行为，非 System 操作）。
            // ContextExtraction 阶段 2（UseWebSession）恒写游客 DomainUser 到 HttpContext.Items["DomainUser"]——
            // 复用作 AOP 帧：guest.Use<ITokenVerifier>() 设 CurrentAopUser=guest → 守卫工厂供给（黑名单查询
            // 经 guest NoAop 直建 DataService）。⚠️ JwtAuthenticationWebExtension 须与 UseWebSession 同装配，
            // 否则无游客 → 降级裸 GetService（守卫工厂抛 = 正确 fail，非静默降级；隔离测试宿主用 Fake 替换兼容）。
            var guest = context.Items[DomainUserKey] as DomainUser<TUserInfo>;
            ITokenVerifier? verifier;
            if (guest != null)
            {
                verifier = guest.Use<ITokenVerifier>();
            }
            else
            {
                verifier = context.RequestServices.GetService<ITokenVerifier>();
            }

            if (verifier == null)
            {
                logger.LogWarning("ITokenVerifier 未注册——JWT 恢复路径跳过（装配方须启用认证中心或注册验证器）");
                await next(context);
                return;
            }

            var tokenResult = await verifier.VerifyAsync(token, context.RequestAborted);
            var user = await restoreUser(context, tokenResult);
            if (user != null)
            {
                context.Items[DomainUserKey] = user;
                // V0.6.0（归层迭代 T4）：验签结果写入 Items——登出端点（AuthCenterWebExtension）取 jti 撤销用
                context.Items[TokenValidationResultKey] = tokenResult;
            }
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
