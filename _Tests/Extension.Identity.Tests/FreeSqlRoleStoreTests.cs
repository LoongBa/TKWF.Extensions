using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// FreeSqlRoleStore 测试——角色 CRUD、名称查询、系统角色/已分配角色删除保护。
/// </summary>
public class FreeSqlRoleStoreTests
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

    private static FreeSqlRoleStore CreateStore(IFreeSql fsql)
        => new(fsql, NullLogger<FreeSqlRoleStore>.Instance);

    private static RoleEntity NewRole(string name = "Admin")
        => new() { Name = name, DisplayName = name };

    [Fact]
    public async Task CreateAsync_SetsId()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole();

        await store.CreateAsync(role, CancellationToken.None);

        Assert.True(role.Id > 0);
        var saved = fsql.Select<RoleEntity>().Where(r => r.Id == role.Id).First();
        Assert.Equal("Admin", saved.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsRole()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole();
        await store.CreateAsync(role, CancellationToken.None);

        var result = await store.GetByIdAsync(role.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(role.Id, result!.Id);
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsRole()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole("Admin");
        await store.CreateAsync(role, CancellationToken.None);

        var result = await store.GetByNameAsync("Admin", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Admin", result!.Name);
    }

    [Fact]
    public async Task GetByNameAsync_NotExists_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var result = await store.GetByNameAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetListAsync_ReturnsPaged()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        for (var i = 0; i < 3; i++)
            await store.CreateAsync(NewRole($"Role{i}"), CancellationToken.None);

        var list = await store.GetListAsync(0, 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal(3, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesRole()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole("Admin");
        await store.CreateAsync(role, CancellationToken.None);
        role.DisplayName = "超级管理员";

        await store.UpdateAsync(role, CancellationToken.None);

        var saved = fsql.Select<RoleEntity>().Where(r => r.Id == role.Id).First();
        Assert.Equal("超级管理员", saved.DisplayName);
    }

    [Fact]
    public async Task DeleteAsync_NormalRole_ReturnsTrue()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole("Editor");
        await store.CreateAsync(role, CancellationToken.None);

        var result = await store.DeleteAsync(role.Id, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task DeleteAsync_SystemRole_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole("Admin");
        role.IsSystemRole = true;
        await store.CreateAsync(role, CancellationToken.None);

        var result = await store.DeleteAsync(role.Id, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task DeleteAsync_AssignedRole_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var role = NewRole("Admin");
        await store.CreateAsync(role, CancellationToken.None);
        var user = new UserEntity
        {
            UserName = "alice",
            NormalizedUserName = "ALICE",
            DisplayName = "alice"
        };
        user.Id = await fsql.Insert(user).ExecuteIdentityAsync();
        await fsql.Insert(new UserRoleEntity { UserId = user.Id, RoleId = role.Id }).ExecuteAffrowsAsync();

        var result = await store.DeleteAsync(role.Id, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task DeleteAsync_NotExists_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var result = await store.DeleteAsync(999, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Operations_FailSilently_OnNullEntity()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        await store.CreateAsync(null!, CancellationToken.None);
        await store.UpdateAsync(null!, CancellationToken.None);

        Assert.Equal(0, fsql.Select<RoleEntity>().Count());
    }
}