using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.2.0（W7 先行）：PermissionChecker fail-closed 测试。
/// <para>fail-closed 语义（Oracle 评审缺口回补）：权限名未定义 → 拒绝；
/// 无 ambient 用户上下文 → 拒绝；store 判定 Denied → 拒绝；仅定义 + 用户 + store Granted → 放行。</para>
/// <para><see cref="PermissionChecker{TUserInfo}"/> 为 internal（IVT 访问）；
/// ambient 用户经 <see cref="DomainUserContext.CurrentAopUser"/>（internal，框架 IVT 授权）设置。</para>
/// </summary>
public class PermissionCheckerTests
{
    private const string DefinedPermission = "Order.Create";
    private const string UnknownPermission = "Order.Nonexistent";
    private const string UserProvider = "User";
    private const string UserId = "u-1001";

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
        {
            var keys = providerKeys == null ? null : new HashSet<string>(providerKeys);
            var granted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _grants)
            {
                if (!kv.Value) continue;
                var parts = kv.Key.Split('|');
                var (perm, prov, key) = (parts[0], parts[1], parts[2]);
                if (prov != providerName) continue;
                if (keys != null && !keys.Contains(key)) continue;
                granted.Add(perm);
            }
            return Task.FromResult(granted);
        }

        public Task<Dictionary<string, HashSet<string>>> GetGrantedPermissionsByProviderKeyAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
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
            return Task.FromResult(result);
        }
    }

    /// <summary>构造含权限定义的 checker（默认仓库含 DefinedPermission）。</summary>
    private static PermissionChecker<SimpleUserInfo> CreateChecker(IPermissionStore? store = null, bool withDefinedPermission = true, IRoleProvider<SimpleUserInfo>? roleProvider = null)
    {
        var repository = new InMemoryPermissionDefinitionRepository();
        if (withDefinedPermission)
            repository.AddRange([new PermissionDefinition { Name = DefinedPermission }]);
        return new PermissionChecker<SimpleUserInfo>(repository, store ?? new StubPermissionStore(), roleProvider ?? new DefaultRoleProvider<SimpleUserInfo>());
    }

    private static IDisposable SetAmbientUser(string? userId)
    {
        var current = DomainUserContext.CurrentAopUser;
        // 无用户场景：标记 null 而非清除——需显式恢复原值
        if (userId == null)
        {
            DomainUserContext.CurrentAopUser = null;
            return new RestoreAmbientUser(current);
        }
        var user = new DomainUser<SimpleUserInfo>
        {
            UserInfo = new SimpleUserInfo(userId, $"用户-{userId}")
        };
        DomainUserContext.CurrentAopUser = user;
        return new RestoreAmbientUser(current);
    }

    /// <summary>测试后恢复原 ambient 用户（AsyncLocal 线程流，xunit.v3 每测试独立上下文但仍显式清理）。</summary>
    private sealed class RestoreAmbientUser(object? previous) : IDisposable
    {
        public void Dispose() => DomainUserContext.CurrentAopUser = previous;
    }

    [Fact]
    public async Task UnknownPermission_FailClosed_ReturnsFalse()
    {
        var checker = CreateChecker();
        Assert.False(await checker.IsGrantedAsync(UnknownPermission));
    }

    [Fact]
    public async Task NullOrWhiteSpacePermission_FailClosed_ReturnsFalse()
    {
        var checker = CreateChecker();
        // 二义性规避：null 同时匹配 string 与 params string[] → 显式转型 string
        Assert.False(await checker.IsGrantedAsync((string)null!));
        Assert.False(await checker.IsGrantedAsync(""));
        Assert.False(await checker.IsGrantedAsync("   "));
    }

    [Fact]
    public async Task DefinedPermission_NoAmbientUserContext_FailClosed_ReturnsFalse()
    {
        using var restore = SetAmbientUser(null);
        var checker = CreateChecker();
        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    [Fact]
    public async Task DefinedPermission_UserStoreDenied_ReturnsFalse()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new StubPermissionStore(); // 未授予任何权限
        var checker = CreateChecker(store);
        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    [Fact]
    public async Task DefinedPermission_UserStoreGranted_ReturnsTrue()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
    }

    [Fact]
    public async Task Accessibility_QueriesUserProviderAndKey()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, isGranted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
        // 双写确认：store 仅对 (DefinedPermission, "User", UserId) 授了权。
        // 若 checker 错误使用其他 provider/键，此处仍 Denied。
        var other = new StubPermissionStore();
        other.Grant(DefinedPermission, "Role", "admin", isGranted: true);
        var checkerOther = CreateChecker(other);
        Assert.False(await checkerOther.IsGrantedAsync(DefinedPermission));
    }

    [Fact]
    public async Task BatchCheck_ReturnsPerPermissionMap()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, isGranted: true);
        var checker = CreateChecker(store);

        var result = await checker.IsGrantedAsync(DefinedPermission, UnknownPermission);

        Assert.True(result[DefinedPermission]);
        Assert.False(result[UnknownPermission]);
    }

    [Fact]
    public async Task UserWithoutUserIdString_FailClosed_ReturnsFalse()
    {
        // 已认证但 UserInfo 无 UserIdString（空）→ 拒绝（未认证/无用户上下文等价）
        using var restore = SetAmbientUser("");
        var store = new StubPermissionStore();
        store.Grant(DefinedPermission, UserProvider, "", isGranted: true);
        var checker = CreateChecker(store);

        Assert.False(await checker.IsGrantedAsync(DefinedPermission));
    }

    // ── V0.8.1 N+1 优化专项测试 ──

    /// <summary>计数 stub：追踪 GetGrantedPermissionNamesAsync 调用次数——验证 N+1 消除 + Scoped 缓存。</summary>
    private sealed class CountingPermissionStore : IPermissionStore
    {
        private readonly StubPermissionStore _inner = new();
        public int BatchQueryCount { get; private set; }

        public void Grant(string name, string provider, string key, bool granted)
            => _inner.Grant(name, provider, key, granted);

        public Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
            => _inner.GetAsync(permissionName, providerName, providerKey);

        public Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
            => _inner.SetAsync(permissionName, providerName, providerKey, isGranted);

        public Task<HashSet<string>> GetGrantedPermissionNamesAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
        {
            BatchQueryCount++;
            return _inner.GetGrantedPermissionNamesAsync(providerName, providerKeys);
        }

        public Task<Dictionary<string, HashSet<string>>> GetGrantedPermissionsByProviderKeyAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
        {
            BatchQueryCount++;
            return _inner.GetGrantedPermissionsByProviderKeyAsync(providerName, providerKeys);
        }
    }

    [Fact]
    public async Task N1Optimization_BatchCheck_FewBatchQueriesRegardlessOfPermissionCount()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new CountingPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, granted: true);
        var checker = CreateChecker(store);

        // 批量检查多个权限名——N+1 优化后每 provider 仅一次批量查询（用户级 + 角色级 = 2 次）
        // 原实现：N 权限 × (用户级 1 + 每角色 N) 次单条查询——N 大时线性放大
        var result = await checker.IsGrantedAsync(DefinedPermission, UnknownPermission, "P3", "P4", "P5");

        Assert.True(result[DefinedPermission]);
        Assert.All(result.Where(kv => kv.Key != DefinedPermission), kv => Assert.False(kv.Value));
        Assert.Equal(2, store.BatchQueryCount);   // 仅 User + Role 两次批量查询，与权限名数量无关
    }

    [Fact]
    public async Task N1Optimization_ScopedCache_SecondCheckNoExtraQuery()
    {
        using var restore = SetAmbientUser(UserId);
        var store = new CountingPermissionStore();
        store.Grant(DefinedPermission, UserProvider, UserId, granted: true);
        var checker = CreateChecker(store);

        Assert.True(await checker.IsGrantedAsync(DefinedPermission));
        Assert.True(await checker.IsGrantedAsync(DefinedPermission));   // 二次检查——Scoped 缓存命中
        Assert.False(await checker.IsGrantedAsync(UnknownPermission));  // fail-closed——未定义权限拒绝

        Assert.Equal(2, store.BatchQueryCount);   // 3 次检查仅 2 次批量查询（User + Role 各 1，缓存命中不重复）
    }
}