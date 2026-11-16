using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换历史查询门面——授权面自有（UserCenter 退役后兑换查询唯一通道）。
/// <para>仅本人：userId 显式参数（装配层强制）；DTO 归本扩展主包（<see cref="RedemptionRecordDto"/>）。
/// v0.3.0 T-5/T-6/T-8：按明文哈希读码 lookup / 兑换历史分页 / payload 解密只读门面（A-2 采纳：只读方法归查询门面）。</para>
/// </summary>
public interface IRedemptionQueryService : IDomainService
{
    /// <summary>
    /// 取用户兑换历史（Redeemed 态，按兑换时间倒序；code 已脱敏透传）。
    /// <para>v0.3.0 T-6：<paramref name="page"/>/<paramref name="pageSize"/> 可空——null=全量（向后兼容）；分页语义=倒序窗口（对齐页序稳定）；
    /// ⚠️ ct 位置参数位移（第 2 → 第 4 位，A-5 注明——现无位置调用消费方，具名调用兼容，实际影响为零）。</para>
    /// </summary>
    Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, int? page = null, int? pageSize = null, CancellationToken ct = default);

    /// <summary>
    /// 按明文哈希读码（v0.3.0 T-5）——<b>收明文内部哈希</b>（<see cref="RedemptionCodeGenerator.Hash"/>
    /// SHA256/UTF8/hex64；公开哈希契约不开启）；只读、不含明文/掩码；批次头两跳取 <c>BatchTest</c>/<c>Channel</c>
    /// （BatchId null → 均 null）；<b>不存在码 → 返回 null</b>（C-4① 查询语义）；只读预检非兑换承诺（CAS 仍唯一裁决）；调用不计兑换频控。
    /// </summary>
    Task<CodeBatchLookupDto?> GetByCodeAsync(string codeInput, CancellationToken ct = default);

    /// <summary>
    /// payload 明文只读取回（v0.3.0 T-8）——可重复读取（每次调用解密返回，不消费不擦除）；
    /// 无 payload/码不存在 → 返回 null（消费方映射 PAYLOAD_UNAVAILABLE）；<b>明文不落日志</b>；
    /// 鉴权由装配层管理端二次鉴权（方法不加匿名开关）。
    /// </summary>
    Task<string?> GetPayloadAsync(long codeId, CancellationToken ct = default);
}

/// <summary>
/// 兑换历史查询门面实现——委托 <see cref="UserRedemptionHistoryViewDataService"/>（跨扩展 VEntity 只读 DataService，红线合规）
/// + <see cref="RedemptionCodeEntityDataService"/>/<see cref="CodeBatchEntityDataService"/>（v0.3.0 读码两跳/payload）。
/// <para>v0.3.0 T-8：ctor 注入 keyed <see cref="ISymmetricKeyProvider"/>（键 <see cref="SymmetricKeyProviderKeys.AuthSurface"/>，
/// 对齐 RedemptionCommandService 形态——测试宿主已有 keyed 注册）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class RedemptionQueryService : DomainServiceBase, IRedemptionQueryService
{
    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<RedemptionQueryService> _logger;

    private UserRedemptionHistoryViewDataService? _historyDataService;
    private UserRedemptionHistoryViewDataService HistoryDataService
        => _historyDataService ??= User.Use<UserRedemptionHistoryViewDataService>();

    private RedemptionCodeEntityDataService? _codeDataService;
    private RedemptionCodeEntityDataService CodeDataService
        => _codeDataService ??= User.Use<RedemptionCodeEntityDataService>();

    private CodeBatchEntityDataService? _batchDataService;
    private CodeBatchEntityDataService BatchDataService
        => _batchDataService ??= User.Use<CodeBatchEntityDataService>();

    public RedemptionQueryService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.AuthSurface)] ISymmetricKeyProvider keys,
        ILogger<RedemptionQueryService> logger) : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var rows = await HistoryDataService.GetHistoryByUserIdAsync(userId, ct);
        var ordered = rows.OrderByDescending(r => r.RedeemedAtUtc)
            .ThenByDescending(r => r.CodeMasked);
        var paged = page is { } p && pageSize is { } ps && p > 0 && ps > 0
            ? ordered.Skip((p - 1) * ps).Take(ps)
            : ordered;
        return paged.Select(r => new RedemptionRecordDto(
                CodeMasked: r.CodeMasked,
                ProductName: r.ProductName,
                TargetAppId: r.TargetAppId,
                RedeemedAtUtc: r.RedeemedAtUtc ?? default,   // Status=1 行必有（CAS 兑换设置）；视图 JOIN 保证
                Status: "redeemed"))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CodeBatchLookupDto?> GetByCodeAsync(string codeInput, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeInput);

        var hash = RedemptionCodeGenerator.Hash(codeInput);
        var code = await CodeDataService.GetByCodeHashAsync(hash, ct);
        if (code is null) return null;                        // 不存在码 → null（查询语义，消费方归 REDEMPTION_CODE_INVALID 防枚举）

        // 头行两跳取 batchTest/channel（BatchId null → 均 null）
        bool? batchTest = null;
        string? channel = null;
        if (code.BatchId is { } batchId)
        {
            var batch = await BatchDataService.GetByIdAsync(batchId, ct);
            batchTest = batch?.Test;
            channel = batch?.Channel;
        }
        return new CodeBatchLookupDto(
            Id: code.Id,
            TargetAppId: code.TargetAppId,
            Status: code.Status,
            ExpireAtUtc: code.ExpireAtUtc,
            BatchId: code.BatchId,
            BatchTest: batchTest,
            Channel: channel);
    }

    /// <inheritdoc />
    public async Task<string?> GetPayloadAsync(long codeId, CancellationToken ct = default)
    {
        var code = await CodeDataService.EntityGetAsync(e => e.Id == codeId, ct);
        if (code is null || string.IsNullOrEmpty(code.PayloadEncrypted)) return null;
        return _keys.Decrypt(code.PayloadEncrypted);          // 可重复只读——明文不落日志
    }
}