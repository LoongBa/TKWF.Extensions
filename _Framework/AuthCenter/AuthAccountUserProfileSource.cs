using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.AuthCenter;

/// <summary>公共档案源实现——认证中心作为 AuthAccount 属主实现 UserCenter 契约（终态路径）。
/// ⚠️ Phone 返回原始值——UserCenter 门面强制脱敏（实现方不得自行 Mask）。
/// ⚠️ 命名：区别于 UserCenter 文档示例的装配层桥接类（同名 AuthAccountProfileSource——
/// 新实现类命名 AuthAccountUserProfileSource 消除碰撞）。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）：**接线型**（skill §4.2）——<see cref="IUserProfileSource"/>
/// 契约属 UserCenter.Abstractions（非 IDomainService，不可修改），被 <c>UserCenterQueryService</c> 经普通 DI
/// （<c>GetService&lt;IUserProfileSource&gt;()</c>）解析——<c>AddConstructibleService</c> 编译约束
/// （where TInterface : IDomainService）不满足，注册保持 <c>TryAddScoped</c> 普通 DI。旧 ctor(IDomainUser)
/// 在 UserCenter 门面普通 DI 解析时 IDomainUser 无可解析（永不注册 DI——D01）生产必失败（真实故障）；
/// 改 ctor(<see cref="IServiceProvider"/>)——<see cref="IAuthAccountQueryService"/> 经 C1 延迟解析
/// （<c>GetRequiredService</c>，消费链内已注册；接线型边界不吞守卫语义）。</para></summary>
public sealed class AuthAccountUserProfileSource : IUserProfileSource
{
    private readonly IServiceProvider _serviceProvider;
    private IAuthAccountQueryService? _accounts;
    private IAuthAccountQueryService Accounts => _accounts ??= _serviceProvider.GetRequiredService<IAuthAccountQueryService>();

    public AuthAccountUserProfileSource(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        // ⚠️ 以账号 UId 为准，不信任传入 userId（数据源为准防误读）
        var account = await Accounts.GetByUIdAsync(userId, ct);
        if (account is null) return null;
        return new UserProfileDto(
            UserId: account.UId,               // 数据源为准（account.UId）——非透传 userId
            Phone: account.Phone,              // 原始值——门面脱敏
            IsWechatBound: !string.IsNullOrEmpty(account.WechatMpOpenId)
                || !string.IsNullOrEmpty(account.WechatWebOpenId)
                || !string.IsNullOrEmpty(account.UnionId),
            Nickname: account.Nickname,
            AvatarUrl: account.Avatar,
            IsTeacherVerified: account.TeacherVerified,
            AuthLevel: account.AuthLevel);
    }
}
