using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 批次管理门面（v0.3.0 T-0~T-3——批次域整体：创建/生成/删除/列表统计/明细/券分页/整批与单码吊销）。
/// <para>DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载（DI004 零豁免）；事务经 <see cref="ITransactionManager"/>
/// 显式 Begin/Commit（方法内收敛——无跨枚举长事务/无 Dispose 悬挂风险；异常/未 Commit 释放自动回滚）。</para>
/// <para>暴露面：批次管理端点归消费方（EduPlatform <c>/api/edu/admin/code-batches</c> 族）——扩展不生成管理端点（转告 §四定稿）。</para>
/// </summary>
public interface ICodeBatchService : IDomainService
{
    /// <summary>创建批次（DRAFT）——校验（Name/ProductName/TargetAppId 非空、Quantity 1..MaxBatchQuantity、ExpireAtUtc null 或晚于 now、Channel≤100、ValidityDays&gt;0）；BatchNo=<c>B{yyyyMMdd}{4 随机大写字母数字}</c>（CSPRNG，唯一冲突重试 ≤3 次）。</summary>
    Task<CodeBatchDto> CreateBatchAsync(CreateCodeBatchRequest request, CancellationToken ct = default);

    /// <summary>
    /// 生成即导出（CAS 抢占 DRAFT→GENERATING → 逐码回调 → 全量成功落 GENERATED）。
    /// <para>回调主方案（C-2）：<paramref name="onPlainCode"/> 门面不缓存不落库不落日志（明文一次性——回调内即消费，如写入 CSV 流）；
    /// 回调异常/ct 取消 → 整体回滚 DRAFT + 码零残留（同事务——进程崩溃 DB 回滚自然回 DRAFT 可重试）。
    /// 状态机：抢占败者（已非 DRAFT）→ <see cref="BatchErrorCodes.BatchNotEditable"/>；批次不存在 → <see cref="BatchErrorCodes.BatchNotFound"/>。</para>
    /// </summary>
    Task GenerateBatchAsync(long batchId, Func<string, CancellationToken, Task> onPlainCode, CancellationToken ct = default);

    /// <summary>删除批次（仅 DRAFT 物理删——无已生成码）；不存在 → <see cref="BatchErrorCodes.BatchNotFound"/>；非 DRAFT → <see cref="BatchErrorCodes.BatchNotEditable"/>。</summary>
    Task DeleteBatchAsync(long batchId, CancellationToken ct = default);

    /// <summary>批次列表分页 + 每批统计（filter：状态/渠道/创建时间范围/名称模糊/Test；pageSize 截断 ≤100；统计口径见 <see cref="CodeBatchDto"/>）。</summary>
    Task<CodeBatchPageDto> GetBatchesAsync(CodeBatchFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>批次详情 + 统计 + 派生标志（PartialRevoked = Status==2 且 0&lt;RevokedCount&lt;PendingCount+ExpiredCount）；不存在 → <see cref="BatchErrorCodes.BatchNotFound"/>。</summary>
    Task<CodeBatchDetailDto> GetBatchAsync(long batchId, CancellationToken ct = default);

    /// <summary>券明细分页（掩码/状态/归属/时间——明文不出现在列表；status 过滤；倒序 RedeemedAtUtc/Id；越界→空页非异常；批次不存在 → <see cref="BatchErrorCodes.BatchNotFound"/>）。</summary>
    Task<CodePageDto> GetBatchCodesAsync(long batchId, int page, int pageSize, int? status, CancellationToken ct = default);

    /// <summary>整批吊销（批次须 GENERATED——单语句条件更新未兑码翻 Revoked + 批次状态机同事务落 REVOKED；REVOKED 幂等返回；不存在 → <see cref="BatchErrorCodes.BatchNotFound"/>；非 GENERATED 非 REVOKED → <see cref="BatchErrorCodes.BatchNotEditable"/>）。</summary>
    Task RevokeBatchAsync(long batchId, CancellationToken ct = default);

    /// <summary>单码吊销（未兑翻 Revoked → true；已兑/已吊销/不存在 → false 幂等不抛；翻成功 → 同事务查该批未兑计数==0 且批次 GENERATED → 批次升 REVOKED）。</summary>
    Task<bool> RevokeCodeAsync(long codeId, CancellationToken ct = default);
}

/// <summary>
/// 批次管理门面实现——委托 <see cref="CodeBatchEntityDataService"/>（批次头）+ <see cref="RedemptionCodeEntityDataService"/>（码行，红线合规，零 IFreeSql/IEntityDAC 直注入）。
/// <para>v0.3.0（ADR90 门面范式）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册 <c>AddConstructibleService</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class CodeBatchService : DomainServiceBase, ICodeBatchService
{
    private readonly AuthSurfaceOptions _options;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<CodeBatchService> _logger;

    private CodeBatchEntityDataService? _batchDataService;
    private CodeBatchEntityDataService BatchDataService
        => _batchDataService ??= User.Use<CodeBatchEntityDataService>();

    private RedemptionCodeEntityDataService? _codeDataService;
    private RedemptionCodeEntityDataService CodeDataService
        => _codeDataService ??= User.Use<RedemptionCodeEntityDataService>();

    public CodeBatchService(
        IDomainUser user,
        IOptions<AuthSurfaceOptions> options,
        ITransactionManager transactionManager,
        ILogger<CodeBatchService> logger) : base(user)
    {
        _options = options?.Value ?? new AuthSurfaceOptions();
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CodeBatchDto> CreateBatchAsync(CreateCodeBatchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetAppId);
        if (request.Name.Length > 100) throw new ArgumentException("批次名称超长——上限 100 字符", nameof(request.Name));
        if (request.ProductName.Length > 100) throw new ArgumentException("产品名超长——上限 100 字符", nameof(request.ProductName));
        if (request.TargetAppId.Length > 100) throw new ArgumentException("目标应用超长——上限 100 字符", nameof(request.TargetAppId));
        if (request.Quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), $"计划数量须 ≥1（实际 {request.Quantity}）");
        if (request.Quantity > _options.MaxBatchQuantity)
            throw new AuthenticationException(BatchErrorCodes.BatchQuantityExceeded);
        if (request.ExpireAtUtc is { } exp && exp <= DateTime.UtcNow)
            throw new ArgumentException("批次有效期须晚于当前时间（UTC）", nameof(request.ExpireAtUtc));
        if (request.Channel is { Length: > 100 })
            throw new ArgumentException("发行渠道超长——上限 100 字符", nameof(request.Channel));
        if (request.ValidityDays is { } vd && vd <= 0)
            throw new ArgumentException("ValidityDays 须 >0（自然日模板；null=无 grant 语义）", nameof(request.ValidityDays));
        if (request.CreatedBy is { Length: > 64 })
            throw new ArgumentException("创建人超长——上限 64 字符", nameof(request.CreatedBy));

        // BatchNo = B{yyyyMMdd}{4 位随机大写字母数字}（CSPRNG，无模偏差需拒绝重试；同日唯一约束撞（23505）→ 重试再生成，有限 3 次——A-3）
        var now = DateTime.UtcNow;
        var datePrefix = now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        const int maxRetry = 3;
        for (var attempt = 0; ; attempt++)
        {
            var batchNo = $"B{datePrefix}{NewRandomSuffix(4)}";
            var entity = new CodeBatchEntity
            {
                BatchNo = batchNo,
                Name = request.Name.Trim(),
                ProductName = request.ProductName.Trim(),
                TargetAppId = request.TargetAppId.Trim(),
                ValidityDays = request.ValidityDays,
                ExpireAtUtc = request.ExpireAtUtc,
                Channel = string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel.Trim(),
                Quantity = request.Quantity,
                GeneratedCount = 0,
                Status = 0,                       // DRAFT
                Test = request.Test,
                CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? null : request.CreatedBy.Trim(),
                CreateTime = now,
                UpdateTime = now,
            };
            try
            {
                var created = await BatchDataService.EntityCreateAsync(entity, ct);
                return ToDto(created, new CodeBatchStatsDto(0, 0, 0, 0));
            }
            catch (Exception ex) when (IsUniqueViolation(ex) && attempt < maxRetry)
            {
                _logger.LogWarning("批次号 {BatchNo} 唯一冲突（同日碰撞），重试 {Attempt}/{MaxRetry}", batchNo, attempt + 1, maxRetry);
            }
        }
    }

    /// <inheritdoc />
    public async Task GenerateBatchAsync(long batchId, Func<string, CancellationToken, Task> onPlainCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onPlainCode);

        using var scope = await _transactionManager.BeginAsync(ct: ct);
        try
        {
            var head = await BatchDataService.GetByIdAsync(batchId, ct)
                ?? throw new AuthenticationException(BatchErrorCodes.BatchNotFound);

            // CAS 抢占 DRAFT→GENERATING（谓词 status==0 且 GeneratedCount==0——败者=已生成/生成中/已吊销 → 不可编辑）
            if (!await BatchDataService.CasClaimGeneratingAsync(batchId, ct))
                throw new AuthenticationException(BatchErrorCodes.BatchNotEditable);

            // 逐码生成（回调主方案 C-2：门面不缓存明文——回调内即消费；异常/取消 → 事务回滚 DRAFT + 码零残留）
            var now = DateTime.UtcNow;
            for (var i = 0; i < head.Quantity; i++)
            {
                ct.ThrowIfCancellationRequested();
                var plain = RedemptionCodeGenerator.Generate();
                await CodeDataService.EntityCreateAsync(new RedemptionCodeEntity
                {
                    CodeHash = RedemptionCodeGenerator.Hash(plain),
                    CodeMasked = RedemptionCodeGenerator.Mask(plain),
                    ProductName = head.ProductName,          // 头投影冗余
                    TargetAppId = head.TargetAppId,
                    Status = 0,                              // Available
                    ExpireAtUtc = head.ExpireAtUtc,          // 继承头有效期
                    BatchId = batchId,
                    CreateTime = now,
                    UpdateTime = now,
                }, ct);
                await onPlainCode(plain, ct);                // 明文一次性回调（CSV 流等消费方写入）
            }

            // 全量成功 → GENERATED + GeneratedCount=Quantity + ExportedAtUtc=now
            if (!await BatchDataService.CompleteGeneratingAsync(batchId, head.Quantity, ct))
                throw new InvalidOperationException($"批次 {batchId} 生成完成落定失败（GENERATING 态已被并发路径消费）");

            await scope.CommitAsync(ct);
        }
        catch
        {
            // using scope Dispose → 未 Commit 自动回滚（GENERATING → DRAFT——含码行删除）
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteBatchAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await BatchDataService.GetByIdAsync(batchId, ct)
            ?? throw new AuthenticationException(BatchErrorCodes.BatchNotFound);
        if (batch.Status != 0)
            throw new AuthenticationException(BatchErrorCodes.BatchNotEditable);
        if (!await BatchDataService.CasRemoveAsync(batchId, ct))
            throw new AuthenticationException(BatchErrorCodes.BatchNotEditable);
    }

    /// <inheritdoc />
    public async Task<CodeBatchPageDto> GetBatchesAsync(CodeBatchFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var p = Math.Max(page, 1);
        var size = Math.Clamp(pageSize, 1, 100);            // 截断 ≤100（方案 §4.2）

        // 过滤谓词（C# 侧组合 + 参数替换归一——EntitySelectAsync 表达式下推；null 参数不生成 IS NULL 的坑由谓词显式处理）
        var f = filter ?? new CodeBatchFilter();
        var parts = new List<System.Linq.Expressions.Expression<Func<CodeBatchEntity, bool>>>();
        if (f.Status is { } st) parts.Add(e => e.Status == st);
        if (f.Channel is { } ch) parts.Add(e => e.Channel == ch);
        if (f.StartTime is { } s) parts.Add(e => e.CreateTime >= s);
        if (f.EndTime is { } en) parts.Add(e => e.CreateTime <= en);
        if (f.NameKeyword is { Length: > 0 } kw) parts.Add(e => e.Name.Contains(kw));
        if (f.Test is { } t) parts.Add(e => e.Test == t);
        System.Linq.Expressions.Expression<Func<CodeBatchEntity, bool>>? predicate = parts.Count == 0 ? null : CombineAnd(parts);

        // 全量取过滤结果（批次量级小——R2 降级分桶；页内逐批统计）
        var all = await BatchDataService.EntitySelectAsync(predicate, skip: 0, limit: int.MaxValue,
            orderBy: q => q.OrderByDescending(e => e.CreateTime).ThenByDescending(e => e.Id), ct: ct);
        var total = all.Count;
        var pageRows = all.Skip((p - 1) * size).Take(size).ToList();

        var items = new List<CodeBatchDto>(pageRows.Count);
        foreach (var b in pageRows)
        {
            var stats = await CodeDataService.GetStatsByBatchIdAsync(b.Id, ct);
            items.Add(ToDto(b, stats));
        }
        return new CodeBatchPageDto(items, total);
    }

    /// <inheritdoc />
    public async Task<CodeBatchDetailDto> GetBatchAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await BatchDataService.GetByIdAsync(batchId, ct)
            ?? throw new AuthenticationException(BatchErrorCodes.BatchNotFound);
        var stats = await CodeDataService.GetStatsByBatchIdAsync(batchId, ct);
        var partial = batch.Status == 2                       // GENERATED（未整体吊销）
            && stats.RevokedCount > 0                          // 已有单张吊销
            && stats.PendingCount + stats.ExpiredCount > 0;    // 仍有未兑码（未全吊——全吊已在 RevokeCodeAsync 升 REVOKED）
        return new CodeBatchDetailDto(ToDto(batch, stats), PartialRevoked: partial);
    }

    /// <inheritdoc />
    public async Task<CodePageDto> GetBatchCodesAsync(long batchId, int page, int pageSize, int? status, CancellationToken ct = default)
    {
        _ = await BatchDataService.GetByIdAsync(batchId, ct)
            ?? throw new AuthenticationException(BatchErrorCodes.BatchNotFound);
        var p = Math.Max(page, 1);
        var size = Math.Clamp(pageSize, 1, 100);

        var rows = await CodeDataService.EntitySelectAsync(
            e => e.BatchId == batchId && (status == null || e.Status == status.Value),
            skip: 0, limit: int.MaxValue,
            orderBy: q => q.OrderByDescending(e => e.RedeemedAtUtc).ThenByDescending(e => e.Id), ct: ct);
        return new CodePageDto(
            rows.Skip((p - 1) * size).Take(size)
                .Select(r => new CodeMaskedRowDto(r.Id, r.CodeMasked, r.Status, r.RedeemedByUId, r.RedeemedAtUtc))
                .ToList(),
            rows.Count);
    }

    /// <inheritdoc />
    public async Task RevokeBatchAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await BatchDataService.GetByIdAsync(batchId, ct)
            ?? throw new AuthenticationException(BatchErrorCodes.BatchNotFound);
        if (batch.Status == 3) return;                        // REVOKED 幂等返回
        if (batch.Status != 2)                                 // 须 GENERATED（DRAFT 无码吊销无意义；GENERATING 生成中）
            throw new AuthenticationException(BatchErrorCodes.BatchNotEditable);

        using var scope = await _transactionManager.BeginAsync(ct: ct);
        try
        {
            // 单语句条件更新未兑码（BatchId==? && Status==0 → Status=3）+ 批次状态机同事务 GENERATED→REVOKED
            _ = await CodeDataService.RevokeByBatchAsync(batchId, ct);
            await BatchDataService.EntityUpdateWhereAsync(
                where: e => e.Id == batchId && e.Status == 2,
                setColumns: e => new CodeBatchEntity { Status = 3, UpdateTime = DateTime.UtcNow },
                ct: ct);
            await scope.CommitAsync(ct);
        }
        catch
        {
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeCodeAsync(long codeId, CancellationToken ct = default)
    {
        using var scope = await _transactionManager.BeginAsync(ct: ct);
        try
        {
            // 单语句条件更新（Id==? && Status==0 → Status=3）——影响 0 行 = 已兑/已吊销/不存在 → false 幂等不抛（C-4②）
            if (!await CodeDataService.RevokeSingleAsync(codeId, ct))
                return false;

            // 翻成功 → 同事务查该批归属（BatchId）→ 判定「吊销后该批 status==0 码计数==0」且批次 GENERATED → 批次升 REVOKED
            var code = await CodeDataService.EntityGetAsync(e => e.Id == codeId, ct);
            if (code?.BatchId is { } batchId
                && await CodeDataService.CountActiveByBatchAsync(batchId, ct) == 0)
            {
                await BatchDataService.EntityUpdateWhereAsync(
                    where: e => e.Id == batchId && e.Status == 2,
                    setColumns: e => new CodeBatchEntity { Status = 3, UpdateTime = DateTime.UtcNow },
                    ct: ct);
            }
            await scope.CommitAsync(ct);
            return true;
        }
        catch
        {
            throw;                                            // 未 Commit → 释放自动回滚（单码吊销 + 批次升 REVOKED 原子）
        }
    }

    /// <summary>批次头 → 列表 DTO（统计分桶并入）。</summary>
    private static CodeBatchDto ToDto(CodeBatchEntity b, CodeBatchStatsDto s)
        => new(
            Id: b.Id,
            BatchNo: b.BatchNo,
            Name: b.Name,
            ProductName: b.ProductName,
            TargetAppId: b.TargetAppId,
            ValidityDays: b.ValidityDays,
            ExpireAtUtc: b.ExpireAtUtc,
            Channel: b.Channel,
            Quantity: b.Quantity,
            GeneratedCount: b.GeneratedCount,
            Status: b.Status,
            Test: b.Test,
            ExportedAtUtc: b.ExportedAtUtc,
            CreatedBy: b.CreatedBy,
            CreateTime: b.CreateTime,
            RedeemedCount: s.RedeemedCount,
            PendingCount: s.PendingCount,
            ExpiredCount: s.ExpiredCount,
            RevokedCount: s.RevokedCount,
            RedeemRate: b.GeneratedCount > 0 ? (double)s.RedeemedCount / b.GeneratedCount : 0);

    /// <summary>组合多个谓词（AND）——共享单一参数、AndAlso 拼接（避免 Expression.Invoke——FreeSql 表达式翻译兼容性）。</summary>
    private static System.Linq.Expressions.Expression<Func<CodeBatchEntity, bool>> CombineAnd(
        IReadOnlyList<System.Linq.Expressions.Expression<Func<CodeBatchEntity, bool>>> parts)
    {
        var target = System.Linq.Expressions.Expression.Parameter(typeof(CodeBatchEntity), "e");
        var visitor = new ParameterReplaceVisitor(target);
        var body = visitor.Visit(parts[0].Body);
        for (var i = 1; i < parts.Count; i++)
            body = System.Linq.Expressions.Expression.AndAlso(body, visitor.Visit(parts[i].Body));
        return System.Linq.Expressions.Expression.Lambda<Func<CodeBatchEntity, bool>>(body, target);
    }

    /// <summary>表达式参数替换（组合谓词归一为共享参数）。</summary>
    private sealed class ParameterReplaceVisitor(System.Linq.Expressions.ParameterExpression target)
        : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node)
            => target;
    }

    /// <summary>CSPRNG 随机大写字母数字后缀（<see cref="RandomNumberGenerator.GetInt32(int)"/> 内部无模偏差）。</summary>
    private static string NewRandomSuffix(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(length, alphabet, static (chars, abc) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = abc[RandomNumberGenerator.GetInt32(abc.Length)];
        });
    }

    /// <summary>
    /// 唯一约束冲突判定（对齐 AuthCenter IAuthGrantCommandService 先例——主项目未显式引用各 Db provider 包，以异常消息特征判定）：
    /// SQLite "UNIQUE constraint failed" / PG "duplicate key" / MySql "Duplicate entry"——覆盖主要 Provider。
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}