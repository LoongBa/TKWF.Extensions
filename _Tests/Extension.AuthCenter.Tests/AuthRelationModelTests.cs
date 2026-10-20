using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

public class AuthRelationModelTests
{
    private static (StubDomainUser User, CapturingSecurityLogStore Store) CreateHost(
        SecurityLoggingOptions? securityLog = null, AuthCenterOptions? authOptions = null)
    {
        var store = new CapturingSecurityLogStore();
        var securityOptions = securityLog ?? new SecurityLoggingOptions();
        var options = authOptions ?? AuthenticationTestHost.CreateOptions();
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();

        var user = AuthenticationTestHost.CreateStub(fsql, register: (services, u) =>
        {
            services.AddSingleton<ISecurityLogStore>(store);
            services.AddSingleton<IAuthFactorService>(
                new AuthFactorService(u, Options.Create(securityOptions), NullLogger<AuthFactorService>.Instance));
            services.AddSingleton<IFreezeService>(
                new FreezeService(u, Options.Create(securityOptions), NullLogger<FreezeService>.Instance));
            services.AddSingleton<IFreezeStateProvider>(new FreezeStateProvider(u));
            services.AddSingleton<IAuthAccountService>(new AuthAccountService(
                u, Options.Create(new DomainOptions()), Options.Create(options), new FakeCredentialProtector()));
        });

        return (user, store);
    }

    private static async Task SeedAccountAsync(StubDomainUser user, string uid)
        => await user.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = uid });

    [Fact]
    public async Task Freeze_MultipleReasons_PartialRelease_KeepsFrozenUntilLastReasonReleased()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-frz-multi");
        var freezes = user.Use<IFreezeService>();

        await freezes.FreezeAsync("u-frz-multi", "spam", null, null);
        await freezes.FreezeAsync("u-frz-multi", "chargeback", null, null);

        var both = await freezes.ListActiveFreezesAsync("u-frz-multi");
        Assert.Equal(2, both.Count);

        await freezes.ReleaseFreezeAsync("u-frz-multi", "spam", "admin-op");
        Assert.True(await freezes.IsFrozenAsync("u-frz-multi"));
        var remaining = await freezes.ListActiveFreezesAsync("u-frz-multi");
        Assert.Single(remaining);
        Assert.Equal("chargeback", remaining[0].Reason);

        await freezes.ReleaseFreezeAsync("u-frz-multi", "chargeback", "admin-op");
        Assert.False(await freezes.IsFrozenAsync("u-frz-multi"));
    }

    [Fact]
    public async Task Freeze_SameReason_IsIdempotent()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-frz-idem");
        var freezes = user.Use<IFreezeService>();

        await freezes.FreezeAsync("u-frz-idem", "manual", null, null);
        await freezes.FreezeAsync("u-frz-idem", "manual", null, null);

        var active = await freezes.ListActiveFreezesAsync("u-frz-idem");
        Assert.Single(active);
    }

    [Fact]
    public async Task Freeze_BlankReason_Rejected()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-frz-blank");

        await Assert.ThrowsAsync<ArgumentException>(
            () => user.Use<IFreezeService>().FreezeAsync("u-frz-blank", "  ", null, null));
    }

    [Fact]
    public async Task Freeze_MissingAccount_RejectedWithAccountNotFound()
    {
        var (user, _) = CreateHost();

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => user.Use<IFreezeService>().FreezeAsync("u-frz-missing", "manual", null, null));

        Assert.Equal("ACCOUNT_NOT_FOUND", ex.Message);
    }

    [Fact]
    public async Task FreezeStateProvider_SnapshotInvalidatedByFreezeAndReleaseWrites()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-frz-snap");
        var provider = user.Use<IFreezeStateProvider>();
        var freezes = user.Use<IFreezeService>();

        Assert.False(await provider.IsFrozenAsync("u-frz-snap"));

        await freezes.FreezeAsync("u-frz-snap", "manual", null, null);
        Assert.True(await provider.IsFrozenAsync("u-frz-snap"));

        await freezes.ReleaseAllFreezesAsync("u-frz-snap", "admin-op");
        Assert.False(await provider.IsFrozenAsync("u-frz-snap"));
    }

    [Fact]
    public async Task Bind_SameIdentifier_IsIdempotent_SingleRowSingleEvent()
    {
        var (user, store) = CreateHost();
        await SeedAccountAsync(user, "u-bind-idem");
        var factors = user.Use<IAuthFactorService>();

        var first = await factors.BindAsync("u-bind-idem", AuthFactorTypes.Sms, "13800001001");
        var second = await factors.BindAsync("u-bind-idem", AuthFactorTypes.Sms, "13800001001");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await factors.ListByUIdAsync("u-bind-idem"));

        var events = await user.Use<AuthFactorEventEntityDataService>()
            .EntitySelectAsync(e => e.UId == "u-bind-idem", ct: CancellationToken.None);
        Assert.Single(events);
        Assert.Equal((int)AuthFactorEventTypes.Bind, events[0].EventType);
        Assert.Single(store.Entries, e => e.EventType == SecurityLogEventTypes.Bind);
    }

    [Fact]
    public async Task Bind_TwoFactorTypes_WritesAuditCopyPerBind()
    {
        var (user, store) = CreateHost();
        await SeedAccountAsync(user, "u-bind-two");
        var factors = user.Use<IAuthFactorService>();

        await factors.BindAsync("u-bind-two", AuthFactorTypes.Sms, "13800001002");
        await factors.BindAsync("u-bind-two", AuthFactorTypes.Email, "User@Example.COM");

        var all = await factors.ListByUIdAsync("u-bind-two");
        Assert.Equal(2, all.Count);
        Assert.Equal("user@example.com", all.Single(f => f.FactorType == (int)AuthFactorTypes.Email).Identifier);
        Assert.Equal(2, store.Entries.Count(e => e.EventType == SecurityLogEventTypes.Bind));
    }

    [Fact]
    public async Task Bind_EventTypeFilterExcludingBind_WritesDomainEventButNoAuditCopy()
    {
        var securityLog = new SecurityLoggingOptions
        {
            EventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SecurityLogEventTypes.Login },
        };
        var (user, store) = CreateHost(securityLog);
        await SeedAccountAsync(user, "u-bind-gated");

        await user.Use<IAuthFactorService>().BindAsync("u-bind-gated", AuthFactorTypes.Sms, "13800001003");

        var events = await user.Use<AuthFactorEventEntityDataService>()
            .EntitySelectAsync(e => e.UId == "u-bind-gated", ct: CancellationToken.None);
        Assert.Single(events);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task Unbind_DeletesFactor_IncrementsTokenVersion_WritesEventAndAuditCopy()
    {
        var (user, store) = CreateHost();
        await SeedAccountAsync(user, "u-unbind-1");
        var factors = user.Use<IAuthFactorService>();

        var sms = await factors.BindAsync("u-unbind-1", AuthFactorTypes.Sms, "13800001004");
        await factors.BindAsync("u-unbind-1", AuthFactorTypes.Email, "keep@example.com");
        var before = (await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-unbind-1"))!.TokenVersion;

        await factors.UnbindAsync("u-unbind-1", sms.Id, "admin-op");

        var remaining = await factors.ListByUIdAsync("u-unbind-1");
        Assert.Single(remaining);
        Assert.Equal((int)AuthFactorTypes.Email, remaining[0].FactorType);

        var after = (await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-unbind-1"))!.TokenVersion;
        Assert.Equal(before + 1, after);

        var events = await user.Use<AuthFactorEventEntityDataService>()
            .EntitySelectAsync(e => e.UId == "u-unbind-1", ct: CancellationToken.None);
        Assert.Contains(events, e => e.EventType == (int)AuthFactorEventTypes.Unbind && e.Identifier == "13800001004");
        var audit = Assert.Single(store.Entries, e => e.EventType == SecurityLogEventTypes.Unbind);
        Assert.Equal("u-unbind-1", audit.UserName);
        Assert.Contains("admin-op", audit.Detail);
    }

    [Fact]
    public async Task Unbind_LastFactor_Rejected()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-unbind-last");
        var factors = user.Use<IAuthFactorService>();
        var only = await factors.BindAsync("u-unbind-last", AuthFactorTypes.Sms, "13800001005");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => factors.UnbindAsync("u-unbind-last", only.Id));

        Assert.Equal(IAuthFactorService.LAST_FACTOR_CANNOT_UNBIND, ex.Message);
        Assert.Single(await factors.ListByUIdAsync("u-unbind-last"));
    }

    [Fact]
    public async Task Bind_IdentifierTooLong_Rejected()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-bind-long");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => user.Use<IAuthFactorService>().BindAsync(
                "u-bind-long", AuthFactorTypes.Sms, new string('1', 257)));

        Assert.Equal(IAuthFactorService.IDENTIFIER_TOO_LONG, ex.Message);
    }

    [Fact]
    public async Task VerifyAndReset_WriteTheirDomainEvents()
    {
        var (user, _) = CreateHost();
        await SeedAccountAsync(user, "u-factor-life");
        var factors = user.Use<IAuthFactorService>();
        var bound = await factors.BindAsync("u-factor-life", AuthFactorTypes.Email, "life@example.com");

        await factors.VerifyAsync("u-factor-life", bound.Id);
        await factors.ResetAsync("u-factor-life", bound.Id, "new-secret", mustChange: true);

        var events = await user.Use<AuthFactorEventEntityDataService>()
            .EntitySelectAsync(e => e.UId == "u-factor-life", ct: CancellationToken.None);
        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.EventType == (int)AuthFactorEventTypes.Bind);
        Assert.Contains(events, e => e.EventType == (int)AuthFactorEventTypes.Verify);
        Assert.Contains(events, e => e.EventType == (int)AuthFactorEventTypes.Reset);
    }

    private sealed class CapturingSecurityLogStore : ISecurityLogStore
    {
        public List<SecurityLogEntry> Entries { get; } = [];

        public Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
