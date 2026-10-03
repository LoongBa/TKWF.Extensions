using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志扩展初始化器——经 <c>[TKWFExtension("SecurityLog")]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 Options（<c>TKWF:SecurityLog</c>）+ SG1 DataService +
    ///       <see cref="ISecurityLogStore"/> + <see cref="ISecurityLogQueryService"/> + <see cref="ISecurityLogAnalyticsService"/>
    ///       （均 <c>AddConstructibleService</c>——接口可构造守卫工厂 + 实现类 throw-factory，消费方 <c>User.Use&lt;接口&gt;()</c> 解析）</item>
    /// <item><see cref="ConfigureFilters"/>——不自动注册过滤器（消费方 opt-in：<c>ConfigureGlobalFilters</c> 中
    ///       <c>builder.AddSecurityLog()</c>，对齐 <c>FilterBuilder.AddAuditLog()</c> 先例）</item>
    /// <item><see cref="InitializeAsync"/>——不调用（V0.1.0 无种子）</item>
    /// </list>
    /// <para>V4.9.85 起发现不自动启用——消费方须在自身领域初始化器上标注
    /// <c>[TKWFEnabledExtension(typeof(SecurityLogExtensionInitializer&lt;&gt;))]</c> 白名单声明，三钩子才执行。</para>
    /// </summary>
    [TKWFExtension("SecurityLog")]
    public class SecurityLogExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "SecurityLog";

        /// <summary>扩展描述。</summary>
        public override string Description => "安全日志扩展——认证/授权安全事件记录与查询（只增不改）";

        /// <summary>
        /// 注册安全日志存储 + 查询服务 + 分析服务 + Options 配置（SG1 DataService 经 v4.10.8 ADR61 自动注册）。
        /// <para>V4.10.53（领域自治根治，ADR90）：注册形态由 <c>TryAddScoped&lt;I, Impl&gt;</c> 改为
        /// <c>AddConstructibleService&lt;I, Impl&gt;</c>——三态说明：
        /// <list type="bullet">
        /// <item>接口 → 可构造守卫工厂（CurrentAopUser 守卫）：仅 <c>User.Use&lt;接口&gt;()</c> AOP 路径可解析
        ///       （域作用域外解析即抛 InvalidOperationException——DI004 运行期兜底）；</item>
        /// <item>实现类 → throw-factory：禁止直接 DI 解析（运行时永不调用，仅满足 ValidateOnBuild）；</item>
        /// <item>旧形态构造注入 <see cref="IDomainUser"/> 而 IDomainUser 永不注册 DI（D01）→ 生产解析必失败
        ///       （v0.3.3 同根缺陷）；消费方统一经 <c>User.Use&lt;ISecurityLogStore&gt;()</c> 等解析。</item>
        /// </list>
        /// Analytics ctor 依赖 <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>——<c>AddOptions</c>
        /// 注册默认值兜底（无 IConfiguration 的非 Web 宿主仍可解析），消费方 <c>Configure&lt;T&gt;(section)</c> 覆盖。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. Options（默认值 + 配置节绑定——SG1 经 [Options("TKWF:SecurityLog")] 生成 GetSection 绑定；
            //    Analytics 守卫工厂经 ActivatorUtilities 解析 IOptions 需此处兜底，对齐 skill §4.8 心得 10）
            services.AddOptions<SecurityLoggingOptions>()
                .BindConfiguration("TKWF:SecurityLog");

            // 2. SG1 DataService——ADR61 起自动注册（可构造工厂），不再手动 TryAddScoped

            // 3. 写入存储（只增不改：仅 SaveAsync）——AddConstructibleService 接口守卫工厂 + 实现 throw-factory
            services.AddConstructibleService<ISecurityLogStore, SecurityLogStore>();

            // 4. 查询服务（分页/过滤/Count/GetDetailAsync）
            services.AddConstructibleService<ISecurityLogQueryService, SecurityLogQueryService>();

            // 5. 分析服务（v0.2.0：失败次数 TopN 聚合 + 保留天数清理）
            services.AddConstructibleService<ISecurityLogAnalyticsService, SecurityLogAnalyticsService>();
        }

        /// <summary>过滤器不自动注册——消费方 opt-in：<c>builder.AddSecurityLog()</c>（对齐 AddAuditLog 先例）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现：过滤器经消费方 ConfigureGlobalFilters 中 FilterBuilder.AddSecurityLog() 显式启用
        }

        /// <summary>系统就绪后初始化（空实现）。</summary>
        public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
    }
}
