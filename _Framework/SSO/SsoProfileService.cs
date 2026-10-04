using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;

namespace TKWF.Ext.SSO;

/// <summary>
/// SSO profile API 服务实现（server-to-server——scope 强制 + 审计，开发方案 §5.5/§5.7）。
/// <para>scope 裁剪：只允许 <c>profile:basic</c>（nickname/avatar/AuthLevel）字段；未含该 scope 的
/// 组合按最小化返回（仅 UId，其余字段剥离）。<b>永不返回</b> openid/channel_id/phone——经
/// <see cref="ISsoAccountQueryService"/> 读 <see cref="SsoAccountDto"/>（SSO 消费字段子集，
/// 敏感字段不出契约包，ADR-SSO Oracle P1-2）；映射仅取 UId/Nickname/AvatarUrl/AuthLevel，不引入其余字段。</para>
/// <para>审计：本迭代只做 <see cref="ILogger"/> 结构化日志审计（app_id/uid/scopes/ip/ts）——开发方案 §5.5
/// 的 <c>SsoProfileAuditEntity</c> 推迟（可选优化 2：复用 SecurityLog 另议）；对齐 SecurityLog 只增语义的后续迭代。</para>
/// <para>V4.10.53（领域自治根治，ADR90 正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
/// 获取用户上下文（IDomainUser 永不注册 DI）；<see cref="ISsoAccountQueryService"/> 为 <c>IDomainService</c>
/// 门面（<c>AddConstructibleService</c> 注册）——须经 <c>User.Use&lt;ISsoAccountQueryService&gt;()</c> AOP 帧内解析，
/// 禁止 ctor 直注（帧外解析抛守卫，对齐 tkwf-extension skill §4.6 门面消费其他门面）。
/// 注册由 SSO Initializer 经 <c>AddConstructibleService&lt;ISsoProfileService, SsoProfileService&gt;</c> 完成。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SsoProfileService : DomainServiceBase, ISsoProfileService
{
    private const string ProfileBasicScope = "profile:basic";

    private ISsoAccountQueryService? _accounts;
    private readonly ILogger<SsoProfileService> _logger;

    private ISsoAccountQueryService Accounts => _accounts ??= User.Use<ISsoAccountQueryService>();

    public SsoProfileService(IDomainUser user, ILogger<SsoProfileService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SsoProfileDto?> GetProfileAsync(
        string appId, string uid, IReadOnlyList<string> scopes, string? ipAddress, CancellationToken ct = default)
    {
        // 审计（结构日志——本迭代不建审计表；SsoProfileAuditEntity 推迟）。每次调用记录，含结果无关的请求上下文。
        var audit = new SsoProfileAuditEntry(appId, uid, scopes is null ? "" : string.Join(",", scopes), ipAddress);
        _logger.LogInformation(
            "SSO profile API 访问审计——AppId={AppId} UId={UId} Scopes={Scopes} IpAddress={IpAddress} TimestampUtc={TimestampUtc}",
            audit.AppId, audit.UId, audit.Scopes, audit.IpAddress, DateTime.UtcNow);

        var account = await Accounts.GetByUIdAsync(uid, ct);
        if (account is null) return null;

        // scope 裁剪：profile:basic 授权 nickname/avatar/AuthLevel；未含则最小化返回（仅 UId，字段剥离）。
        // DTO 固定 4 字段，永不引入 openid/channel_id/phone。
        var hasProfileBasic = scopes is not null && scopes.Contains(ProfileBasicScope, StringComparer.Ordinal);
        return new SsoProfileDto(
            UId: account.UId,                // 数据源为准（account.UId）——非透传 uid
            Nickname: hasProfileBasic ? account.Nickname : null,
            AvatarUrl: hasProfileBasic ? account.AvatarUrl : null,
            AuthLevel: hasProfileBasic ? account.AuthLevel : 0);
    }
}
