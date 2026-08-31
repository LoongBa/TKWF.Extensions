using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.4.0（G3）+ V0.7.0（W3）：PermissionExtensionInitializer.InitializeAsync 种子初始化测试。
/// <para>覆盖要点：IServiceProviderAware 注入；V0.7.0 起预置 admin 角色 Admin.All 系统权限
/// （替代 V0.4.0 的逐权限授予）；仅插入缺失记录（不覆盖既有授予/撤销）；空角色名禁用种子；
/// 未注册 IEntityDAC（真实持久化未接线）时跳过。</para>
/// </summary>
public class PermissionSeedInitializerTests
{
    private const string RoleProvider = "Role";

    private sealed record Def(string Name);

    /// <summary>构建带 InMemory 持久化的 DI 容器 + 注入权限定义仓库。</summary>
    private static ServiceProvider BuildSvcProvider(
        bool registerDac, string? seedRole, params string[] permissionNames)
    {
        var services = new ServiceCollection();

        // 权限定义仓库（注入两个已定义权限）
        var repository = new InMemoryPermissionDefinitionRepository();
        foreach (var name in permissionNames)
        {
            repository.AddRange(new[]
            {
                new PermissionDefinition { Name = name, DisplayName = name, Group = "Test" }
            });
        }
        services.AddScoped<IPermissionDefinitionRepository>(_ => repository);

        if (registerDac)
        {
            var dac = new InMemoryEntityDac();
            services.AddScoped<IEntityDAC<PermissionGrantEntity>>(_ => dac);
            services.AddScoped<IDomainUser, StubDomainUser>();
            services.AddScoped<PermissionGrantEntityDataService>();
        }

        if (seedRole is not null)
            services.AddSingleton<IOptions<PermissionOptions>>(
                Options.Create(new PermissionOptions { SeedAdminRoleName = seedRole }));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InitializeAsync_SeedsAdminRole_AdminAllSystemPermission()
    {
        var sp = BuildSvcProvider(registerDac: true, seedRole: "admin", "Order.Create", "Order.Delete");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo> { ServiceProvider = sp };

        await initializer.InitializeAsync();

        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PermissionGrantEntityDataService>();
        var adminAll = await svc.GetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin");
        Assert.NotNull(adminAll);
        Assert.True(adminAll!.IsGranted);

        // V0.7.0：种子只预置 Admin.All 系统权限，不再逐权限授予
        var grants = await svc.GetByProviderAsync(RoleProvider, "admin");
        Assert.Single(grants);
        Assert.Equal(PermissionNames.AdminAll, grants[0].PermissionName);
    }

    [Fact]
    public async Task InitializeAsync_Idempotent_DoesNotDuplicate()
    {
        var sp = BuildSvcProvider(registerDac: true, seedRole: "admin", "Order.Create");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo> { ServiceProvider = sp };

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PermissionGrantEntityDataService>();
        var grants = await svc.GetByProviderAsync(RoleProvider, "admin");
        Assert.Single(grants);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotOverwriteExistingRevoke()
    {
        var sp = BuildSvcProvider(registerDac: true, seedRole: "admin", "Order.Create");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo> { ServiceProvider = sp };

        using (var scope = sp.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<PermissionGrantEntityDataService>();
            // 消费方先显式撤销 Admin.All（记录已存在，IsGranted=false）
            await svc.SetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin", isGranted: false);
        }

        await initializer.InitializeAsync();

        using var verifyScope = sp.CreateScope();
        var verifySvc = verifyScope.ServiceProvider.GetRequiredService<PermissionGrantEntityDataService>();
        var grant = await verifySvc.GetGrantAsync(PermissionNames.AdminAll, RoleProvider, "admin");
        Assert.NotNull(grant);
        Assert.False(grant!.IsGranted); // 未被种子覆盖
    }

    [Fact]
    public async Task InitializeAsync_EmptySeedRole_DisablesSeeding()
    {
        var sp = BuildSvcProvider(registerDac: true, seedRole: "", "Order.Create");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo> { ServiceProvider = sp };

        await initializer.InitializeAsync();

        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PermissionGrantEntityDataService>();
        var grants = await svc.GetByProviderAsync(RoleProvider, "admin");
        Assert.Empty(grants);
    }

    [Fact]
    public async Task InitializeAsync_NoDacRegistered_SkipsSeeding()
    {
        // 未注册 IEntityDAC（真实持久化未接线）→ 无 DataService → 跳过种子
        var sp = BuildSvcProvider(registerDac: false, seedRole: "admin", "Order.Create");
        var initializer = new PermissionExtensionInitializer<SimpleUserInfo> { ServiceProvider = sp };

        await initializer.InitializeAsync(); // 不应抛异常
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
    }
}
