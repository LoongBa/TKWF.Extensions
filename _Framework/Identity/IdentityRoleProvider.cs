using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// </summary>
public sealed class IdentityRoleProvider<TUserInfo> : IRoleProvider<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    private readonly IUserManager _userManager;
    private Dictionary<string, IReadOnlyList<string>>? _cache;   // Scoped 缓存：userIdString → roles

    public IdentityRoleProvider(IUserManager userManager)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(TUserInfo userInfo)
    {
        // UserIdString → long（Identity 用户表 long 约定）；转换失败返回空
        if (userInfo?.UserIdString is not { } userIdStr || !long.TryParse(userIdStr, out var userId)) return [];

        // Scoped 缓存：同一请求内二次调用命中内存（PermissionChecker 双调用/批量放大消除，Oracle P1-2）
        _cache ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (_cache.TryGetValue(userIdStr, out var cached)) return cached;

        var roles = (await _userManager.GetUserRolesAsync(userId)).Select(r => r.Name).ToList();
        _cache[userIdStr] = roles;
        return roles;
    }
}
