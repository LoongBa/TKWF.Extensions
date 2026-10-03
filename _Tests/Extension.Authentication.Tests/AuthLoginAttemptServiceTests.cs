using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D6：AuthLoginAttempt 限流窗口计数 + 策略隔离（wechat 60s / redeem 60min / sms 委托）。</summary>
public class AuthLoginAttemptServiceTests
{
    private static (AuthLoginAttemptService Service, AuthLoginAttemptEntityDataService Ds) CreateService(AuthCenterOptions options)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = new StubDomainUser();
        var ds = new AuthLoginAttemptEntityDataService(
            stub, new FreeSqlEntityDAC<AuthLoginAttemptEntity>(new UnitOfWorkManager(fsql)));
        stub.Register(ds);
        return (new AuthLoginAttemptService(Options.Create(options), stub, NullLogger<AuthLoginAttemptService>.Instance), ds);
    }

    private static async Task RecordNAsync(AuthLoginAttemptService service, string identity, string authType, int n)
    {
        for (var i = 0; i < n; i++)
        {
            await service.RecordAttemptAsync(new AuthLoginAttemptEntity
            {
                UserIdentity = identity,
                AuthType = authType,
                IsSuccess = false,
                AttemptTime = DateTime.UtcNow
            });
        }
    }

    [Fact]
    public async Task IsRateLimited_WechatWindow_ExceedsThreshold()
    {
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.OAuthPerMinutePerIp = 3;
        var (service, _) = CreateService(options);
        var identity = "13800138000";

        await RecordNAsync(service, identity, AuthTypes.Wechat, 2);
        Assert.False(await service.IsRateLimitedAsync(identity, AuthTypes.Wechat));

        await RecordNAsync(service, identity, AuthTypes.Wechat, 1);
        Assert.True(await service.IsRateLimitedAsync(identity, AuthTypes.Wechat));
    }

    [Fact]
    public async Task IsRateLimited_AuthTypeIsolated()
    {
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.OAuthPerMinutePerIp = 2;
        options.LoginProtection.RedeemPerHour = 5;
        var (service, _) = CreateService(options);
        var identity = "u-1";

        await RecordNAsync(service, identity, AuthTypes.Wechat, 5);   // 超 wechat 阈值
        await RecordNAsync(service, identity, AuthTypes.Redeem, 3);   // 未超 redeem 阈值

        Assert.True(await service.IsRateLimitedAsync(identity, AuthTypes.Wechat));
        Assert.False(await service.IsRateLimitedAsync(identity, AuthTypes.Redeem));
    }

    [Fact]
    public async Task IsRateLimited_Sms_ReturnsFalse_DelegatedToSmsVerification()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        await RecordNAsync(service, "13800138000", AuthTypes.Sms, 100);
        Assert.False(await service.IsRateLimitedAsync("13800138000", AuthTypes.Sms));
    }

    [Fact]
    public async Task GetRecentAttempts_ReturnsLatest()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        await RecordNAsync(service, "13800138000", AuthTypes.Wechat, 3);
        var recent = await service.GetRecentAttemptsAsync("13800138000", AuthTypes.Wechat, 2);
        Assert.Equal(2, recent.Count);
    }
}
