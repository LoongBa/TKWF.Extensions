using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.4.0（G3）+ V0.7.0（W3）+ V4.10.53（领域自治根治）：PermissionExtensionInitializer 种子测试。
/// <para>覆盖要点：预置 admin 角色 Admin.All 系统权限（V0.7.0 起替代逐权限授予）；仅插入缺失记录
/// （不覆盖既有授予/撤销）；幂等；空角色名禁用种子；未注册 IEntityDAC（真实持久化未接线）时跳过。</para>
/// <para>V4.10.53：种子核心提取为 <see cref="PermissionExtensionInitializer{TUserInfo}.SeedAdminAllAsync"/>
/// 供直测（免 DomainHost/System 作用域）；<see cref="PermissionExtensionInitializer{TUserInfo}.InitializeAsync"/>
/// 的守卫跳过路径（无 IEntityDAC / 空角色名）不经宿主，仍直测 InitializeAsync。</para>
/// </summary>
public class PermissionSeedInitializerTests
{
    private const string RoleProvider = "Role";

    /// <summary>构建仅含种子守卫所需注册的 DI 容器（IEntityDAC + Options——跳过路径不经宿主）。</summary>
    private static ServiceProvider BuildSvcProvider(bool registerDac, string? seedRole)
    {
        var services = new ServiceCollection();

        if (registerDac)
            services.AddScoped<IEntityDAC<PermissionGrantEntity>>(_ => new InMemoryEntityDac());

        if (seedRole is not null)
            services.AddSingleton<IOptions<PermissionOptions>>(
                Options.Create(new PermissionOptions { SeedAdminRoleName = seedRole }));

        return services.BuildServiceProvider();
    }

    /// <summary>经内存桩 DAC 构造 DataService（种子核心直测载体）。</summary>
    private static PermissionGrantEntityDataService CreateDataService(InMemoryEntityDac dac)
        => new(new StubDomainUser(), dac);

    [Fact]
    public async Task SeedAdminAll_SeedsAdminRole_AdminAllSystemPermission()
    {
        var dac = new InMemoryEntityDac();
        var dataService = CreateDataService(dac);

        await PermissionExtensionInitializer<SimpleUserInfo>.SeedAdminAllAsync(dataService, "admin");

        var adminAll = await dataService.GetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin");
        Assert.NotNull(adminAll);
        Assert.True(adminAll!.IsGranted);

        // V0.7.0：种子只预置 Admin.All 系统权限，不再逐权限授予
        var grants = await dataService.GetByProviderAsync(RoleProvider, "admin");
        Assert.Single(grants);
        Assert.Equal(PermissionNames.AdminAll, grants[0].PermissionName);
    }

    [Fact]
    public async Task SeedAdminAll_Idempotent_DoesNotDuplicate()
    {
        var dac = new InMemoryEntityDac();
        var dataService = CreateDataService(dac);

        await PermissionExtensionInitializer<SimpleUserInfo>.SeedAdminAllAsync(dataService, "admin");
        await PermissionExtensionInitializer<SimpleUserInfo>.SeedAdminAllAsync(dataService, "admin");

        var grants = await dataService.GetByProviderAsync(RoleProvider, "admin");
        Assert.Single(grants);
    }

    [Fact]
    public async Task SeedAdminAll_DoesNotOverwriteExistingRevoke()
    {
        var dac = new InMemoryEntityDac();
        var dataService = CreateDataService(dac);
        // 消费方先显式撤销 Admin.All（记录已存在，IsGranted=false）
        await dataService.SetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin", isGranted: false);

        await PermissionExtensionInitializer<SimpleUserInfo>.SeedAdminAllAsync(dataService, "admin");

        var grant = await dataService.GetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin");
        Assert.NotNull(grant);
        Assert.False(grant!.IsGranted); // 未被种子覆盖
    }

    [Fact]
    public async Task InitializeAsync_EmptySeedRole_DisablesSeeding()
    {
        // 空角色名 → 种子守卫返回（不经 DomainHost/System 作用域——无宿主也安全）
        var sp = BuildSvcProvider(registerDac: true, seedRole: "");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo>();

        await initializer.InitializeAsync(sp); // 不应抛异常
    }

    [Fact]
    public async Task InitializeAsync_NoDacRegistered_SkipsSeeding()
    {
        // 未注册 IEntityDAC（真实持久化未接线）→ 种子守卫返回（不经 DomainHost/System 作用域）
        var sp = BuildSvcProvider(registerDac: false, seedRole: "admin");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo>();

        await initializer.InitializeAsync(sp); // 不应抛异常
    }

    /// <summary>最小 IDomainUser 桩——仅满足 DataService 构造。</summary>
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
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

    /// <summary>内存桩 IEntityDAC&lt;PermissionGrantEntity&gt;（复用测试桩）。</summary>
    private sealed class InMemoryEntityDac : IEntityDAC<PermissionGrantEntity>
    {
        private readonly List<PermissionGrantEntity> _items = new();
        private long _nextId = 1;

        public IReadOnlyList<PermissionGrantEntity> Items => _items;

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
            return Task.FromResult(entity);
        }

        public Task<List<PermissionGrantEntity>> InsertBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            var list = entities.ToList();
            foreach (var entity in list) _items.Add(entity);
            return Task.FromResult(list);
        }

        public Task<bool> DeleteAsync(
            PermissionGrantEntity entity, CancellationToken ct = default)
            => Task.FromResult(_items.Remove(entity));

        public Task UpdateAsync(PermissionGrantEntity entity, CancellationToken ct = default)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) _items[index] = entity;
            return Task.CompletedTask;
        }

        public Task UpdateBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.CompletedTask;
        }

        public Task<int> UpdateColumnsBatchAsync<TColumns>(
            IEnumerable<PermissionGrantEntity> entities,
            Expression<Func<PermissionGrantEntity, TColumns>> columns,
            CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.FromResult(entities.Count());
        }

        public Task<int> UpdateWhereAsync<TColumns>(
            Expression<Func<PermissionGrantEntity, bool>> where,
            Expression<Func<PermissionGrantEntity, TColumns>> setColumns,
            CancellationToken ct = default)
            => throw new NotSupportedException("InMemoryEntityDac 不实现 UpdateWhereAsync（测试集未使用，ADR89 契约测试另设 fixture）。");
    }
}
