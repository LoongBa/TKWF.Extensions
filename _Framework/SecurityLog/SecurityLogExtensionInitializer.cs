using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志扩展初始化器——经 <c>[TKWFExtension("SecurityLog")]</c> 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 Options（<c>TKWF:SecurityLog</c>）+ SG1 DataService +
    ///       <see cref="ISecurityLogStore"/> + <see cref="ISecurityLogQueryService"/>（均 TryAddScoped，消费方自定义优先）</item>
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
        /// 注册安全日志存储 + 查询服务 + Options 配置 + SG1 DataService。
        /// <para>DataService 显式注册（<c>TryAddScoped</c>）——对齐 Permissions/BackgroundJobs 显式注册先例：
        /// 保证 Store/QueryService 构造依赖在消费方 DI 中确定性可解析（扩展 DataService 为 internal，消费方无法自行注册）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. Options（默认值 + 配置节绑定——SG1 经 [Options("TKWF:SecurityLog")] 生成 GetSection 绑定）
            services.AddOptions<SecurityLoggingOptions>()
                .BindConfiguration("TKWF:SecurityLog");

            // 2. SG1 DataService（Store/QueryService 构造依赖；显式注册保证 DI 可解析）
            services.TryAddScoped<SecurityLogEntityDataService>();

            // 3. 写入存储（只增不改：仅 SaveAsync）
            services.TryAddScoped<ISecurityLogStore, SecurityLogStore>();

            // 4. 查询服务（分页/过滤/Count/GetDetailAsync）
            services.TryAddScoped<ISecurityLogQueryService, SecurityLogQueryService>();
        }

        /// <summary>过滤器不自动注册——消费方 opt-in：<c>builder.AddSecurityLog()</c>（对齐 AddAuditLog 先例）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            // 空实现：过滤器经消费方 ConfigureGlobalFilters 中 FilterBuilder.AddSecurityLog() 显式启用
        }

        /// <summary>系统就绪后初始化（空实现）。</summary>
        public override Task InitializeAsync() => Task.CompletedTask;
    }
}
