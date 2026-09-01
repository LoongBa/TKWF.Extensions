using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Cryptography;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// UserManager 测试——创建用户（密码散列）、凭据验证、改密、角色分配。
/// </summary>
public class UserManagerTests
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

    private static UserManager CreateManager(IFreeSql fsql, IdentityOptions? options = null)
    {
        var store = new FreeSqlUserStore(fsql, NullLogger<FreeSqlUserStore>.Instance);
        var roleStore = new FreeSqlRoleStore(fsql, NullLogger<FreeSqlRoleStore>.Instance);
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options ?? new IdentityOptions());
        return new UserManager(store, roleStore, optionsWrapper, NullLogger<UserManager>.Instance);
    }

    [Fact]
    public async Task CreateUserAsync_HashesPassword()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var user = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);

        Assert.NotNull(user);
        Assert.True(user!.Id > 0);
        var saved = fsql.Select<UserEntity>().Where(u => u.Id == user.Id).First();
        Assert.NotEqual("secret123", saved.PasswordHash);
        Assert.True(PasswordHasher.VerifyPassword("secret123", saved.PasswordHash!));
        Assert.Equal("ALICE", saved.NormalizedUserName);
    }

    [Fact]
    public async Task CreateUserAsync_ShortPassword_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql, new IdentityOptions { PasswordMinLength = 6 });

        var user = await manager.CreateUserAsync("alice", "123", "Alice", CancellationToken.None);

        Assert.Null(user);
        Assert.Equal(0, fsql.Select<UserEntity>().Count());
    }

    [Fact]
    public async Task FindByNameAsync_IsCaseInsensitive()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await manager.CreateUserAsync("Alice", "secret123", "Alice", CancellationToken.None);

        var user = await manager.FindByNameAsync("alice", CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("Alice", user!.UserName);
    }

    [Fact]
    public async Task VerifyCredentials_CorrectPassword_ReturnsUser()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var created = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);

        var user = await manager.VerifyCredentialsAsync("alice", "secret123", CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(created!.Id, user!.Id);
    }

    [Fact]
    public async Task VerifyCredentials_WrongPassword_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);

        var user = await manager.VerifyCredentialsAsync("alice", "wrongpass", CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public async Task VerifyCredentials_UnknownUser_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var user = await manager.VerifyCredentialsAsync("nobody", "secret123", CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public async Task VerifyCredentials_DisabledUser_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var created = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);
        created!.IsActive = false;
        await manager.UpdateUserAsync(created, CancellationToken.None);

        var user = await manager.VerifyCredentialsAsync("alice", "secret123", CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesHash()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var created = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);

        await manager.ChangePasswordAsync(created!.Id, "newpass456", CancellationToken.None);

        var user = await manager.VerifyCredentialsAsync("alice", "newpass456", CancellationToken.None);
        Assert.NotNull(user);
        var old = await manager.VerifyCredentialsAsync("alice", "secret123", CancellationToken.None);
        Assert.Null(old);
    }

    [Fact]
    public async Task AssignRolesAsync_ReplacesExisting()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var user = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);
        var role1 = await manager.CreateRoleAsync("Role1", "角色1", false, CancellationToken.None);
        var role2 = await manager.CreateRoleAsync("Role2", "角色2", false, CancellationToken.None);
        var role3 = await manager.CreateRoleAsync("Role3", "角色3", false, CancellationToken.None);

        await manager.AssignRolesAsync(user!.Id, new[] { role1!.Id, role2!.Id }, CancellationToken.None);
        await manager.AssignRolesAsync(user.Id, new[] { role2!.Id, role3!.Id }, CancellationToken.None);

        var roles = await manager.GetUserRolesAsync(user.Id, CancellationToken.None);

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Name == "Role2");
        Assert.Contains(roles, r => r.Name == "Role3");
        Assert.DoesNotContain(roles, r => r.Name == "Role1");
    }

    [Fact]
    public async Task DeleteUserAsync_RemovesUserAndAssignments()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var user = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        await manager.DeleteUserAsync(user.Id, CancellationToken.None);

        Assert.Equal(0, fsql.Select<UserEntity>().Count());
        Assert.Equal(0, fsql.Select<UserRoleEntity>().Count());
        // 角色保留
        Assert.Equal(1, fsql.Select<RoleEntity>().Count());
    }

    [Fact]
    public async Task CreateRoleAsync_ReturnsRole()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var role = await manager.CreateRoleAsync("Editor", "编辑", false, CancellationToken.None);

        Assert.NotNull(role);
        Assert.True(role!.Id > 0);
        Assert.False(role.IsSystemRole);
    }

    [Fact]
    public async Task DeleteRoleAsync_SystemRole_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);

        var result = await manager.DeleteRoleAsync(role!.Id, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, fsql.Select<RoleEntity>().Count());
    }
}