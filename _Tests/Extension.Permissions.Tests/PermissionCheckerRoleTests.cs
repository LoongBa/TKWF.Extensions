using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.6.0：PermissionChecker 角色→权限映射测试。
/// <para>覆盖用户+角色双重检查逻辑：用户显式授予优先 → 角色级兜底 → fail-closed。</para>
/// </summary>
public class PermissionCheckerRoleTests
{
    private const string DefinedPermission = "Order.Create";
    private const string UserProvider = "User";
    private const string RoleProvider = "Role";
    private const string UserId = "u-1001";
    private const string RoleAdmin = "admin";
    private const string RoleEditor = "editor";

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
    }

    /// <summary>创建带指定角色的 ambient 用户上下文。</summary>
    private static IDisposable SetAmbientUserWithRoles(string userId, params string[] roles)
    {
        var current = DomainUserContext.CurrentAopUser;
        var user = new DomainUser<SimpleUserInfo>
        {
            UserInfo = new SimpleUserInfo(userId, $"用户-{userId}") { Roles = roles.ToList() }
        };
        DomainUserContext.CurrentAopUser = user;
        return new RestoreAmbientUser(current);
    }

    /// <summary>无用户上下文。</summary>
    private static IDisposable SetNoAmbientUser()
    {
        var current = DomainUserContext.CurrentAopUser;
        DomainUserContext.CurrentAopUser = null;
        return new RestoreAmbientUser(current);
    }

    private sealed class RestoreAmbientUser(object? previous) : IDisposable
    {
        public void Dispose() => DomainUserContext.CurrentAopUser = previous;
    }

    private static PermissionChecker<SimpleUserInfo> CreateChecker(
        StubPermissionStore? store = null,
        IRoleProvider<SimpleUserInfo>? roleProvider = null)
    {
        var repository = new InMemoryPermissionDefinitionRepository();
        repository.AddRange([new PermissionDefinition { Name = DefinedPermission }]);
        return new PermissionChecker<SimpleUserInfo>(
            repository,
            store ?? new StubPermissionStore(),
            roleProvider ?? new DefaultRoleProvider<SimpleUserInfo>());
    }

    // ────────────────────── 8 角色级测试 ──────────────────────

    /// <summary>角色已授权 + 用户无授权 → 通过（角色兜底）。</summary>
    [Fact]
    public async Task RoleGranted_NoUserGrant_ReturnsTrue()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, RoleProvider, RoleAdmin, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>用户已授权 + 角色无授权 → 通过（用户优先）。</summary>
    [Fact]
    public async Task UserGranted_NoRoleGrant_ReturnsTrue()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>用户和角色均已授权 → 通过（用户优先，任一即可）。</summary>
    [Fact]
    public async Task UserAndRoleBothGranted_ReturnsTrue()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, isGranted: true);
        store.Grant(DefinedPermission, RoleProvider, RoleAdmin, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>用户和角色均无授权 → 拒绝（fail-closed）。</summary>
    [Fact]
    public async Task UserAndRoleBothNotGranted_ReturnsFalse()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore(); // 无任何授权
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>多角色：其中一个角色已授权 → 通过（任一角色授予即通过）。</summary>
    [Fact]
    public async Task MultipleRoles_OneGranted_ReturnsTrue()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin, RoleEditor);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, RoleProvider, RoleEditor, isGranted: true);
        // admin 角色未授权，仅 editor 授权
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>多角色：所有角色均无授权 → 拒绝。</summary>
    [Fact]
    public async Task MultipleRoles_NoneGranted_ReturnsFalse()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin, RoleEditor);
        var store = new StubPermissionStore();
        // admin 和 editor 均未授权
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>用户 Roles 为 null → 无角色可查，仅用户级检查。</summary>
    [Fact]
    public async Task NullRoles_NoUserGrant_ReturnsFalse()
    {
        using var restore = SetAmbientUserWithRoles(UserId); // 空角色
        var store = new StubPermissionStore();
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>无用户上下文 → fail-closed（角色检查不可达）。</summary>
    [Fact]
    public async Task NoUserContext_FailClosed_ReturnsFalse()
    {
        using var restore = SetNoAmbientUser();
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, RoleProvider, RoleAdmin, isGranted: true);
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    // ────────────────────── V0.7.0 Admin.All 系统权限测试 ──────────────────────

    /// <summary>角色拥有 Admin.All → 任意已定义权限放行（含未显式授权者）。</summary>
    [Fact]
    public async Task RoleHasAdminAll_GrantsAnyDefinedPermission()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(PermissionNames.AdminAll, RoleProvider, RoleAdmin, isGranted: true);
        var checker = CreateChecker(store);

        // 权限已定义但未单独授权——Admin.All 拥有者放行
        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>用户级拥有 Admin.All → 任意已定义权限放行。</summary>
    [Fact]
    public async Task UserHasAdminAll_GrantsAnyDefinedPermission()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(PermissionNames.AdminAll, UserProvider, UserId, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>任一角色拥有 Admin.All → 放行（多角色任一命中）。</summary>
    [Fact]
    public async Task MultipleRoles_OneHasAdminAll_Grants()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin, RoleEditor);
        var store = new StubPermissionStore();
        store.Grant(PermissionNames.AdminAll, RoleProvider, RoleEditor, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>无任何 Admin.All 且权限未授权 → fail-closed。</summary>
    [Fact]
    public async Task NoAdminAll_NoGrant_FailClosed()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore(); // 无 Admin.All，无授权
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    /// <summary>Admin.All 拥有者对未定义权限也放行（系统权限是隐式定义，不走贡献者校验）。</summary>
    [Fact]
    public async Task AdminAll_GrantsUndefinedPermissionName()
    {
        using var restore = SetAmbientUserWithRoles(UserId, RoleAdmin);
        var store = new StubPermissionStore();
        store.Grant(PermissionNames.AdminAll, RoleProvider, RoleAdmin, isGranted: true);
        var checker = CreateChecker(store);

        // 未在仓库定义的权限名——Admin.All 拥有者仍放行
        Assert.True(await checker.IsGrantedAsync("System.UndefinedPermission"));
    }
}
