using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Utility.Cryptography;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// FreeSqlUserStore 测试——用户 CRUD、规范化查询、用户-角色分配、异常静默。
/// </summary>
public class FreeSqlUserStoreTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<UserEntity>();
        fsql.CodeFirst.SyncStructure<RoleEntity>();
        fsql.CodeFirst.SyncStructure<UserRoleEntity>();
        return fsql;
    }

    private static FreeSqlUserStore CreateStore(IFreeSql fsql)
        => new(fsql, NullLogger<FreeSqlUserStore>.Instance);

    private static UserEntity NewUser(string name = "alice")
        => new()
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            DisplayName = name,
            PasswordHash = PasswordHasher.HashPassword("secret123"),
            IsActive = true
        };

    [Fact]
    public async Task CreateAsync_SetsId()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();

        await store.CreateAsync(user, CancellationToken.None);

        Assert.True(user.Id > 0);
        var saved = fsql.Select<UserEntity>().Where(u => u.Id == user.Id).First();
        Assert.Equal("alice", saved.UserName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUser()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);

        var result = await store.GetByIdAsync(user.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.Id);
    }

    [Fact]
    public async Task GetByUserNameAsync_MatchesNormalized()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser("Alice");
        await store.CreateAsync(user, CancellationToken.None);

        var result = await store.GetByUserNameAsync("ALICE", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Alice", result!.UserName);
    }

    [Fact]
    public async Task GetByUserNameAsync_NotExists_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var result = await store.GetByUserNameAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByEmailAsync_ReturnsUser()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        user.Email = "alice@example.com";
        await store.CreateAsync(user, CancellationToken.None);

        var result = await store.GetByEmailAsync("alice@example.com", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("alice@example.com", result!.Email);
    }

    [Fact]
    public async Task GetListAsync_ReturnsPaged()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        for (var i = 0; i < 3; i++)
            await store.CreateAsync(NewUser($"user{i}"), CancellationToken.None);

        var list = await store.GetListAsync(0, 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal(3, fsql.Select<UserEntity>().Count());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesUser()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);
        user.DisplayName = "Alice Updated";

        await store.UpdateAsync(user, CancellationToken.None);

        var saved = fsql.Select<UserEntity>().Where(u => u.Id == user.Id).First();
        Assert.Equal("Alice Updated", saved.DisplayName);
    }

    [Fact]
    public async Task DeleteAsync_CascadesUserRoles()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);
        var role = new RoleEntity { Name = "Admin", DisplayName = "管理员" };
        await fsql.Insert(role).ExecuteAffrowsAsync();
        await store.AssignRoleAsync(user.Id, role.Id, CancellationToken.None);

        await store.DeleteAsync(user.Id, CancellationToken.None);

        Assert.Equal(0, fsql.Select<UserEntity>().Count());
        Assert.Equal(0, fsql.Select<UserRoleEntity>().Count());
        // 角色本身保留
        Assert.Equal(1, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task AssignRole_And_GetRoles_Work()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);
        var role = new RoleEntity { Name = "Admin", DisplayName = "管理员" };
        role.Id = await fsql.Insert(role).ExecuteIdentityAsync();

        await store.AssignRoleAsync(user.Id, role.Id, CancellationToken.None);
        var roles = await store.GetRolesAsync(user.Id, CancellationToken.None);

        Assert.Single(roles);
        Assert.Equal("Admin", roles[0].Name);
    }

    [Fact]
    public async Task AssignRole_Duplicate_IsIdempotent()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);
        var role = new RoleEntity { Name = "Admin", DisplayName = "管理员" };
        role.Id = await fsql.Insert(role).ExecuteIdentityAsync();

        await store.AssignRoleAsync(user.Id, role.Id, CancellationToken.None);
        await store.AssignRoleAsync(user.Id, role.Id, CancellationToken.None);

        Assert.Equal(1, fsql.Select<UserRoleEntity>().Count());
    }

    [Fact]
    public async Task RemoveRole_Works()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);
        var role = new RoleEntity { Name = "Admin", DisplayName = "管理员" };
        role.Id = await fsql.Insert(role).ExecuteIdentityAsync();
        await store.AssignRoleAsync(user.Id, role.Id, CancellationToken.None);

        await store.RemoveRoleAsync(user.Id, role.Id, CancellationToken.None);

        Assert.Equal(0, fsql.Select<UserRoleEntity>().Count());
    }

    [Fact]
    public async Task GetRoles_NoAssignment_ReturnsEmpty()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var user = NewUser();
        await store.CreateAsync(user, CancellationToken.None);

        var roles = await store.GetRolesAsync(user.Id, CancellationToken.None);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task Operations_FailSilently_OnNullEntity()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        // null 实体不应抛异常
        await store.CreateAsync(null!, CancellationToken.None);
        await store.UpdateAsync(null!, CancellationToken.None);

        Assert.Equal(0, fsql.Select<UserEntity>().Count());
    }
}