using System.Linq.Expressions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.2.0（W7 先行）：EntityDACPermissionStore upsert 测试（Oracle 评审缺口回补）。
/// <para>用内存桩 <see cref="InMemoryEntityDac"/> 模拟 <c>IEntityDAC&lt;PermissionGrantEntity&gt;</c>，
/// 验证 GetAsync 读取语义与 SetAsync upsert（存在→更新 IsGranted / 不存在→插入）。</para>
/// </summary>
public class EntityDACPermissionStoreTests
{
    private const string Permission = "Order.Create";
    private const string Provider = "User";
    private const string Key = "u-1001";

    [Fact]
    public async Task GetAsync_NoEntity_ReturnsDenied()
    {
        var store = new EntityDACPermissionStore(new InMemoryEntityDac());

        var result = await store.GetAsync(Permission, Provider, Key);

        Assert.Equal(PermissionGrantResult.Denied, result);
        Assert.False(result.IsGranted);
    }

    [Fact]
    public async Task GetAsync_GrantedEntity_ReturnsGranted()
    {
        var dac = new InMemoryEntityDac();
        await dac.InsertAsync(new PermissionGrantEntity
        {
            PermissionName = Permission,
            ProviderName = Provider,
            ProviderKey = Key,
            IsGranted = true
        }, TestContext.Current.CancellationToken);
        var store = new EntityDACPermissionStore(dac);

        var result = await store.GetAsync(Permission, Provider, Key);

        Assert.Equal(PermissionGrantResult.Granted, result);
        Assert.True(result.IsGranted);
    }

    [Fact]
    public async Task GetAsync_DeniedEntity_ReturnsDenied()
    {
        var dac = new InMemoryEntityDac();
        await dac.InsertAsync(new PermissionGrantEntity
        {
            PermissionName = Permission,
            ProviderName = Provider,
            ProviderKey = Key,
            IsGranted = false
        }, TestContext.Current.CancellationToken);
        var store = new EntityDACPermissionStore(dac);

        var result = await store.GetAsync(Permission, Provider, Key);

        Assert.Equal(PermissionGrantResult.Denied, result);
        Assert.False(result.IsGranted);
    }

    [Fact]
    public async Task SetAsync_NewGrant_InsertsEntity()
    {
        var dac = new InMemoryEntityDac();
        var store = new EntityDACPermissionStore(dac);

        await store.SetAsync(Permission, Provider, Key, isGranted: true);

        Assert.Equal(1, dac.InsertCallCount);
        Assert.Equal(0, dac.UpdateCallCount);
        var entity = Assert.Single(dac.Items);
        Assert.Equal(Permission, entity.PermissionName);
        Assert.Equal(Provider, entity.ProviderName);
        Assert.Equal(Key, entity.ProviderKey);
        Assert.True(entity.IsGranted);
    }

    [Fact]
    public async Task SetAsync_ExistingGrant_UpdatesInsteadOfReinserting()
    {
        var dac = new InMemoryEntityDac();
        var store = new EntityDACPermissionStore(dac);
        await store.SetAsync(Permission, Provider, Key, isGranted: true);

        // 同业务键二次写入 → 应更新（IsGranted 翻转），而非插入新行
        await store.SetAsync(Permission, Provider, Key, isGranted: false);

        Assert.Equal(1, dac.InsertCallCount);
        Assert.Equal(1, dac.UpdateCallCount);
        var entity = Assert.Single(dac.Items);
        Assert.Equal(Permission, entity.PermissionName);
        Assert.Equal(Provider, entity.ProviderName);
        Assert.Equal(Key, entity.ProviderKey);
        Assert.False(entity.IsGranted);

        // 读回一致（upsert 落库后 GetAsync 可见）
        var result = await store.GetAsync(Permission, Provider, Key);
        Assert.Equal(PermissionGrantResult.Denied, result);
    }

    [Fact]
    public async Task SetAsync_RoundTrip_GrantThenRevoke()
    {
        var store = new EntityDACPermissionStore(new InMemoryEntityDac());

        await store.SetAsync(Permission, Provider, Key, isGranted: true);
        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync(Permission, Provider, Key));

        await store.SetAsync(Permission, Provider, Key, isGranted: false);
        Assert.Equal(PermissionGrantResult.Denied, await store.GetAsync(Permission, Provider, Key));
    }

    [Fact]
    public async Task SetAsync_Iso_latedByBusinessKey()
    {
        var store = new EntityDACPermissionStore(new InMemoryEntityDac());

        // 不同用户（ProviderKey）互不影响
        await store.SetAsync(Permission, Provider, "u-1001", isGranted: true);
        await store.SetAsync(Permission, Provider, "u-2002", isGranted: false);

        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync(Permission, Provider, "u-1001"));
        Assert.Equal(PermissionGrantResult.Denied, await store.GetAsync(Permission, Provider, "u-2002"));

        // 不同权限名互不影响：新授予 Order.Delete 不影响既有 Order.Create(u-1001)
        await store.SetAsync("Order.Delete", Provider, "u-1001", isGranted: true);
        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync("Order.Delete", Provider, "u-1001"));
        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync(Permission, Provider, "u-1001"));
        // 从未授予的权限名 → Denied
        Assert.Equal(PermissionGrantResult.Denied, await store.GetAsync("Order.Export", Provider, "u-1001"));

        // 不同 provider 互不影响：Role/admin 授予不影响 User/u-1001（其 Order.Create 仍为 Granted）
        await store.SetAsync(Permission, "Role", "admin", isGranted: true);
        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync(Permission, "Role", "admin"));
        Assert.Equal(PermissionGrantResult.Granted, await store.GetAsync(Permission, Provider, "u-1001"));
        // 从未授予的 provider 键 → Denied
        Assert.Equal(PermissionGrantResult.Denied, await store.GetAsync(Permission, "Member", "m-1"));
    }

    /// <summary>
    /// 内存桩 IEntityDAC&lt;PermissionGrantEntity&gt;：List 存储 + 自增 Id，
    /// 记录 Insert/Update 调用次数供 upsert 语义断言。
    /// </summary>
    private sealed class InMemoryEntityDac : IEntityDAC<PermissionGrantEntity>
    {
        private readonly List<PermissionGrantEntity> _items = new();
        private long _nextId = 1;

        public IReadOnlyList<PermissionGrantEntity> Items => _items;
        public int InsertCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }

        public IQueryable<PermissionGrantEntity> Query => _items.AsQueryable();

        public Task<PermissionGrantEntity?> FirstOrDefaultAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.FirstOrDefault());

        public Task<List<PermissionGrantEntity>> ToListAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.ToList());

        public Task<long> CountAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult((long)query.Count());

        public Task<TResult?> FirstOrDefaultAsync<TResult>(
            IQueryable<PermissionGrantEntity> query,
            Expression<Func<PermissionGrantEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).FirstOrDefault());

        public Task<List<TResult>> ToListAsync<TResult>(
            IQueryable<PermissionGrantEntity> query,
            Expression<Func<PermissionGrantEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).ToList());

        public Task<PermissionGrantEntity> InsertAsync(
            PermissionGrantEntity entity, CancellationToken ct = default)
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            _items.Add(entity);
            InsertCallCount++;
            return Task.FromResult(entity);
        }

        public Task<List<PermissionGrantEntity>> InsertBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            var list = entities.ToList();
            foreach (var entity in list) _items.Add(entity);
            InsertCallCount += list.Count;
            return Task.FromResult(list);
        }

        public Task<bool> DeleteAsync(
            PermissionGrantEntity entity, CancellationToken ct = default)
            => Task.FromResult(_items.Remove(entity));

        public Task UpdateAsync(PermissionGrantEntity entity, CancellationToken ct = default)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) _items[index] = entity;
            UpdateCallCount++;
            return Task.CompletedTask;
        }

        public Task UpdateBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            UpdateCallCount += entities.Count();
            return Task.CompletedTask;
        }

        public Task<int> UpdateColumnsBatchAsync<TColumns>(
            IEnumerable<PermissionGrantEntity> entities,
            Expression<Func<PermissionGrantEntity, TColumns>> columns,
            CancellationToken ct = default)
        {
            // 桩不做列级合并：替换全对象即可满足测试断言
            foreach (var entity in entities) UpdateAsync(entity, ct);
            var count = entities.Count();
            UpdateCallCount += count;
            return Task.FromResult(count);
        }
    }
}