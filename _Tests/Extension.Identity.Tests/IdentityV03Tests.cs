using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKWF.Ext.Account;
using TKWF.Ext.Identity;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// Identity V0.3.0 测试——IAccountPasswordManager 适配器（组装方案 + 可配置迭代）、
/// IRoleProvider 实时查库（Scoped 缓存）、IdentityAuthService 注册/登录、IdentityUserHelperBase 登录衔接基类。
/// </summary>
public class IdentityV03Tests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        IdentityTestHost.SyncSchema(fsql);
        return fsql;
    }

    private static IUserManager CreateManager(IFreeSql fsql)
        => IdentityTestHost.CreateUserManager(fsql);

    private static UserManager CreateConcreteManager(IFreeSql fsql)
        => IdentityTestHost.CreateUserManager(fsql) as UserManager
           ?? throw new InvalidOperationException("CreateUserManager 应返回 UserManager");

    // ── IdentityPasswordManager（组装方案 + 可配置迭代）──

    [Fact]
    public async Task PasswordManager_SetPassword_AssemblesFormat_AndVerifyPasses()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await manager.CreateUserAsync("alice", "oldpass123", "Alice", CancellationToken.None);

        // 模拟 Account SecurePassword 重置：客户端用 PBKDF2(明文, salt, 600000) 输出 32 bytes hex
        // （真实 PBKDF2——对齐 ts-client crypto.ts deriveBits(..., 256) 与服务端 Rfc2898DeriveBytes.Pbkdf2 语义）
        const string plaintext = "newpass456";
        var saltBytes = new byte[32];
        Random.Shared.NextBytes(saltBytes);
        var iterations = 600000;   // DomainOptions.Auth.Pbkdf2Iterations 默认
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(plaintext, saltBytes, iterations, HashAlgorithmName.SHA256, 32);
        var saltHex = Convert.ToHexString(saltBytes).ToLowerInvariant();
        var clientHashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var options = Options.Create(new DomainOptions());   // Pbkdf2Iterations 默认 600000
        var pm = new IdentityPasswordManager(manager, options);

        Assert.True(await pm.SetPasswordAsync("alice", clientHashHex, saltHex, CancellationToken.None));

        // 验证组装格式：PasswordHash = "600000.{base64salt}.{base64hash}"，与客户端参数一致
        var user = await manager.FindByNameAsync("alice", CancellationToken.None);
        Assert.NotNull(user);
        var parts = user!.PasswordHash!.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("600000", parts[0]);
        Assert.Equal(Convert.ToBase64String(saltBytes), parts[1]);
        Assert.Equal(Convert.ToBase64String(hashBytes), parts[2]);

        // 端到端验证（C1）：组装后明文密码可登录（PasswordHasher.VerifyPassword 解析格式内嵌迭代重算比对）
        var verified = await manager.VerifyCredentialsAsync("alice", plaintext, CancellationToken.None);
        Assert.NotNull(verified);                       // 组装后能登录
        var oldFail = await manager.VerifyCredentialsAsync("alice", "oldpass123", CancellationToken.None);
        Assert.Null(oldFail);                           // 旧密码失败
    }

    [Fact]
    public async Task PasswordManager_UserExists_TrueAndFalse()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var pm = new IdentityPasswordManager(manager, Options.Create(new DomainOptions()));

        await manager.CreateUserAsync("bob", "pass12345", "Bob", CancellationToken.None);
        Assert.True(await pm.UserExistsAsync("bob", CancellationToken.None));
        Assert.False(await pm.UserExistsAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task PasswordManager_InvalidHex_ReturnsFalse()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var pm = new IdentityPasswordManager(manager, Options.Create(new DomainOptions()));

        await manager.CreateUserAsync("carol", "pass12345", "Carol", CancellationToken.None);
        Assert.False(await pm.SetPasswordAsync("carol", "not-hex!", "not-hex!", CancellationToken.None));
    }

    [Fact]
    public async Task PasswordManager_WrongHashLength_ReturnsFalse()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var pm = new IdentityPasswordManager(manager, Options.Create(new DomainOptions()));

        await manager.CreateUserAsync("dave", "pass12345", "Dave", CancellationToken.None);
        var saltHex = Convert.ToHexString(new byte[32]).ToLowerInvariant();
        var shortHashHex = Convert.ToHexString(new byte[16]).ToLowerInvariant();  // 16 bytes ≠ 32
        Assert.False(await pm.SetPasswordAsync("dave", shortHashHex, saltHex, CancellationToken.None));
    }

    [Fact]
    public async Task PasswordManager_CustomIterations_FromOptions()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await manager.CreateUserAsync("erin", "pass12345", "Erin", CancellationToken.None);

        var options = Options.Create(new DomainOptions { Auth = { Pbkdf2Iterations = 123456 } });  // 自定义迭代（可配置）
        var pm = new IdentityPasswordManager(manager, options);

        var saltHex = Convert.ToHexString(new byte[32]).ToLowerInvariant();
        var hashHex = Convert.ToHexString(new byte[32]).ToLowerInvariant();
        Assert.True(await pm.SetPasswordAsync("erin", hashHex, saltHex, CancellationToken.None));

        var user = await manager.FindByNameAsync("erin", CancellationToken.None);
        Assert.StartsWith("123456.", user!.PasswordHash);
    }

    // ── IdentityRoleProvider（实时查库 + Scoped 缓存）──

    [Fact]
    public async Task RoleProvider_GetRoles_RealTimeLookup()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var user = await manager.CreateUserAsync("frank", "pass12345", "Frank", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        var provider = new IdentityRoleProvider<TestUserInfo>(manager);
        var userInfo = new TestUserInfo(user.Id.ToString(), user.UserName);

        var roles = await provider.GetRolesAsync(userInfo);
        Assert.Single(roles);
        Assert.Equal("Admin", roles[0]);
    }

    [Fact]
    public async Task RoleProvider_AssignRoles_ImmediateEffect()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var user = await manager.CreateUserAsync("grace", "pass12345", "Grace", CancellationToken.None);
        var role1 = await manager.CreateRoleAsync("Role1", "角色1", false, CancellationToken.None);
        var role2 = await manager.CreateRoleAsync("Role2", "角色2", false, CancellationToken.None);
        var userInfo = new TestUserInfo(user!.Id.ToString(), user.UserName);

        var provider = new IdentityRoleProvider<TestUserInfo>(manager);
        Assert.Empty(await provider.GetRolesAsync(userInfo));       // 无角色

        await manager.AssignRolesAsync(user.Id, new[] { role1!.Id }, CancellationToken.None);

        // 实时查库——新请求（新 provider 实例 = 新 Scoped 缓存）立即看到新角色，无需重新登录
        var provider2 = new IdentityRoleProvider<TestUserInfo>(manager);
        var afterAssign = await provider2.GetRolesAsync(userInfo);
        Assert.Single(afterAssign);
        Assert.Equal("Role1", afterAssign[0]);

        // 同请求内 Scoped 缓存（Oracle P1-2）：同一实例二次调用命中缓存，不重复查库
        var cachedAgain = await provider2.GetRolesAsync(userInfo);
        Assert.Same(afterAssign, cachedAgain);   // 缓存命中——同一引用
    }

    [Fact]
    public async Task RoleProvider_InvalidUserId_ReturnsEmpty()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var provider = new IdentityRoleProvider<TestUserInfo>(manager);

        var badUserInfo = new TestUserInfo("not-a-number", "weird");
        Assert.Empty(await provider.GetRolesAsync(badUserInfo));
    }

    // ── IdentityAuthService（注册/登录）──

    [Fact]
    public async Task AuthService_Register_CreatesUser()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var svc = IdentityTestHost.CreateAuthService(fsql);

        var result = await svc.RegisterAsync("henry", "pass12345", "Henry", CancellationToken.None);
        Assert.True(result.Success);
        Assert.NotNull(await manager.FindByNameAsync("henry", CancellationToken.None));
    }

    [Fact]
    public async Task AuthService_Register_Duplicate_ReturnsFalse()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var svc = IdentityTestHost.CreateAuthService(fsql);

        await svc.RegisterAsync("ivan", "pass12345", "Ivan", CancellationToken.None);
        var result = await svc.RegisterAsync("ivan", "pass54321", "Ivan", CancellationToken.None);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task AuthService_Login_ValidCredentials_ReturnsPayload()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var svc = IdentityTestHost.CreateAuthService(fsql);

        await svc.RegisterAsync("judy", "pass12345", "Judy", CancellationToken.None);
        var login = await svc.LoginAsync("judy", "pass12345", CancellationToken.None);

        Assert.NotNull(login);
        Assert.True(login!.Success);
        Assert.Equal("judy", login.UserName);
        Assert.Equal("Judy", login.DisplayName);
    }

    [Fact]
    public async Task AuthService_Login_InvalidCredentials_ReturnsNull()
    {
        using var fsql = CreateFreeSql();
        var svc = IdentityTestHost.CreateAuthService(fsql);

        await svc.RegisterAsync("karl", "pass12345", "Karl", CancellationToken.None);
        var login = await svc.LoginAsync("karl", "wrongpass", CancellationToken.None);
        Assert.Null(login);
    }

    // ── IdentityUserHelperBase（登录衔接基类——工厂方法验证；登录全链路走消费方 Host，见使用指南）──

    [Fact]
    public async Task UserHelperBase_Factory_BuildsUserInfoWithRoles()
    {
        using var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var user = await manager.CreateUserAsync("lily", "pass12345", "Lily", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        // 登录链路（VerifyCredentials + GetUserRoles + CreateUserInfoFromEntity 工厂）可测核心：
        // IdentityUserHelperBase.OnLoginByPasswordAsync 调 user.GetService<IUserManager>()（消费方 Host scope）——
        // 单元测试验证工厂方法契约（entity + roleNames → TUserInfo，Roles 填充）
        var roles = await manager.GetUserRolesAsync(user.Id, CancellationToken.None);
        var helper = new TestIdentityUserHelper();
        var userInfo = helper.CreateUserInfoForTest(user, roles.Select(r => r.Name));

        Assert.Equal(user.Id.ToString(), userInfo.UserIdString);
        Assert.Equal("Lily", userInfo.DisplayName);
        Assert.Contains("Admin", userInfo.Roles ?? new List<string>());
    }

    private sealed class TestIdentityUserHelper : IdentityUserHelperBase<TestUserInfo>
    {
        protected override TestUserInfo CreateUserInfoFromEntity(UserEntity entity, IEnumerable<string> roleNames)
            => new(entity.Id.ToString(), entity.UserName)
            {
                DisplayName = entity.DisplayName,
                Roles = roleNames.ToList()
            };

        public TestUserInfo CreateUserInfoForTest(UserEntity entity, IEnumerable<string> roleNames)
            => CreateUserInfoFromEntity(entity, roleNames);
    }
}
