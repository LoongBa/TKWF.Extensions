using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N-write：AuthAccountService——写契约委托真实 DataService（SQLite 内存库）。
/// <para>DMP OI7 转达缺口闭环：公开写契约使消费端可建/维护影子 AuthAccount（成本端编译器 typeof 可引用），
/// 解除 TokenService.RefreshTokenAsync（L196-200 ACCOUNT_NOT_FOUND/REFRESH_STALE）对 DMP P1 令牌替换的阻塞。</para>
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。业务断言语义不变。</para>
/// <para>V0.9.0（ADR-密码策略与口令协议）：AuthAccountService ctor 扩参（SecurityLog Options + DomainOptions +
/// AuthCenterOptions + ICredentialProtector + ILogger——SecurePassword 协议依赖）。</para></summary>
public class AuthAccountServiceTests
{
    /// <summary>测试 AES-256-GCM 密钥（32 bytes——AesGcmCredentialProtector ctor 校验长度）。</summary>
    private static readonly byte[] TestProtectionKey = new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    private static (AuthAccountService Service, AuthAccountEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new AuthAccountService(
            stub,
            Options.Create(new SecurityLoggingOptions()),           // SecurityLog Options（冻结事件门控）
            Options.Create(new DomainOptions()),                    // DomainOptions.Auth.Pbkdf2Iterations=600000（SecurePassword 单一来源）
            Options.Create(AuthenticationTestHost.CreateOptions()), // AuthCenterOptions（PasswordPolicy/LoginProtection）
            new FakeCredentialProtector(),       // ICredentialProtector（AES-256-GCM）
            NullLogger<AuthAccountService>.Instance);
        return (service, stub.Use<AuthAccountEntityDataService>());
    }

    [Fact]
    public async Task Create_Then_GetByUId_ReturnsShadowAccount()
    {
        var (service, _) = CreateService();
        var account = new AuthAccountEntity { UId = "u-admin-1", IsEnabled = true }; // 影子账号：Phone 可空（唯一索引 NULL 放行）

        await service.CreateAsync(account);

        Assert.True(account.Id > 0);                    // 回写自增 Id
        Assert.NotEqual(default, account.CreateTime);    // UTC 时间戳写入

        var read = await service.GetByUIdAsync("u-admin-1");
        Assert.NotNull(read);
        Assert.Equal("u-admin-1", read!.UId);
        Assert.True(read.IsEnabled);
    }

    [Fact]
    public async Task Update_Modifies_Fields()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new AuthAccountEntity { UId = "u-admin-1", IsEnabled = true });

        var read = await service.GetByUIdAsync("u-admin-1");
        read!.IsEnabled = false;                       // 禁用路径（DMP 管理员停用 → 令牌刷新 ACCOUNT_NOT_FOUND）
        await service.UpdateAsync(read);

        var updated = await service.GetByUIdAsync("u-admin-1");
        Assert.False(updated!.IsEnabled);
    }

    [Fact]
    public async Task IncrementTokenVersion_Invalidates_OldRefresh()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new AuthAccountEntity { UId = "u-admin-1", IsEnabled = true });
        Assert.Equal(0, (await service.GetByUIdAsync("u-admin-1"))!.TokenVersion);

        // DMP 改密 → bump → 旧 refresh TokenVersion 不匹配 → 扩展 TokenService REFRESH_STALE（不再续期）
        await service.IncrementTokenVersionAsync("u-admin-1");

        Assert.Equal(1, (await service.GetByUIdAsync("u-admin-1"))!.TokenVersion);
    }

    [Fact]
    public void Contract_Public_Interface_InternalImpl()
    {
        // 消费端编译期 typeof 引用能力 = 契约公开（DMP OI7 核心诉求——internal 类型注册键消费端无法引用）
        Assert.True(typeof(IAuthAccountService).IsPublic);
        Assert.Contains(typeof(IAuthAccountService), typeof(AuthAccountService).GetInterfaces());

        // internal sealed 实现（对齐 AuthAccountQueryService 先例）
        var impl = typeof(AuthAccountService);
        Assert.True(impl.IsNotPublic);
        Assert.True(impl.IsSealed);
    }
}
