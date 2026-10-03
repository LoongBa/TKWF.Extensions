using System;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.UserCenter;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>公共档案源实现——认证中心作为 AuthAccount 属主实现 UserCenter 契约（终态路径）。
/// ⚠️ Phone 返回原始值——UserCenter 门面强制脱敏（实现方不得自行 Mask）。
/// ⚠️ 命名：区别于 UserCenter 文档示例的装配层桥接类（同名 AuthAccountProfileSource——
/// 新实现类命名 AuthAccountUserProfileSource 消除碰撞）。</summary>
public sealed class AuthAccountUserProfileSource : IUserProfileSource
{
    private IAuthAccountQueryService? _accounts;
    private readonly IDomainUser _user;
    private IAuthAccountQueryService Accounts => _accounts ??= _user.Use<IAuthAccountQueryService>();

    public AuthAccountUserProfileSource(IDomainUser user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
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