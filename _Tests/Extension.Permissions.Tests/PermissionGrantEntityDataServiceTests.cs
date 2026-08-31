using System.Linq.Expressions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.3.0（W4）：PermissionGrantEntityDataService 单元测试。
/// <para>用内存桩 <see cref="InMemoryEntityDac"/> 模拟 <c>IEntityDAC&lt;PermissionGrantEntity&gt;</c>，
/// 最小桩 <see cref="StubDomainUser"/> 模拟 <see cref="IDomainUser"/>，
/// 验证 DataService CRUD + 自定义查询 + upsert + revoke。</para>
/// </summary>
public class PermissionGrantEntityDataServiceTests
{
    private const string Permission = "Order.Create";
    private const string Provider = "User";
    private const string Key = "u-1001";

    [Fact]
    public async Task SetGrantAsync_NewGrant_InsertsAndReturnsDto()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);

        var dto = await svc.SetGrantAsync(Permission, Provider, Key, isGranted: true);

        Assert.Equal(Permission, dto.PermissionName);
        Assert.Equal(Provider, dto.ProviderName);
        Assert.Equal(Key, dto.ProviderKey);
        Assert.True(dto.IsGranted);
        // 新插入实体非来自持久源（IsFromPersistentSource 由框架读路径统一置位，写路径不置位）
        Assert.False(dto.IsFromPersistentSource);
        Assert.Equal(1, dac.InsertCallCount);
        Assert.Equal(0, dac.UpdateCallCount);
    }

    [Fact]
    public async Task SetGrantAsync_ExistingGrant_UpdatesInPlace()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync(Permission, Provider, Key, isGranted: true);

        var dto = await svc.SetGrantAsync(Permission, Provider, Key, isGranted: false);

        Assert.False(dto.IsGranted);
        Assert.Equal(1, dac.InsertCallCount);
        Assert.Equal(1, dac.UpdateCallCount);
        Assert.Single(dac.Items);
    }

    [Fact]
    public async Task GetGrantAsync_Exists_ReturnsDto()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync(Permission, Provider, Key, isGranted: true);

        var dto = await svc.GetGrantAsync(Permission, Provider, Key);

        Assert.NotNull(dto);
        Assert.Equal(Permission, dto!.PermissionName);
        Assert.True(dto.IsGranted);
    }

    [Fact]
    public async Task GetGrantAsync_NotExists_ReturnsNull()
    {
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), new InMemoryEntityDac());

        var dto = await svc.GetGrantAsync(Permission, Provider, Key);

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetByPermissionNameAsync_ReturnsMatching()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync("Order.Create", Provider, "u-1", isGranted: true);
        await svc.SetGrantAsync("Order.Create", Provider, "u-2", isGranted: false);
        await svc.SetGrantAsync("Order.Delete", Provider, "u-1", isGranted: true);

        var dtos = await svc.GetByPermissionNameAsync("Order.Create");

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal("Order.Create", d.PermissionName));
    }

    [Fact]
    public async Task GetByProviderAsync_ReturnsMatching()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync("Order.Create", Provider, "u-1001", isGranted: true);
        await svc.SetGrantAsync("Order.Delete", Provider, "u-1001", isGranted: true);
        await svc.SetGrantAsync("Order.Create", "Role", "admin", isGranted: true);

        var dtos = await svc.GetByProviderAsync(Provider, "u-1001");

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal(Provider, d.ProviderName));
        Assert.All(dtos, d => Assert.Equal("u-1001", d.ProviderKey));
    }

    [Fact]
    public async Task RevokeGrantAsync_Exists_DeletesAndReturnsTrue()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync(Permission, Provider, Key, isGranted: true);

        var result = await svc.RevokeGrantAsync(Permission, Provider, Key);

        Assert.True(result);
        Assert.Empty(dac.Items);
    }

    [Fact]
    public async Task RevokeGrantAsync_NotExists_ReturnsFalse()
    {
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), new InMemoryEntityDac());

        var result = await svc.RevokeGrantAsync(Permission, Provider, Key);

        Assert.False(result);
    }

    [Fact]
    public async Task SetGrantAsync_DifferentProviders_AreIsolated()
    {
        var dac = new InMemoryEntityDac();
        var svc = new PermissionGrantEntityDataService(new StubDomainUser(), dac);
        await svc.SetGrantAsync(Permission, Provider, "u-1001", isGranted: true);
        await svc.SetGrantAsync(Permission, Provider, "u-2002", isGranted: false);
        await svc.SetGrantAsync(Permission, "Role", "admin", isGranted: true);

        var userGrant = await svc.GetGrantAsync(Permission, Provider, "u-1001");
        var roleGrant = await svc.GetGrantAsync(Permission, "Role", "admin");

        Assert.NotNull(userGrant);
        Assert.True(userGrant!.IsGranted);
        Assert.NotNull(roleGrant);
        Assert.True(roleGrant!.IsGranted);
        // u-2002 denied
        var denied = await svc.GetGrantAsync(Permission, Provider, "u-2002");
        Assert.NotNull(denied);
        Assert.False(denied!.IsGranted);
    }

    /// <summary>最小 IDomainUser 桩——仅满足编译，不提供真实用户上下文。</summary>
    private sealed class StubDomainUser : IDomainUser
    {
        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public TDomainService Use<TDomainService>() where TDomainService : IDomainService
            => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
        public TService GetService<TService>() where TService : notnull
            => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => Array.Empty<TService>();
    }

    /// <summary>
    /// 内存桩 IEntityDAC&lt;PermissionGrantEntity&gt;（复用 V0.2.0 测试桩）。
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
            foreach (var entity in entities) UpdateAsync(entity, ct);
            var count = entities.Count();
            UpdateCallCount += count;
            return Task.FromResult(count);
        }
    }
}
