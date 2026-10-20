using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>认证因素类型（AuthFactorEntity.FactorType 列语义：1=Password / 2=Sms / 3=Email / 4=Federated /
/// 5=Totp(预留) / 6=Passkey(预留)）。</summary>
public enum AuthFactorTypes
{
    /// <summary>密码（一账号一密码，Identifier 恒为 UId）。</summary>
    Password = 1,

    /// <summary>短信（Identifier = E.164）。</summary>
    Sms = 2,

    /// <summary>邮箱（Identifier = 小写 trim）。</summary>
    Email = 3,

    /// <summary>联邦（Identifier = <c>{channelId}|{externalUserId}</c>）。</summary>
    Federated = 4,

    /// <summary>TOTP（预留）。</summary>
    Totp = 5,

    /// <summary>Passkey（预留）。</summary>
    Passkey = 6,
}

/// <summary>因素事件类型（AuthFactorEventEntity.EventType 列语义：1=Bind / 2=Unbind / 3=Verify / 4=Reset）。</summary>
public enum AuthFactorEventTypes
{
    /// <summary>绑定。</summary>
    Bind = 1,

    /// <summary>解绑。</summary>
    Unbind = 2,

    /// <summary>验证通过。</summary>
    Verify = 3,

    /// <summary>重置（密钥/凭据变更）。</summary>
    Reset = 4,
}

/// <summary>因素事件来源（AuthFactorEventEntity.Source 列语义：1=Manual / 2=Business / 3=Policy / 4=Provisioning）。</summary>
public enum AuthFactorSources
{
    /// <summary>手动（默认——控制台/API 显式操作）。</summary>
    Manual = 1,

    /// <summary>业务（业务事件驱动）。</summary>
    Business = 2,

    /// <summary>策略（风控/策略自动触发）。</summary>
    Policy = 3,

    /// <summary>建号/供应（Provisioning 自动绑定）。</summary>
    Provisioning = 4,
}

/// <summary>
/// 认证因素服务（V0.11.0 Iteration 1 Wave 1b 门面层——方案 §4.7 D4）——认证因素（凭据）绑定/解绑/验证/重置/
/// 查询 + 派生值（主手机号 / AuthLevel / 通道绑定）；每次操作追加写 <c>AuthFactorEventEntity</c>（领域事实源，
/// Source 默认 <see cref="AuthFactorSources.Manual"/>，OperatorUId/OperatorName 取自帧内 <c>User</c>）。
/// 解绑 = 物理删除（无 Revoked 态）+ 最后因素守卫 + TokenVersion++（同 UoW，方案 §6.2）。
/// </summary>
public interface IAuthFactorService : IDomainService
{
    /// <summary>绑定因素（upsert 幂等——同 UId 同 Identifier 并发返回既有；唯一冲突跨 UId 抛 <see cref="FACTOR_ALREADY_BOUND"/>）。</summary>
    Task<AuthFactorEntityDto> BindAsync(string uid, AuthFactorTypes factorType, string identifier, string? secretHash = null, bool mustChange = false, AuthFactorSources source = AuthFactorSources.Manual, CancellationToken ct = default);

    /// <summary>解绑因素（物理删除六步：归属校验 → 最后因素守卫 → 写 Unbind 事件 → 删行 → TokenVersion++）。</summary>
    Task UnbindAsync(string uid, long factorId, string? operatorName = null, CancellationToken ct = default);

    /// <summary>验证因素通过（State→Active + VerifiedAt；写 Verify 事件）。</summary>
    Task<AuthFactorEntityDto> VerifyAsync(string uid, long factorId, CancellationToken ct = default);

    /// <summary>重置因素密钥/凭据（SecretHash + SecretUpdatedAt；写 Reset 事件）。</summary>
    Task<AuthFactorEntityDto> ResetAsync(string uid, long factorId, string? newSecretHash, bool? mustChange = null, CancellationToken ct = default);

    /// <summary>按 uid 列出全部因素（FactorType 升序）。</summary>
    Task<IReadOnlyList<AuthFactorEntityDto>> ListByUIdAsync(string uid, CancellationToken ct = default);

    /// <summary>是否存在至少一个 Active 因素（可登录凭据面）。</summary>
    Task<bool> HasAnyActiveAsync(string uid, CancellationToken ct = default);

    /// <summary>主手机号（最早 BoundAt 的 Active Sms 因素 Identifier）；无则 null。</summary>
    Task<string?> GetPrimaryPhoneAsync(string uid, CancellationToken ct = default);

    /// <summary>认证强度（任一 Active Federated → 2，否则 1——账号级推导，V0.12.0 会话级 AAL 前语义）。</summary>
    Task<int> GetAuthLevelAsync(string uid, CancellationToken ct = default);

    /// <summary>是否已绑定指定 channel 前缀的联邦因素（Identifier 以 <c>{channelPrefix}|</c> 开头）。</summary>
    Task<bool> IsBoundToChannelAsync(string uid, string channelPrefix, CancellationToken ct = default);

    /// <summary>按联邦标识反查因素（Federated 因素 Identifier = <c>{channelId}|{externalUserId}</c>）——绑定关系
    /// <b>反查面</b>（<c>IExternalIdentityService</c> 四方法全部委托本服务，故反查须落在同一权威上）；不存在返回 null。</summary>
    Task<AuthFactorEntityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default);

    /// <summary>按手机号反查因素（Sms 因素 Identifier = E.164 规范化后）——<b>账号定位面</b>（方案 §6.5 T7：
    /// 替代 <c>AuthAccount.Phone</c> 列查询，`SmsAuthenticationProvider` 与 `IAuthAccountQueryService.GetByPhoneAsync`
    /// 共用本方法作为唯一入口）；不存在返回 null。</summary>
    Task<AuthFactorEntityDto?> GetByPhoneAsync(string phone, CancellationToken ct = default);

    /// <summary>解绑被拒——最后一个 Active 因素不可解绑（方案 §4.7 守卫）。</summary>
    public const string LAST_FACTOR_CANNOT_UNBIND = "LAST_FACTOR_CANNOT_UNBIND";

    /// <summary>绑定冲突——同 (FactorType, Identifier) 已被其他 UId 绑定（唯一约束兜底）。</summary>
    public const string FACTOR_ALREADY_BOUND = "FACTOR_ALREADY_BOUND";

    /// <summary>标识超长（&gt;256）拒绝——不做 hash 降级（方案 §6.1）。</summary>
    public const string IDENTIFIER_TOO_LONG = "IDENTIFIER_TOO_LONG";

    /// <summary>因素不存在或不属于该 UId（归属校验失败——fail-closed 不泄露）。</summary>
    public const string FACTOR_NOT_FOUND = "FACTOR_NOT_FOUND";
}
