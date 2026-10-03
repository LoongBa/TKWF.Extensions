using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Identity;

/// <summary>
/// Permissions 角色提供者——实时查库（V0.3.0）替代 <c>DefaultRoleProvider</c> 内存快照（<c>IUserInfo.Roles</c>）。
/// <para>角色变更（<c>IUserManager.AssignRolesAsync</c>）**即时生效**（无需重新登录）——权限管理后台改角色后
/// 下次权限检查按新角色判定。查询路径：<c>IUserManager.GetUserRolesAsync(userId)</c> → <c>UserStore.GetRolesAsync</c>
/// → <c>UserRoleViewDataService</c>（VEntity JOIN 单查询，V0.2.0 已就绪）。</para>
/// <para>Scoped 缓存（Oracle P1-2）：<c>PermissionChecker.IsGrantedCoreAsync</c> 每次检查调 GetRolesAsync 两次
/// （HasSystemPermissionAsync + 角色循环）+ 批量放大 2N——同一请求内按 userIdString 缓存，首查 DB 后命中内存。</para>
/// <para>DI 注册：Identity 扩展用 <c>AddScoped</c>（非 TryAdd）覆盖 Permissions 默认——仅当消费方双白名单
/// 声明 Identity + Permissions 时生效（Oracle P1-4）。</para>
/// <para>V4.10.53（领域自治根治，正确路线）：**接线型**（skill §4.2）——跨扩展契约
/// <see cref="IRoleProvider{TUserInfo}"/>（Permissions.Abstractions）非 IDomainService（不可修改契约），
/// Permissions.<c>PermissionChecker</c> ctor 经普通 DI 构造注入本实现——<c>AddConstructibleService</c>
/// 编译约束（where TInterface : IDomainService）不满足。旧 ctor(IDomainUser) 在 PermissionChecker 解析时
/// IDomainUser 无可解析（永不注册 DI——D01）生产必失败（真实故障）；改 ctor(<see cref="IServiceProvider"/>)——
/// <see cref="IUserManager"/> 经 C1 延迟解析（<c>GetRequiredService</c>，消费链内已注册）。</para>
/// </summary>
public sealed class IdentityRoleProvider<TUserInfo> : IRoleProvider<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    private readonly IServiceProvider _serviceProvider;
    private IUserManager? _userManager;
    private Dictionary<string, IReadOnlyList<string>>? _cache;   // Scoped 缓存：userIdString → roles

    private IUserManager UserManager => _userManager ??= _serviceProvider.GetRequiredService<IUserManager>();

    public IdentityRoleProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(TUserInfo userInfo)
    {
        // UserIdString → long（Identity 用户表 long 约定）；转换失败返回空
        if (userInfo?.UserIdString is not { } userIdStr || !long.TryParse(userIdStr, out var userId)) return [];

        // Scoped 缓存：同一请求内二次调用命中内存（PermissionChecker 双调用/批量放大消除，Oracle P1-2）
        _cache ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (_cache.TryGetValue(userIdStr, out var cached)) return cached;

        var roles = (await UserManager.GetUserRolesAsync(userId)).Select(r => r.Name).ToList();
        _cache[userIdStr] = roles;
        return roles;
    }
}
