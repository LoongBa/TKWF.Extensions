using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N2/N3：AuthAccountUserProfileSource——映射正确性（全字段 + Phone 原始值）与微信绑定推导矩阵（不碰 DB，手写假查询服务）。
/// <para>V4.10.53（领域自治根治后重写）：接线型（skill §4.2）——ctor(<see cref="IServiceProvider"/>)，
/// <see cref="IAuthAccountQueryService"/> 经 C1 延迟解析（GetRequiredService）；测试直构
/// <c>new AuthAccountUserProfileSource(sp)</c>（sp 注册 FakeAuthAccountQueryService）。业务断言语义不变。</para></summary>
public class AuthAccountUserProfileSourceTests
{
    /// <summary>N2：全字段账号 → UserProfileDto 映射正确；UserId 以数据源为准（非透传 userId）；Phone 返回原始值（门面负责脱敏）。</summary>
    [Fact]
    public async Task Mapping_FullFields_ReturnsMappedProfile()
    {
        var account = new AuthAccountEntity
        {
            UId = "u-1001",
            Phone = "13812345678",
            Nickname = "测试用户",
            Avatar = "https://cdn.example.com/a.png",
            TeacherVerified = true,
            AuthLevel = 3,
            WechatMpOpenId = "mp-1",
        };
        var source = CreateSource(account);

        var profile = await source.GetProfileAsync("u-1001");

        Assert.NotNull(profile);
        Assert.Equal("u-1001", profile!.UserId);          // 数据源为准（account.UId）——非透传 userId
        Assert.Equal("13812345678", profile.Phone);       // 原始值——未脱敏（实现方不得自行 Mask，门面统一处理）
        Assert.True(profile.IsWechatBound);
        Assert.Equal("测试用户", profile.Nickname);
        Assert.Equal("https://cdn.example.com/a.png", profile.AvatarUrl);
        Assert.True(profile.IsTeacherVerified);
        Assert.Equal(3, profile.AuthLevel);
    }

    /// <summary>N2b：账号不存在 → 返回 null（档案缺失降级）。</summary>
    [Fact]
    public async Task AccountNotFound_ReturnsNull()
    {
        var source = CreateSource(null);
        Assert.Null(await source.GetProfileAsync("u-unknown"));
    }

    /// <summary>N3：微信绑定推导矩阵——MpOpenId / WebOpenId / UnionId 任一非空 = 已绑定；三字段全空（仅手机号账号）= 未绑定。</summary>
    [Theory]
    [InlineData("mp-1", null, null, true)]    // 仅 WechatMpOpenId（公众号网页授权 snsapi_base）→ 已绑定
    [InlineData(null, "web-1", null, true)]   // 仅 WechatWebOpenId（开放平台扫码 snsapi_login）→ 已绑定
    [InlineData(null, null, "union-1", true)] // 仅 UnionId（装配层可选补充）→ 已绑定
    [InlineData(null, null, null, false)]     // 三字段全空（仅手机号账号）→ 未绑定
    public async Task Mapping_IsWechatBound_Matrix(string? mp, string? web, string? union, bool expected)
    {
        var account = new AuthAccountEntity
        {
            UId = "u-1001",
            Phone = "13812345678",
            WechatMpOpenId = mp,
            WechatWebOpenId = web,
            UnionId = union,
        };
        var source = CreateSource(account);

        var profile = await source.GetProfileAsync("u-1001");

        Assert.NotNull(profile);
        Assert.Equal(expected, profile!.IsWechatBound);
    }

    /// <summary>接线型直构：ctor(IServiceProvider)——C1 延迟解析 IAuthAccountQueryService（普通 DI 注册 Fake 实现）。</summary>
    private static AuthAccountUserProfileSource CreateSource(AuthAccountEntity? account)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthAccountQueryService>(new FakeAuthAccountQueryService(account));
        return new AuthAccountUserProfileSource(services.BuildServiceProvider());
    }

    /// <summary>手写假实现——每次查询返回固定账号（N2/N3 仅验证映射逻辑，不碰 DB）。</summary>
    private sealed class FakeAuthAccountQueryService : IAuthAccountQueryService
    {
        private readonly AuthAccountEntity? _account;

        public FakeAuthAccountQueryService(AuthAccountEntity? account) => _account = account;

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_account);
        public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
            => Task.FromResult(_account);
        public Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
            => Task.FromResult(_account);
        public Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
            => Task.FromResult(_account);
    }
}
