using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.MFA.DTOs;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 绑定 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（Disable 解绑删绑定行）。</para>
/// <para>数据访问红线：Service 层只依赖 DataService（ADR61 自动注册），零 IFreeSql/IEntityDAC 直注入。</para>
/// <para>secret AES-GCM 加解密在 <c>TotpMfaMethod</c> 方法层（经 keyed <see cref="ISymmetricKeyProvider"/>，
/// E4 密钥管理抽象 V0.2.0）；本 DataService 纯持久化。</para>
/// </summary>
partial class MfaSecretEntityDataService(IDomainUser user, IEntityDAC<MfaSecretEntity> dac)
    : DomainDataServiceBase<MfaSecretEntity, MfaSecretEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Service 委托路径的业务方法（异常自然传播——业务规则由 MfaService 处理） ──

    /// <summary>按用户 + 方法查已激活绑定（启用判定 = IsConfirmed）。</summary>
    public Task<MfaSecretEntity?> GetActiveSecretAsync(string userId, string method, CancellationToken ct = default)
        => EntityGetAsync(f => f.UserId == userId && f.Method == method && f.IsConfirmed, ct);

    /// <summary>按用户 + 方法查待激活绑定（ConfirmEnroll 流——校验 EnrollToken）。</summary>
    public Task<MfaSecretEntity?> GetPendingSecretAsync(string userId, string method, CancellationToken ct = default)
        => EntityGetAsync(f => f.UserId == userId && f.Method == method && !f.IsConfirmed, ct);

    /// <summary>按用户查全部绑定（IsMfaEnabledAsync / GetEnabledMethodsAsync / Disable 级联删除）。</summary>
    public async Task<IReadOnlyList<MfaSecretEntity>> GetByUserAsync(string userId, CancellationToken ct = default)
        => await EntitySelectAsync(f => f.UserId == userId, 0, MaxQuerySize, q => q.OrderBy(f => f.Id), ct);

    /// <summary>新增绑定（回写自增 Id）。</summary>
    public Task<MfaSecretEntity> CreateAsync(MfaSecretEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>更新绑定（激活翻转 IsConfirmed / 清空 EnrollTokenHash——CanUpdate=false 列除外）。</summary>
    public Task<MfaSecretEntity> UpdateAsync(MfaSecretEntity entity, CancellationToken ct = default)
        => EntityUpdateAsync(entity, ct);

    /// <summary>
    /// CAS 更新 last-used-step（MF-01 同窗重放防护记账，V0.3.0）——WHERE id + (null 或递增) 守卫——并发恰一成功；败者 0 行。
    /// <para>拒绝条件 <c>LastUsedTotpStep &lt; matchedStep</c>（同/更低步已被用 → 0 行 → 调用方统一失败，RFC 6238 §5.2）；
    /// setColumns 匿名类型（对齐 <see cref="MfaChallengeEntityDataService.MarkConsumedIfActiveAsync"/> ADR89 先例，不构造整实体）。</para>
    /// </summary>
    public Task<int> TryAdvanceLastUsedTotpStepAsync(long id, long matchedStep, CancellationToken ct = default)
        => EntityUpdateWhereAsync(f => f.Id == id && (f.LastUsedTotpStep == null || f.LastUsedTotpStep < matchedStep),
                                  f => new { LastUsedTotpStep = matchedStep }, ct);

    /// <summary>查询批量上限（防高频用户全量拉取——单用户绑定数极少，此限为兜底）。</summary>
    private const int MaxQuerySize = 1000;
}
