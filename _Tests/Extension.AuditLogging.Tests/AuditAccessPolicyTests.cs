using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

public class AuditAccessPolicyTests
{
    private static DefaultAuditAccessPolicy CreatePolicy(bool allowWithoutPermissions)
        => new(new StubDomainUser(), Options.Create(new AuditAccessPolicyOptions { AllowWithoutPermissions = allowWithoutPermissions }));

    [Fact]
    public async Task QueryOwn_Authenticated_Allowed()
    {
        var decision = await CreatePolicy(false)
            .EvaluateAsync(new AuditAccessRequest(AuditOperation.QueryOwn, IsAuthenticated: true), CancellationToken.None);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task QueryOwn_Anonymous_Denied()
    {
        var decision = await CreatePolicy(true)
            .EvaluateAsync(new AuditAccessRequest(AuditOperation.QueryOwn, IsAuthenticated: false), CancellationToken.None);

        Assert.False(decision.Allowed);
    }

    [Theory]
    [InlineData(AuditOperation.QueryAll)]
    [InlineData(AuditOperation.Detail)]
    [InlineData(AuditOperation.Export)]
    [InlineData(AuditOperation.Delete)]
    [InlineData(AuditOperation.Purge)]
    public async Task Sensitive_DefaultDenied_FailClosed(AuditOperation operation)
    {
        var decision = await CreatePolicy(false)
            .EvaluateAsync(new AuditAccessRequest(operation), CancellationToken.None);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Sensitive_AllowWithoutPermissions_Allowed()
    {
        var decision = await CreatePolicy(true)
            .EvaluateAsync(new AuditAccessRequest(AuditOperation.Export, IsAuthenticated: true), CancellationToken.None);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task NullRequest_Denied()
    {
        var decision = await CreatePolicy(true).EvaluateAsync(null!, CancellationToken.None);

        Assert.False(decision.Allowed);
    }
}
