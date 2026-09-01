using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// DefaultPasswordResetFlow 测试——发起/完成/防枚举/过期/幂等 + 密码管理器缺失容错。
/// </summary>
public class DefaultPasswordResetFlowTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<PasswordResetCodeEntity>();
        return fsql;
    }

    private static DefaultPasswordResetFlow CreateFlow(IFreeSql fsql, IAccountPasswordManager? passwordManager = null)
    {
        var services = new ServiceCollection();
        if (passwordManager != null)
            services.AddSingleton(passwordManager);
        var sp = services.BuildServiceProvider();

        var store = new FreeSqlPasswordResetStore(fsql, NullLogger<FreeSqlPasswordResetStore>.Instance);
        return new DefaultPasswordResetFlow(
            store,
            sp,
            Options.Create(new AccountOptions()),
            NullLogger<DefaultPasswordResetFlow>.Instance);
    }

    private sealed class FakePasswordManager : IAccountPasswordManager
    {
        public bool UserExists { get; set; } = true;
        public bool SetResult { get; set; } = true;
        public string? SavedUserName { get; private set; }
        public string? SavedHash { get; private set; }

        public Task<bool> UserExistsAsync(string userName, CancellationToken ct)
            => Task.FromResult(UserExists);

        public Task<bool> SetPasswordAsync(string userName, string newClientHash, string salt, CancellationToken ct)
        {
            SavedUserName = userName;
            SavedHash = newClientHash;
            return Task.FromResult(SetResult);
        }
    }

    [Fact]
    public async Task InitiateReset_GeneratesCode_StoresToDatabase()
    {
        var fsql = CreateFreeSql();
        var flow = CreateFlow(fsql, new FakePasswordManager());

        var result = await flow.InitiateResetAsync("alice", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, fsql.Select<PasswordResetCodeEntity>().Count());
        var saved = fsql.Select<PasswordResetCodeEntity>().First();
        Assert.Equal("alice", saved.UserName);
        Assert.Equal(8, saved.ResetCode.Length);
    }

    [Fact]
    public async Task InitiateReset_NonExistentUser_ReturnsTrue_AntiEnumeration()
    {
        var fsql = CreateFreeSql();
        var manager = new FakePasswordManager { UserExists = false };
        var flow = CreateFlow(fsql, manager);

        var result = await flow.InitiateResetAsync("nobody", CancellationToken.None);

        // 防用户枚举：用户不存在也返回 true，且不生成码
        Assert.True(result);
        Assert.Equal(0, fsql.Select<PasswordResetCodeEntity>().Count());
    }

    [Fact]
    public async Task InitiateReset_NoPasswordManager_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var flow = CreateFlow(fsql, passwordManager: null); // 未注册

        var result = await flow.InitiateResetAsync("alice", CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, fsql.Select<PasswordResetCodeEntity>().Count());
    }

    [Fact]
    public async Task CompleteReset_ValidCode_SetsPassword_MarksUsed()
    {
        var fsql = CreateFreeSql();
        var manager = new FakePasswordManager();
        var flow = CreateFlow(fsql, manager);
        await flow.InitiateResetAsync("alice", CancellationToken.None);
        var saved = fsql.Select<PasswordResetCodeEntity>().First();

        var result = await flow.CompleteResetAsync("alice", saved.ResetCode, "CLIENT_HASH", "SALT", CancellationToken.None);

        Assert.True(result.Success, $"重置失败: {result.Message}");
        Assert.Equal("alice", manager.SavedUserName);
        Assert.Equal("CLIENT_HASH", manager.SavedHash);
        var updated = fsql.Select<PasswordResetCodeEntity>().Where(p => p.Id == saved.Id).First();
        Assert.True(updated.IsUsed);
    }

    [Fact]
    public async Task CompleteReset_WrongCode_ReturnsFailure()
    {
        var fsql = CreateFreeSql();
        var flow = CreateFlow(fsql, new FakePasswordManager());
        await flow.InitiateResetAsync("alice", CancellationToken.None);

        var result = await flow.CompleteResetAsync("alice", "WRONG000", "HASH", "SALT", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task CompleteReset_UsedCode_ReturnsFailure_NoDoubleConsume()
    {
        var fsql = CreateFreeSql();
        var manager = new FakePasswordManager();
        var flow = CreateFlow(fsql, manager);
        await flow.InitiateResetAsync("alice", CancellationToken.None);
        var saved = fsql.Select<PasswordResetCodeEntity>().First();

        await flow.CompleteResetAsync("alice", saved.ResetCode, "HASH1", "SALT", CancellationToken.None);
        // 第二次使用同一码（已标记 Used）
        var second = await flow.CompleteResetAsync("alice", saved.ResetCode, "HASH2", "SALT", CancellationToken.None);

        Assert.False(second.Success);
        Assert.NotEqual("HASH2", manager.SavedHash); // 未再落地
    }

    [Fact]
    public async Task CompleteReset_ExpiredCode_ReturnsFailure()
    {
        var fsql = CreateFreeSql();
        var flow = CreateFlow(fsql, new FakePasswordManager());
        await fsql.Insert(new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "EXPIRED1",
            ExpireTime = DateTime.Now.AddMinutes(-1) // 已过期
        }).ExecuteAffrowsAsync();

        var result = await flow.CompleteResetAsync("alice", "EXPIRED1", "HASH", "SALT", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CompleteReset_NoPasswordManager_ReturnsFailure()
    {
        var fsql = CreateFreeSql();
        var flow = CreateFlow(fsql, passwordManager: null);

        var result = await flow.CompleteResetAsync("alice", "ANY", "HASH", "SALT", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("密码重置未启用", result.Message);
    }
}