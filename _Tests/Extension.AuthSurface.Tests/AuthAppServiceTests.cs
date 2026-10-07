using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// 应用目录管理门面测试——"我的应用" AppName 来源（运营数据维护）。
/// </summary>
public class AuthAppServiceTests
{
    private static IFreeSql CreateInMemoryFreeSql() => AuthSurfaceTestHost.CreateInMemoryFreeSql();

    private static ServiceProvider CreateProvider(IFreeSql fsql)
        => AuthSurfaceTestHost.CreateProvider(fsql);

    private static DomainUser<TestUserInfo> BindUser(ServiceProvider sp)
    {
        DomainUser<TestUserInfo>.BindScope(sp);
        return new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("u-admin", "运营") };
    }

    [Fact]
    public async Task CreateApp_Success_ReturnsDto()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var dto = await user.Use<IAuthAppService>().CreateAppAsync("edu-course", "教育课程", "https://cdn.example.com/icon.png", CancellationToken.None);

        Assert.Equal("edu-course", dto.AppId);
        Assert.Equal("教育课程", dto.AppName);
        Assert.Equal("https://cdn.example.com/icon.png", dto.Icon);
        Assert.True(dto.IsEnabled);
    }

    [Fact]
    public async Task CreateApp_DuplicateAppId_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var svc = user.Use<IAuthAppService>();

        await svc.CreateAppAsync("edu-course", "教育课程", null, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAppAsync("edu-course", "重复", null, CancellationToken.None));
        Assert.Contains("AUTHAPP_ALREADY_EXISTS", ex.Message);
    }

    [Fact]
    public async Task UpdateApp_UpdatesNameAndIcon()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var svc = user.Use<IAuthAppService>();

        await svc.CreateAppAsync("edu-course", "旧名", null, CancellationToken.None);
        var dto = await svc.UpdateAppAsync("edu-course", "新名", "https://cdn.example.com/new.png", CancellationToken.None);

        Assert.Equal("新名", dto.AppName);
        Assert.Equal("https://cdn.example.com/new.png", dto.Icon);
    }

    [Fact]
    public async Task SetEnabled_FalseDisables()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var svc = user.Use<IAuthAppService>();

        await svc.CreateAppAsync("edu-course", "教育课程", null, CancellationToken.None);
        await svc.SetEnabledAsync("edu-course", false, CancellationToken.None);

        var row = fsql.Select<AuthAppEntity>().Where(e => e.AppId == "edu-course").ToOne();
        Assert.False(row.IsEnabled);
    }

    [Fact]
    public async Task UpdateApp_NotFound_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            user.Use<IAuthAppService>().UpdateAppAsync("no-such-app", "名", null, CancellationToken.None));
        Assert.Contains("AUTHAPP_NOT_FOUND", ex.Message);
    }
}
