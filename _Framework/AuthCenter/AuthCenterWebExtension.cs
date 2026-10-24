using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证中心 Web 装配扩展（V0.6.0 归层迭代 T4——原 <c>JwtAuthenticationWebExtension</c> 归层更名 +
/// 内建标准对内端点；v4.10.45 Web 装配钩子 ADR87/D22/G18）——自描述 Web 装配需求，消费方
/// <c>UseWebExtensions</c> 一次声明。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.BeforeAuthentication"/>——ContextExtraction 之后、
/// HttpAuthentication 之前执行（验签恢复 DomainUser 供框架认证判定——P0-1 锚点分桶设计目标）。</para>
/// <para>领域自治（G18 §3）：<see cref="ITokenVerifier"/> 等业务服务留 Domain 钩子
/// <see cref="AuthCenterExtensionInitializer{TUserInfo}"/> 注册——本 Web 钩子仅宿主注册（中间件 + 端点映射）。</para>
/// <para>配置分层（AGENTS §8）：领域决策在 <see cref="AuthCenterOptions"/>（TKWF:AuthCenter）；
/// 暴露面（端点开关）在 <see cref="AuthCenterEndpointOptions"/>（TKWF:AuthCenter:Web——
/// 不镜像领域配置，领域安全行为 Web 层不可关闭）；中间件行为（RejectInvalidToken）在
/// <see cref="AuthCenterMiddlewareOptions"/>（独立 POCO）。</para>
/// <para>委托直传（G18 §3，Oracle P0-2）：<see cref="RestoreUser"/> 实例属性 → <see cref="ConfigureMiddleware"/>
/// 内 <c>UseMiddleware&lt;JwtAuthenticationMiddleware{TUserInfo}&gt;(RestoreUser)</c>（不经 Options——Options 是 POCO）。</para>
/// <para>B3a/B3b（MapGenerated 生成化，Oracle1 2026-10-24 裁定）：11 个手写 minimal API handler
/// （<c>DeclareProtocolEndpoints</c>）删除——协议端点在 <see cref="DeclareEndpoints"/> 经
/// <see cref="IExtensionEndpointRegistry.MapGenerated{TFacade}"/> 挂载门面全部 [ApiExpose] 端点（组路径 =
/// <c>TKWF:Api:RestBasePath</c>（缺省 /api）+ 程序集 [assembly: ExtensionModule("AuthCenter","auth")] 前缀 = /api/auth——
/// <see cref="AuthCenterEndpointOptions.RoutePrefix"/> 不再生效，已标 [Obsolete]）。
/// 业务失败由门面抛类型化异常（DomainException 400 / RateLimitedException 429 / ServiceNotConfiguredException 503），
    /// 框架 <c>WebExceptionMiddleware</c> 统一映射，handler 内 FailReason→HTTP 重映射删除。</para>
/// <para>⚠️ 装配约束（V0.5.2 延伸）：须与 <c>UseWebSession</c> 同装配（ContextExtraction 阶段 2 写游客 DomainUser——
/// 匿名端点 handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 调用守卫工厂门面；未装配 → 抛守卫 = 正确 fail 非静默降级）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public sealed class AuthCenterWebExtension<TUserInfo> : IWebExtension
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>消费方恢复委托（验签结果 → 已认证 DomainUser；UserHelper 子类内经 CreateUserInstance() 构建——需 Host 上下文）。
    /// <para>必填：消费方在 <c>Add&lt;AuthCenterWebExtension&lt;TUserInfo&gt;&gt;(x =&gt; x.RestoreUser = ...)</c> 设置；
    /// 未设置 → ConfigureMiddleware 抛 InvalidOperationException（启动期 fail-fast，避免运行时 NRE）。</para></summary>
    public Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>>? RestoreUser { get; set; }

    /// <summary>端点 Options 编程式配置（可选）——后于配置节 <c>TKWF:AuthCenter:Web</c> 应用。</summary>
    public Action<AuthCenterEndpointOptions>? ConfigureOptions { get; set; }

    /// <summary>中间件行为 Options 编程式配置（可选）——<see cref="AuthCenterMiddlewareOptions.RejectInvalidToken"/>（归层更名自 JwtAuthenticationOptions）。</summary>
    public Action<AuthCenterMiddlewareOptions>? ConfigureMiddlewareOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行；configure 委托先于本方法——实例属性已就绪，零延迟闭包）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // 仅宿主注册：中间件行为 Options + 端点 Options（JwtAuthenticationMiddleware 经 IOptions 解析 RejectInvalidToken）
        services.AddOptions<AuthCenterMiddlewareOptions>();
        if (ConfigureMiddlewareOptions is not null)
            services.Configure(ConfigureMiddlewareOptions);
        services.AddOptions<AuthCenterEndpointOptions>();
        if (ConfigureOptions is not null)
            services.Configure(ConfigureOptions);
    }

    /// <summary>中间件挂载（BeforeAuthentication 锚点桶排空时执行）——RestoreUser 直传中间件构造参数，不经 Options。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
    {
        if (RestoreUser is null)
            throw new InvalidOperationException(
                "AuthCenterWebExtension.RestoreUser 未设置——消费方须在 UseWebExtensions 的 Add 委托内赋值（如 x.RestoreUser = ...）。");
        app.UseMiddleware<JwtAuthenticationMiddleware<TUserInfo>>(RestoreUser);
    }

    /// <summary>
    /// 协议端点声明（B3a/B3b Oracle1 2026-10-24——<c>DeclareProtocolEndpoints</c> 手写 11 handler 删除，F13 生成化）——
    /// 经窄接口 <see cref="IExtensionEndpointRegistry"/> 按门面挂载 [ApiExpose] 生成端点（每程序集一个组 = /api/auth，
    /// R2-3 无 builder 门禁保持）。**每端点开关塌缩为每门面门控**（Oracle 裁定：MapGenerated 暴露映射门面全部
    /// [ApiExpose] 方法——编译期属性无法运行时条件化）：如 <see cref="AuthCenterEndpointOptions.RefreshEndpointEnabled"/> ||
    /// Logout || TokenVerify 三开关任一开 → 挂 <see cref="ITokenService"/>（其暴露 refresh+logout+verify 全部
    /// [ApiExpose] 方法）；需要更细禁用的消费方在网关层处理。
    /// <para>⚠️ 路由前缀：组路径 = <c>TKWF:Api:RestBasePath</c>（缺省 /api）+ <c>TKWF:Api:Modules:AuthCenter:Prefix</c>
    /// （缺省回退程序集 [assembly: ExtensionModule("AuthCenter","auth")] 默认前缀 auth）——<see cref="AuthCenterEndpointOptions.RoutePrefix"/>
    /// 不再生效（[Obsolete]，配置绑定保留避免断裂）。</para></summary>
    public void DeclareEndpoints(IExtensionEndpointRegistry registry)
    {
        var endpointOptions = ResolveOptions(registry.ServiceProvider);

        if (endpointOptions.SmsSendCodeEndpointEnabled || endpointOptions.SmsVerifyEndpointEnabled)
            registry.MapGenerated<ISmsVerificationService>();   // 两开关合并——同门面（send-code + verify 双端点；任一开即挂载，对齐 ITokenService 三开关 OR 保守语义）
        if (endpointOptions.SmsLoginEndpointEnabled)
            registry.MapGenerated<ISmsLoginService>();
        if (endpointOptions.PasswordLoginEndpointEnabled)
            registry.MapGenerated<IPasswordLoginService>();
        if (endpointOptions.ExternalLoginEndpointEnabled)
            registry.MapGenerated<IExternalIdpLoginService>();   // POST /login/external/{channelType}
        if (endpointOptions.TicketLoginEndpointEnabled)
            registry.MapGenerated<IExternalTicketLoginService>();   // POST /login/ticket（V0.10.0 独立路由）
        if (endpointOptions.RefreshEndpointEnabled || endpointOptions.LogoutEndpointEnabled || endpointOptions.TokenVerifyEndpointEnabled)
            registry.MapGenerated<ITokenService>();   // 三开关合并——ITokenService 暴露 refresh+logout+verify 全部 [ApiExpose] 方法
        if (endpointOptions.GrantsEndpointEnabled)
            registry.MapGenerated<IAuthGrantQueryService>();
        if (endpointOptions.TicketExchangeEndpointEnabled)
            registry.MapGenerated<IOAuthTicketService>();
    }

    /// <summary>端点 Options 解析（HealthCheck 先例——IOptions → IConfiguration 直读 → 默认值 三段兜底）。</summary>
    private static AuthCenterEndpointOptions ResolveOptions(IServiceProvider services)
    {
        var options = services.GetService<IOptions<AuthCenterEndpointOptions>>()?.Value;
        if (options is not null) return options;
        var fallback = new AuthCenterEndpointOptions();
        services.GetService<IConfiguration>()?.GetSection("TKWF:AuthCenter:Web").Bind(fallback);
        return fallback;
    }
}