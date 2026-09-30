using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Identity;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// Identity V0.4.0 测试——UserRoleViewQueryService REST 直接暴露（方案 03）：
/// VEntity DTO 一等公民（含 DisplayName/IsSystemRole JOIN 字段）、仅本人防护（C5/N5）、
/// 门面链路回归（N3，GetUserRolesAsync 仍返回 RoleEntity）。
/// </summary>
public class UserRoleViewQueryServiceTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        IdentityTestHost.SyncSchema(fsql);
        return fsql;
    }

    // ── N1：REST 直接暴露——VEntity DTO 完整字段 + 仅本人 ──

    [Fact]
    public async Task GetMyRoles_ReturnsViewDto_WithFullFields_OnlySelf()
    {
        using var fsql = CreateFreeSql();
        var manager = IdentityTestHost.CreateUserManager(fsql);
        var user = await manager.CreateUserAsync("alice", "pass12345", "Alice", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        // 构造可解析（N4——依赖经真实 DAC 驱动，对齐手动构造先例）+ 以本人身份调用
        var svc = IdentityTestHost.CreateRoleViewQueryService(fsql, new AuthenticatedStubUser(user!.Id.ToString()));
        var roles = await svc.GetMyRolesAsync(CancellationToken.None);

        var dto = Assert.Single(roles);
        Assert.Equal("Admin", dto.Name);
        Assert.Equal("管理员", dto.DisplayName);      // 视图 JOIN 字段不再丢弃
        Assert.True(dto.IsSystemRole);
        Assert.Equal(user!.Id, dto.UserId);           // 仅本人
    }

    [Fact]
    public async Task GetMyRoles_OtherUser_Roles_NotLeaked()
    {
        using var fsql = CreateFreeSql();
        var manager = IdentityTestHost.CreateUserManager(fsql);
        var alice = await manager.CreateUserAsync("alice", "pass12345", "Alice", CancellationToken.None);
        var bob = await manager.CreateUserAsync("bob", "pass12345", "Bob", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(alice!.Id, new[] { role!.Id }, CancellationToken.None);

        // 以 bob 身份调用只返回 bob 的（空）——不泄露 alice 的 Admin 角色（防 IDOR）
        var svc = IdentityTestHost.CreateRoleViewQueryService(fsql, new AuthenticatedStubUser(bob!.Id.ToString()));
        Assert.Empty(await svc.GetMyRolesAsync(CancellationToken.None));
    }

    // ── N5：越权防护——无用户上下文抛 UnauthorizedAccessException ──

    [Fact]
    public async Task GetMyRoles_NoUserContext_ThrowsUnauthorizedAccess()
    {
        using var fsql = CreateFreeSql();
        var manager = IdentityTestHost.CreateUserManager(fsql);
        var user = await manager.CreateUserAsync("carol", "pass12345", "Carol", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        var svc = IdentityTestHost.CreateRoleViewQueryService(fsql);   // StubDomainUser——无 userId
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.GetMyRolesAsync(CancellationToken.None));
    }

    // ── N3：门面链路零改动回归——GetUserRolesAsync 仍返回 RoleEntity ──

    [Fact]
    public async Task Facade_GetUserRoles_StillReturnsRoleEntity()
    {
        using var fsql = CreateFreeSql();
        var manager = IdentityTestHost.CreateUserManager(fsql);
        var user = await manager.CreateUserAsync("dave", "pass12345", "Dave", CancellationToken.None);
        var role = await manager.CreateRoleAsync("Admin", "管理员", true, CancellationToken.None);
        await manager.AssignRolesAsync(user!.Id, new[] { role!.Id }, CancellationToken.None);

        var roles = await manager.GetUserRolesAsync(user.Id, CancellationToken.None);
        var r = Assert.Single(roles);
        Assert.IsType<RoleEntity>(r);                // 门面仍返回实体（非 VEntity DTO）——零破坏
        Assert.Equal("Admin", r.Name);
    }
}
