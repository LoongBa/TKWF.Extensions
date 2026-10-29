using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 弱身份清理能力门面（D03 1.4 §6.3 O2 定稿——能力归 AuthCenter，策略归业务）。
/// <para>判据（白名单制，fail-safe）：账号**至少有一个**认证因素且**全部为 Federated**（联邦路径建号只产 Federated）；
/// 零因素账号（无联邦建号证据，含消费方影子管理员）不判弱；任何非 Federated 因素（Password/Sms/Email/Totp/Passkey）
/// = 强身份不清。另需：无 UserProfile 档案、未冻结、<c>CreateTime</c> 不晚于 <paramref name="criteria"/> 推导的截止
/// （<c>inactiveDays</c> 由调用方传入——能力层不存策略；当前以建号时刻近似「无交互」，非活动证据探测）。</para>
/// <para>权责边界（A17）：WeChatOperations 只做策略存储/UI（<c>weakIdentityRetention</c> 配置域）；语义校验
/// 走本契约（WeakIdentityCriteria/Page/Stats 全封闭 DTO）。注册：<c>AddConstructibleService</c> 门面标准形态。</para>
/// </summary>
public interface IWeakIdentityCleanupService : IDomainService
{
    /// <summary>分页查询弱身份（判据见类注释；inactiveDays 调用方传入，能力层不存策略）。</summary>
    Task<WeakIdentityPage> QueryWeakIdentitiesAsync(WeakIdentityCriteria criteria, CancellationToken ct = default);

    /// <summary>批量清理（PurgeMode 两模式：SoftDelete=IsEnabled=false 留恢复窗口 / PhysicalDelete=GDPR 物理删）。</summary>
    Task<int> PurgeAsync(IReadOnlyList<string> platformUids, PurgeMode mode, CancellationToken ct = default);

    /// <summary>弱身份统计（数量/占比——运营指标卡片）。</summary>
    Task<WeakIdentityStats> GetWeakStatsAsync(CancellationToken ct = default);
}

/// <summary>弱身份清理模式（本期两者；Anonymize 后置——O2 定稿）。</summary>
public enum PurgeMode
{
    /// <summary>软删除：AuthAccount.IsEnabled=false（留恢复窗口——恢复 = 再次交互 AutoProvision 自动重建）。</summary>
    SoftDelete = 0,

    /// <summary>物理删除：账号行 + 关联因素/档案物理删（GDPR 删除权）。</summary>
    PhysicalDelete = 1,
}

/// <summary>弱身份查询条件（策略归调用方——本 DTO 不承载任何策略存储）。</summary>
/// <param name="InactiveDays">无交互天数阈值（调用方传入，能力层不存策略）。</param>
/// <param name="PageIndex">页码（1 起）。</param>
/// <param name="PageSize">页大小（默认 50，上限 200）。</param>
public sealed record WeakIdentityCriteria(int InactiveDays, int PageIndex = 1, int PageSize = 50);

/// <summary>弱身份分页结果。</summary>
/// <param name="Items">本页弱身份行（uid + 创建时刻 + 最后交互窗口推定）。</param>
/// <param name="Total">符合判据总数。</param>
/// <param name="HasMore">是否还有下一页。</param>
public sealed record WeakIdentityPage(IReadOnlyList<WeakIdentityItem> Items, int Total, bool HasMore);

/// <summary>弱身份单行。</summary>
/// <param name="UId">平台内部 id（操作键：PurgeAsync 入参）。</param>
/// <param name="CreatedAt">账号创建时刻（UTC——联邦路径建号时刻）。</param>
/// <param name="IsDisabled">是否已软删（SoftDelete 后行可见——恢复窗口对账）。</param>
public sealed record WeakIdentityItem(string UId, System.DateTime CreatedAt, bool IsDisabled);

/// <summary>弱身份统计（运营指标）。</summary>
/// <param name="WeakCount">弱身份总数（符合判据，含已软删）。</param>
/// <param name="AccountTotal">账号总数。</param>
/// <param name="WeakRatio">占比（0-1；AccountTotal=0 → 0）。</param>
public sealed record WeakIdentityStats(int WeakCount, int AccountTotal, double WeakRatio);