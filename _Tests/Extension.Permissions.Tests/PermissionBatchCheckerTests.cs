using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.9.0：IPermissionBatchChecker 多用户批量权限检查测试。
/// <para>覆盖 Oracle P1-2（分组归因——GetGrantedPermissionsByProviderKeyAsync）/P1-3（复用 IRoleProvider 角色解析）/
/// 角色回退/Admin.All/fail-closed/空输入未注册（P2-2）。</para>
/// </summary>
public class PermissionBatchCheckerTests
{
    private const string DefinedPermission = "Order.View";
    private const string UnknownPermission = "Order.Nonexistent";
    private const string AdminAll = "Admin.All";

    private sealed class StubPermissionStore : IPermissionStore
    {
        private readonly Dictionary<string, bool> _grants = new();

        public void Grant(string permissionName, string providerName, string providerKey, bool isGranted)
            => _grants[$"{permissionName}|{providerName}|{providerKey}"] = isGranted;

        public Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
        {
            _grants.TryGetValue($"{permissionName}|{providerName}|{providerKey}", out var granted);
            return Task.FromResult(granted ? PermissionGrantResult.Granted : PermissionGrantResult.Denied);
        }

        public Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
        {
            Grant(permissionName, providerName, providerKey, isGranted);
            return Task.CompletedTask;
        }

        public Task<HashSet<string>> GetGrantedPermissionNamesAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
            => Task.FromResult(GetByKey(providerName, providerKeys).SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal));

        /// <summary>分组归因实现——Oracle P1-2：按 providerKey 分组返回（与 DataService GetGrantedNamesByProviderKeyAsync 同语义）。</summary>
        public Task<Dictionary<string, HashSet<string>>> GetGrantedPermissionsByProviderKeyAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
            => Task.FromResult(GetByKey(providerName, providerKeys));

        private Dictionary<string, HashSet<string>> GetByKey(string providerName, IEnumerable<string>? providerKeys)
        {
            var keys = providerKeys == null ? null : new HashSet<string>(providerKeys);
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var kv in _grants)
            {
                if (!kv.Value) continue;
                var parts = kv.Key.Split('|');
                var (perm, prov, key) = (parts[0], parts[1], parts[2]);
                if (prov != providerName) continue;
                if (keys != null && !keys.Contains(key)) continue;
                if (!result.TryGetValue(key, out var set)) { set = new HashSet<string>(StringComparer.Ordinal); result[key] = set; }
                set.Add(perm);
            }
            return result;
        }
    }

    /// <summary>测试角色提供者——按 UserIdString 解析角色（对齐 IdentityRoleProvider 语义，Oracle P1-3 复用路径）。</summary>
    private sealed class TestRoleProvider : IRoleProvider<SimpleUserInfo>
    {
        private readonly Dictionary<string, string[]> _userRoles = new();

        public void Assign(string userId, params string[] roles) => _userRoles[userId] = roles;

        public Task<IReadOnlyList<string>> GetRolesAsync(SimpleUserInfo userInfo)
        {
            var userId = userInfo?.UserIdString ?? "";
            _userRoles.TryGetValue(userId, out var roles);
            return Task.FromResult<IReadOnlyList<string>>(roles ?? Array.Empty<string>());
        }
    }

    private static (PermissionChecker<SimpleUserInfo> Checker, StubPermissionStore Store, TestRoleProvider RoleProvider) Create(
        StubPermissionStore? store = null)
    {
        var repository = new InMemoryPermissionDefinitionRepository();
        repository.AddRange([new PermissionDefinition { Name = DefinedPermission }]);
        var s = store ?? new StubPermissionStore();
        var rp = new TestRoleProvider();
        return (new PermissionChecker<SimpleUserInfo>(repository, s, rp), s, rp);
    }

    [Fact]
    public async Task IsGranted_MultiUser_UserLevelAttribution()
    {
        var (checker, store, _) = Create();
        store.Grant(DefinedPermission, "User", "1", true);
        store.Grant(DefinedPermission, "User", "2", true);
        store.Grant(DefinedPermission, "User", "3", false);
        store.Grant(DefinedPermission, "User", "4", false);

        var result = await checker.IsGrantedAsync([1, 2, 3, 4], DefinedPermission);

        // Oracle P1-2：分组归因——每个用户独立判定（非扁平并集）
        Assert.Equal(4, result.Count);
        Assert.True(result[1]);
        Assert.True(result[2]);
        Assert.False(result[3]);
        Assert.False(result[4]);
    }

    [Fact]
    public async Task IsGranted_MixedUsers_PartialPermission()
    {
        var (checker, store, _) = Create();
        store.Grant(DefinedPermission, "User", "1", true);
        store.Grant(DefinedPermission, "User", "2", false);

        var result = await checker.IsGrantedAsync([1, 2], DefinedPermission);

        Assert.True(result[1]);
        Assert.False(result[2]);
    }

    [Fact]
    public async Task IsGranted_RoleFallback_UserNotGrantedButRoleGranted()
    {
        var (checker, store, rp) = Create();
        rp.Assign("1", "Admin", "Operator");
        rp.Assign("2", "Viewer");
        store.Grant(DefinedPermission, "Role", "Admin", true);   // Admin 角色有权限
        store.Grant(DefinedPermission, "Role", "Viewer", false);

        var result = await checker.IsGrantedAsync([1, 2], DefinedPermission);

        // Oracle P1-3：角色回退——用户级未授予但角色授予 → 通过
        Assert.True(result[1]);   // 经 Admin 角色
        Assert.False(result[2]);  // 经 Viewer 角色（未授予）
    }

    [Fact]
    public async Task IsGranted_AdminAll_UserOrRole_Allows()
    {
        var (checker, store, rp) = Create();
        rp.Assign("1", "admin");
        store.Grant(AdminAll, "Role", "admin", true);   // 角色 Admin.All
        store.Grant(AdminAll, "User", "2", true);       // 用户 Admin.All

        var result = await checker.IsGrantedAsync([1, 2, 3], DefinedPermission);   // 3 无 Admin.All

        Assert.True(result[1]);   // 角色 Admin.All
        Assert.True(result[2]);   // 用户 Admin.All
        Assert.False(result[3]);  // 无 Admin.All 且无 Order.View → fail-closed
    }

    [Fact]
    public async Task IsGranted_UnknownPermission_FailClosed()
    {
        var (checker, store, _) = Create();
        store.Grant(UnknownPermission, "User", "1", true);

        var result = await checker.IsGrantedAsync([1], UnknownPermission);

        // 未定义权限 → fail-closed 拒绝（EvaluatePermission 语义）
        Assert.False(result[1]);
    }

    [Fact]
    public async Task IsGranted_EmptyInputs_ReturnsEmpty()
    {
        var (checker, _, _) = Create();

        Assert.Empty(await checker.IsGrantedAsync([], DefinedPermission));
        Assert.Empty(await checker.IsGrantedAsync([1], ""));
    }

    [Fact]
    public async Task IsGranted_NoRoleProviderRoles_UserLevelOnly()
    {
        // Oracle P2-2：无 Identity（仅 DefaultRoleProvider 语义）→ 角色级空，仅用户级判定（fail-closed）
        var (checker, store, _) = Create();
        store.Grant(DefinedPermission, "User", "1", true);
        store.Grant(DefinedPermission, "Role", "Admin", true);   // 角色有但用户 1 无该角色

        var result = await checker.IsGrantedAsync([1], DefinedPermission);

        Assert.True(result[1]);   // 用户级有 → 通过（角色无关）
    }
}