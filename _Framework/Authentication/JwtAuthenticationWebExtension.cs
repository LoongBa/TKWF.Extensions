using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.Authentication;

/// <summary>
/// JWT 认证 Web 装配扩展（v4.10.45 Web 装配钩子——ADR87/D22/G18，内测期 4 先例收敛迁移）——
/// 自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明（替代旧静态方法
/// <c>AddJwtAuthentication</c> + <c>UseTkfwJwtAuthentication</c> 配对；CHANGELOG 破坏性变更）。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.BeforeAuthentication"/>——ContextExtraction 之后、
/// HttpAuthentication 之前执行（验签恢复 DomainUser 供框架认证判定——P0-1 锚点分桶设计目标）。</para>
/// <para>领域自治（G18 §3 / TKWF_SG1b_WEBEXT_001）：<see cref="ITokenVerifier"/> 等业务服务留 Domain 钩子
/// <see cref="AuthCenterExtensionInitializer{TUserInfo}"/> 注册——本 Web 钩子仅宿主注册。</para>
/// <para>委托直传（G18 §3，Oracle P0-2）：<see cref="RestoreUser"/> 实例属性 → <see cref="ConfigureMiddleware"/>
/// 内 <c>UseMiddleware&lt;JwtAuthenticationMiddleware{TUserInfo}&gt;(RestoreUser)</c>（不经 Options——Options 是 POCO）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public sealed class JwtAuthenticationWebExtension<TUserInfo> : IWebExtension
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>消费方恢复委托（验签结果 → 已认证 DomainUser；UserHelper 子类内经 CreateUserInstance() 构建——需 Host 上下文）。
    /// <para>必填：消费方在 <c>Add&lt;JwtAuthenticationWebExtension&lt;TUserInfo&gt;&gt;(x =&gt; x.RestoreUser = ...)</c> 设置；
    /// 未设置 → ConfigureMiddleware 抛 InvalidOperationException（启动期 fail-fast，避免运行时 NRE）。</para></summary>
    public Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>>? RestoreUser { get; set; }

    /// <summary>JWT 认证 Options 编程式配置（可选）——保留旧 <c>AddJwtAuthentication(configure)</c> 能力；后于默认值应用。</summary>
    public Action<JwtAuthenticationOptions>? ConfigureOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行；configure 委托先于本方法——实例属性已就绪，零延迟闭包）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // 仅宿主注册：Options（JwtAuthenticationMiddleware 经 IOptions<JwtAuthenticationOptions> 解析 RejectInvalidToken）
        services.AddOptions<JwtAuthenticationOptions>();
        if (ConfigureOptions is not null)
            services.Configure(ConfigureOptions);
    }

    /// <summary>中间件挂载（BeforeAuthentication 锚点桶排空时执行）——RestoreUser 直传中间件构造参数，不经 Options。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
    {
        if (RestoreUser is null)
            throw new InvalidOperationException(
                "JwtAuthenticationWebExtension.RestoreUser 未设置——消费方须在 UseWebExtensions 的 Add 委托内赋值（如 x.RestoreUser = ...）。");
        app.UseMiddleware<JwtAuthenticationMiddleware<TUserInfo>>(RestoreUser);
    }
}
