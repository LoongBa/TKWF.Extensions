using System;
using System.Threading.Tasks;
using TKWF.Ext.AuditLogging;
using Xunit;

namespace TKWF.Ext.SecurityLog.Tests;

public class SecurityFacetReaderTests
{
    [Fact]
    public async Task QueryAsync_ReturnsSeededEvent_WithPiiTrimmed()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var user = SecurityLogTestHost.CreateStub(fsql);

        var dataService = user.Use<SecurityLogEntityDataService>();
        await dataService.EntityCreateAsync(new SecurityLogEntity
        {
            EventType = "Login",
            EventCategory = "Authentication",
            UserName = "alice",
            Result = "Success",
            Detail = "token=secret",
            CorrelationId = "c-1",
            CreateTime = DateTime.UtcNow,
        });

        var reader = new SecurityFacetReader(user);
        var page = await reader.QueryAsync(new AuditFacetQuery());

        Assert.Equal(1, page.Total);
        var view = Assert.IsType<SecurityAuditView>(Assert.Single(page.Items));
        Assert.Equal("Login", view.EventType);
        Assert.Equal("Success", view.Result);
        Assert.Null(view.Summary);
    }
}
