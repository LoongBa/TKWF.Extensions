using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>AuthCenter 侧渠道类型常量（T5 2026-10-09——原 <c>WeChatAuthenticationProvider.ChannelWechatMp/Web</c> 内联：
/// 微信双源 Provider 已删，常量迁本类保 <see cref="IAuthAccountQueryService.IsWechatBoundAsync"/> 语义）。</summary>
internal static class AuthCenterChannelIds
{
    /// <summary>公众号网页授权 channelId（snsapi_base）。</summary>
    public const string WechatMp = "wechat_mp";

    /// <summary>开放平台扫码 channelId（snsapi_login）。</summary>
    public const string WechatWeb = "wechat_web";
}

/// <summary>账号查询服务——对外只读查询契约（装配实例 / 内部复用）。</summary>
public interface IAuthAccountQueryService : IDomainService
{
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);
    Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default);

    /// <summary>按平台内部 id 查档案（V0.9.0 A.1 凭据/档案分离——档案 1:1 经 UserProfile 表；档案查询消费面）。
    /// <para>2026-10-08（UserCenter 退役）：档案读统一经本方法——替代已删除的 UserCenter 契约实现。</para></summary>
    Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default);

    /// <summary>微信绑定判定（V0.9.0 A.8 联邦归一化——wechat_mp/wechat_web channel 行存在性，替代原 AuthAccount 三列 OR 推导）。</summary>
    Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default);
}

/// <summary>账号查询服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 IdentityPasswordManager 先例一致（public sealed + TryAddScoped 为注册方）；
/// 本类仅门面委托无外部扩展需求，internal 即可（AuthAccountEntityDataService 亦 internal，注入合法）。
/// V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册改
/// <c>AddConstructibleService&lt;IAuthAccountQueryService, AuthAccountQueryService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// V0.6.0（SSO 立项，ADR-SSO Oracle P1-2）：补实现 <see cref="ISsoAccountQueryService"/>——SSO 消费面契约
/// （返回不可变 DTO <see cref="SsoAccountDto"/>，敏感字段不出契约包）。
/// V0.11.0（方案 §4.10 T4）：<c>ISsoAccountLinkService</c>（联盟锚点写契约）随锚点列退役删除。</para></summary>
[DiContractIgnore]
internal sealed class AuthAccountQueryService : DomainServiceBase, IAuthAccountQueryService, ISsoAccountQueryService
{
    private AuthAccountEntityDataService? _dataService;
    private AuthAccountEntityDataService DataService => _dataService ??= User.Use<AuthAccountEntityDataService>();
    private UserProfileEntityDataService? _profileDataService;
    private UserProfileEntityDataService ProfileDataService => _profileDataService ??= User.Use<UserProfileEntityDataService>();
    private IAuthFactorService? _factors;
    private IAuthFactorService Factors => _factors ??= User.Use<IAuthFactorService>();

    public AuthAccountQueryService(IDomainUser user) : base(user)
    {
    }

    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => DataService.GetByUIdAsync(uid, ct);
    /// <inheritdoc cref="IAuthAccountQueryService.GetByPhoneAsync"/>
    /// <remarks>V0.11.0（方案 §6.5/T7）：手机号不落账号列——经 <see cref="IAuthFactorService.GetByPhoneAsync"/> 反查 UId 再取账号。</remarks>
    public async Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
    {
        var factor = await Factors.GetByPhoneAsync(phone, ct);
        return factor is null ? null : await DataService.GetByUIdAsync(factor.UId, ct);
    }

    /// <summary>按平台内部 id 查档案（1:1 True——UserProfile 属主；档案查询消费面）。</summary>
    public async Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default)
        => await ProfileDataService.GetByUIdAsync(uid, ct);

    /// <inheritdoc cref="IAuthAccountQueryService.IsWechatBoundAsync"/>
    /// <remarks>V0.11.0（方案 §6.5/T7）：绑定判定改经 AuthFactor（Federated 因素 Identifier 前缀
    /// <c>wechat_mp|</c>/<c>wechat_web|</c>）——因素是联邦身份唯一权威，PlatformAccountMap 通道列随 T1 退役。</remarks>
    public async Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default)
        => await Factors.IsBoundToChannelAsync(uid, AuthCenterChannelIds.WechatMp, ct)
            || await Factors.IsBoundToChannelAsync(uid, AuthCenterChannelIds.WechatWeb, ct);

    /// <inheritdoc cref="ISsoAccountQueryService.GetByUIdAsync"/>
    /// <remarks>显式接口实现——与 <see cref="IAuthAccountQueryService.GetByUIdAsync"/> 同名同参不同返回类型（实体 vs DTO），
    /// C# 隐式实现签名冲突，须显式实现区分。</remarks>
    async Task<SsoAccountDto?> ISsoAccountQueryService.GetByUIdAsync(string uid, CancellationToken ct)
    {
        var entity = await DataService.GetByUIdAsync(uid, ct);
        if (entity is null) return null;
        var profile = await ProfileDataService.GetByUIdAsync(uid, ct);   // 档案（Nickname/Avatar 分离表）
        return ToDto(entity, profile, await Factors.GetAuthLevelAsync(entity.UId, ct));
    }

    /// <summary>实体 + 档案 → SSO 消费面 DTO（Nickname/Avatar 从 UserProfile 1:1 读——V0.9.0 A.1 凭据/档案分离；
    /// 敏感字段不外泄。V0.11.0 T9：AuthLevel 由调用方经 AuthFactor 派生后传入；T7：联盟锚点字段随列退役）。</summary>
    private static SsoAccountDto ToDto(AuthAccountEntity entity, UserProfileEntity? profile, int authLevel)
        => new SsoAccountDto(
            UId: entity.UId,
            Nickname: profile?.Nickname,
            AvatarUrl: profile?.Avatar,
            AuthLevel: authLevel);
}