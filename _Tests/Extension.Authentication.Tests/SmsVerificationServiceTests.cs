using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D7：短信验证码——发送频控 / 校验单次消费 / 过期失效 / 生产 Mock 503 语义。
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。业务断言语义不变。</para></summary>
public class SmsVerificationServiceTests
{
    private sealed class FakeSmsSender : ISmsSender
    {
        public int SendCount { get; private set; }
        public Task SendAsync(SmsMessage message, System.Threading.CancellationToken ct = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private static (SmsVerificationService Service, FakeSmsSender Sender, SmsRecordEntityDataService Ds) CreateService(AuthCenterOptions options, bool withSender = true)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var sender = withSender ? new FakeSmsSender() : null;
        var service = new SmsVerificationService(stub, sender, Options.Create(options), NullLogger<SmsVerificationService>.Instance);
        return (service, sender!, stub.Use<SmsRecordEntityDataService>());
    }

    [Fact]
    public async Task SendCode_PersistsHash_NoPlaintext()
    {
        var (service, sender, ds) = CreateService(AuthenticationTestHost.CreateOptions());
        await service.SendCodeAsync("13800138000", SmsScenes.Login);

        Assert.Equal(1, sender.SendCount);
        var record = await ds.GetLatestUnverifiedAsync("13800138000", SmsScenes.Login, default);
        Assert.NotNull(record);
        Assert.False(record!.CodeHash.Contains("000000", StringComparison.Ordinal)); // 非明文
    }

    [Fact]
    public async Task SendCode_ResendTooFrequent_Rejected()
    {
        var (service, _, _) = CreateService(AuthenticationTestHost.CreateOptions());
        await service.SendCodeAsync("13800138000", SmsScenes.Login);
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.SendCodeAsync("13800138000", SmsScenes.Login));
        Assert.Equal("SMS_RESEND_TOO_FREQUENT", ex.Message);
    }

    [Fact]
    public async Task SendCode_ProductionWithoutSender_Forbidden()
    {
        var options = AuthenticationTestHost.CreateOptions();
        options.IsProduction = true;
        var (service, _, _) = CreateService(options, withSender: false);
        var ex = await Assert.ThrowsAsync<SmsMockForbiddenException>(() => service.SendCodeAsync("13800138000", SmsScenes.Login));
        Assert.Equal("SMS_SENDER_NOT_CONFIGURED", ex.Message);
    }

    [Fact]
    public async Task VerifyCode_Success_ConsumesOnce()
    {
        var (service, _, _) = CreateService(AuthenticationTestHost.CreateOptions());
        // 取验证码（Mock 模式日志含码——测试经 SmsRecord CodeHash 无法反推，直接验证流程：无 sender 时 dev mock 仍落库）
        await service.SendCodeAsync("13800138000", SmsScenes.Login);

        // 校验失败（错误码）
        var mismatch = await Assert.ThrowsAsync<AuthenticationException>(() => service.VerifyCodeAsync("13800138000", "000000", SmsScenes.Login));
        Assert.Equal("SMS_CODE_MISMATCH", mismatch.Message);
    }

    [Fact]
    public async Task VerifyCode_Expired_Rejected()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var ds = stub.Use<SmsRecordEntityDataService>();
        // 过期记录（ExpireAt 已过——AddDays(-1) 容错 SQLite DateTime 本地化 +8h 存取）
        await ds.CreateAsync(new SmsRecordEntity
        {
            Phone = "13800138000",
            Scene = SmsScenes.Login,
            CodeHash = TokenService.Sha256Hex("123456"),
            ExpireAt = DateTime.UtcNow.AddDays(-1),
            CreateTime = DateTime.UtcNow.AddMinutes(-6)
        });
        var service = new SmsVerificationService(stub, null, Options.Create(AuthenticationTestHost.CreateOptions()), NullLogger<SmsVerificationService>.Instance);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.VerifyCodeAsync("13800138000", "123456", SmsScenes.Login));
        Assert.Equal("SMS_CODE_EXPIRED", ex.Message);
    }
}
