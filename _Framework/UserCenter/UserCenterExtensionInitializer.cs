using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 用户中心扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册 Options + <see cref="IUserCenterQueryService"/> 门面（TryAddScoped）；
///      <b>Source 接口不注册默认实现</b>——实现方（认证中心/授权面扩展 Initializer TryAddScoped / 装配实例注册）自动装配，
///      对齐 IDashboardDataProvider / ISmsSender 先例</item>
/// <item>ConfigureFilters——不调用（非过滤器扩展）</item>
/// <item>InitializeAsync——不调用（零实体零持久化——聚合读取层）</item>
/// </list>
/// </summary>
[TKWFExtension("UserCenter")]
public class UserCenterExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "UserCenter";

    /// <summary>扩展描述。</summary>
    public override string Description => "用户中心（档案面）——公共 Profile + 兑换历史/我的应用经契约协作；领域逻辑（脱敏/降级）进扩展，数据源由认证中心/授权面/装配实例实现";

    /// <summary>
    /// 注册用户中心门面。
    /// <para>TryAdd 语义：消费方可自定义 <see cref="IUserCenterQueryService"/> 实现；
    /// Source 接口（IUserProfileSource / IRedemptionHistorySource / IUserAppsSource）不在此注册——
    /// 由数据属主扩展（认证中心/授权面 v0.2.0+）或装配实例（过渡期模板基类）注册。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值注册（默认值兜底；消费方经 TKWF:UserCenter 节绑定）
        services.AddOptions<UserCenterOptions>();

        // 门面（Scoped：按请求组合三 Source）
        services.TryAddScoped<IUserCenterQueryService, UserCenterQueryService>();
    }
}