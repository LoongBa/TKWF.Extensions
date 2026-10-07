using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N2/N3：AuthAccountUserProfileSource——映射正确性（全字段 + Phone 原始值）与微信绑定判定（不碰 DB，手写假查询服务）。
/// <para>V0.9.0 语义对齐（ADR-AuthCenter-身份域数据模型与密码能力边界）：凭据/档案分离（Nickname/Avatar 读 UserProfile 1:1）；
/// TeacherVerified 迁出（档案面恒 false）；AuthLevel 泛化（1=手机号 / 2=联邦快捷）；IsWechatBound 改查 PlatformAccountMap 通道行
/// （经门面 IsWechatBoundAsync）。</para>
/// <para>V4.10.53（领域自治根治后重写）：接线型（skill §4.2）——ctor(<see cref="IServiceProvider"/>)，
/// <see cref="IAuthAccountQueryService"/> 经 C1 延迟解析（GetRequiredService）；测试直构
/// <c>new AuthAccountUserProfileSource(sp)</c>（sp 注册 FakeAuthAccountQueryService）。业务断言语义不变。</para></summary>
public class AuthAccountUserProfileSourceTests
{
    /// <summary>N2：全字段账号 + 档案 → UserProfileDto 映射正确；UserId 以数据源为准（非透传 userId）；Phone 返回原始值（门面负责脱敏）。</summary>
    [Fact]
    public async Task Mapping_FullFields_ReturnsMappedProfile()
    {
        var account = new AuthAccountEntity
        {
            UId = "u-1001",
            Phone = "13812345678",
            AuthLevel = (int)AuthLevel.Federated,
        };
        var profile = new UserProfileEntity
        {
            UId = account.UId,
            Nickname = "测试用户",
            Avatar = "https://cdn.example.com/a.png",
            Email = "test@example.com",
        };
        var source = CreateSource(account, profile, wechatBound: true);

        var dto = await source.GetProfileAsync("u-1001");

        Assert.NotNull(dto);
        Assert.Equal("u-1001", dto!.UserId);          // 数据源为准（account.UId）——非透传 userId
        Assert.Equal("13812345678", dto.Phone);       // 原始值——未脱敏（实现方不得自行 Mask，门面统一处理）
        Assert.True(dto.IsWechatBound);               // 经门面 IsWechatBoundAsync（PlatformAccountMap 通道行）
        Assert.Equal("测试用户", dto.Nickname);        // UserProfile 1:1 档案
        Assert.Equal("https://cdn.example.com/a.png", dto.AvatarUrl);
        Assert.False(dto.IsTeacherVerified);          // V0.9.0 A.4：TeacherVerified 迁出——档案面恒 false（教育线业务扩展自建）
        Assert.Equal((int)AuthLevel.Federated, dto.AuthLevel);   // AuthLevel 泛化（2=联邦快捷）
    }

    /// <summary>N2b：账号不存在 → 返回 null（档案缺失降级）。</summary>
    [Fact]
    public async Task AccountNotFound_ReturnsNull()
    {
        var source = CreateSource(null, null, wechatBound: false);
        Assert.Null(await source.GetProfileAsync("u-unknown"));
    }

    /// <summary>N3：微信绑定判定——经门面 IsWechatBoundAsync（PlatformAccountMap 通道行存在性，替代原三列 OR 推导——A.8 联邦归一化）。</summary>
    [Theory]
    [InlineData(true, true)]     // 存在 wechat 通道行 → 已绑定
    [InlineData(false, false)]   // 无通道行（仅手机号账号）→ 未绑定
    public async Task Mapping_IsWechatBound_Matrix(bool wechatBound, bool expected)
    {
        var account = new AuthAccountEntity { UId = "u-1001", Phone = "13812345678" };
        var source = CreateSource(account, null, wechatBound);

        var dto = await source.GetProfileAsync("u-1001");

        Assert.NotNull(dto);
        Assert.Equal(expected, dto!.IsWechatBound);
    }

    /// <summary>接线型直构：ctor(IServiceProvider)——C1 延迟解析 IAuthAccountQueryService（普通 DI 注册 Fake 实现）。</summary>
    private static AuthAccountUserProfileSource CreateSource(AuthAccountEntity? account, UserProfileEntity? profile, bool wechatBound)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthAccountQueryService>(new FakeAuthAccountQueryService(account, profile, wechatBound));
        return new AuthAccountUserProfileSource(services.BuildServiceProvider());
    }

    /// <summary>手写假实现——每次查询返回固定账号/档案/绑定判定（N2/N3 仅验证映射逻辑，不碰 DB）。</summary>
    private sealed class FakeAuthAccountQueryService : IAuthAccountQueryService
    {
        private readonly AuthAccountEntity? _account;
        private readonly UserProfileEntity? _profile;
        private readonly bool _wechatBound;

        public FakeAuthAccountQueryService(AuthAccountEntity? account, UserProfileEntity? profile, bool wechatBound)
        {
            _account = account;
            _profile = profile;
            _wechatBound = wechatBound;
        }

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_account);
        public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
            => Task.FromResult(_account);

        // V0.9.0（T3）：档案经门面读 UserProfile（凭据/档案分离——Nickname/Avatar 从档案表取）
        public Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_profile);

        // V0.9.0（A.8）：微信绑定判定 = PlatformAccountMap 通道行存在性（替代原 AuthAccount 三列 OR 推导）
        public Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_wechatBound);
    }
}