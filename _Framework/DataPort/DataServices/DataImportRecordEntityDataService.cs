using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.DataPort;
using TKWF.Ext.DataPort.DTOs;

namespace TKWF.Ext.DataPort;

partial class DataImportRecordEntityDataService(IDomainUser user, IEntityDAC<DataImportRecordEntity> dac)
    : DomainDataServiceBase<DataImportRecordEntity, DataImportRecordEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：DataImportTaskService 委托路径的业务方法 ──
    // 说明：StartTime/CreateTime 标注 CanUpdate=false（实体声明）——重置/更新时保留原值。
    // 原 raw SQL 的"UTC DateTime 语义"由实体 DateTime 类型 + 服务显式赋值保证（非 raw SQL 必需）。

    /// <summary>按 FileHash 查询批次记录（幂等检查）。</summary>
    public async Task<DataImportRecordEntity?> GetByFileHashAsync(string fileHash, CancellationToken ct = default)
        => await EntityGetAsync(r => r.FileHash == fileHash, ct);

    /// <summary>按 BatchNo 查询批次记录（回查/回滚入口）。</summary>
    public async Task<DataImportRecordEntity?> GetByBatchNoAsync(string batchNo, CancellationToken ct = default)
        => await EntityGetAsync(r => r.BatchNo == batchNo, ct);

    /// <summary>创建批次记录（回写自增 Id）。</summary>
    public async Task CreateAsync(DataImportRecordEntity record, CancellationToken ct = default)
        => await EntityCreateAsync(record, ct);

    /// <summary>重置失败批次为 Processing（原 raw SQL ①——StartTime 保留 CanUpdate=false 不覆盖，其余字段重置）。
    /// V0.1.5（DP-04）：<b>不清空 FailedBatchIndexes/BatchSize/MaxAttemptedBatchIndex 三新列</b>——重导判定依据（整实体读改写自然保留）。</summary>
    public async Task ResetToProcessingAsync(long id, DateTime updateTime, CancellationToken ct = default)
    {
        var record = await EntityGetAsync(r => r.Id == id, ct);
        if (record == null) return;

        record.Status = "Processing";
        record.EndTime = null;
        record.ErrorSummary = null;
        record.SuccessCount = 0;
        record.FailedCount = 0;
        record.BatchFailureCount = 0;
        record.UpdateTime = updateTime;
        // StartTime 不重置（CanUpdate=false 保留首次导入时间——语义优于原 raw SQL 覆盖）
        await EntityUpdateAsync(record, ct);
    }

    /// <summary>更新批次状态（原 raw SQL ②——成功/部分成功路径）。
    /// <para>V0.1.5（DP-04）尾参 ×2：<paramref name="failedBatchIndexes"/>（JSON 数字数组字符串，PartiallySucceeded 写入；Succeeded/Failed 传 null 清除）+ <paramref name="maxAttemptedBatchIndex"/>（已尝试最大批次索引，≥0 写入；null 不覆盖）。</para></summary>
    public async Task UpdateStatusAsync(
        long id, string status, int successCount, int failedCount, int batchFailureCount,
        DateTime endTime, string? errorSummary, DateTime updateTime, CancellationToken ct = default,
        string? failedBatchIndexes = null, int? maxAttemptedBatchIndex = null)
    {
        var record = await EntityGetAsync(r => r.Id == id, ct);
        if (record == null) return;

        record.Status = status;
        record.SuccessCount = successCount;
        record.FailedCount = failedCount;
        record.BatchFailureCount = batchFailureCount;
        record.EndTime = endTime;
        record.ErrorSummary = errorSummary;
        record.UpdateTime = updateTime;
        // V0.1.5（DP-04）：终态一并写入重放判定状态（尾参可选——既有调用不传则保留原值，非破坏）
        record.FailedBatchIndexes = failedBatchIndexes;
        record.MaxAttemptedBatchIndex = maxAttemptedBatchIndex;
        await EntityUpdateAsync(record, ct);
    }

    /// <summary>查询超时 Processing 记录（DP-05 恢复扫描复用——WHERE Status='Processing' AND UpdateTime&lt;阈值，走 TKWFIX_Dir_Status_Updatetime 索引）。</summary>
    public async Task<List<DataImportRecordEntity>> ListStaleProcessingAsync(
        DateTime olderThanUtc, int limit, CancellationToken ct = default)
        => await EntitySelectAsync(r => r.Status == "Processing" && r.UpdateTime < olderThanUtc, skip: 0, limit: limit, ct: ct);

    /// <summary>心跳 touch（DP-05 P1-2）——CAS 条件更新：仅 Processing 态刷新 UpdateTime（防覆写终态）；返回影响行数（0 = 已终态化，调用方停止心跳）。</summary>
    public Task<int> TouchProcessingAsync(long id, DateTime utcNow, CancellationToken ct = default)
        => EntityUpdateWhereAsync<DataImportRecordEntity>(
            r => r.Id == id && r.Status == "Processing",
            e => new DataImportRecordEntity { UpdateTime = utcNow },
            ct);

    /// <summary>CAS 恢复（DP-05 P1-1）——条件更新重置超时 Processing → Failed（WHERE Id AND Status='Processing' AND UpdateTime&lt;阈值），返回影响行数（0 = 并发终态化/心跳刷新/他实例已恢复→跳过）。</summary>
    public Task<int> MarkStaleFailedAsync(
        long id, DateTime staleBeforeUtc, string errorSummary, DateTime utcNow, CancellationToken ct = default)
        => EntityUpdateWhereAsync<DataImportRecordEntity>(
            r => r.Id == id && r.Status == "Processing" && r.UpdateTime < staleBeforeUtc,
            e => new DataImportRecordEntity { Status = "Failed", EndTime = utcNow, ErrorSummary = errorSummary, UpdateTime = utcNow },
            ct);

    /// <summary>标记批次失败（原 raw SQL ③——异常路径，best-effort）。</summary>
    public async Task MarkFailedAsync(long id, DateTime endTime, string errorSummary, DateTime updateTime, CancellationToken ct = default)
    {
        var record = await EntityGetAsync(r => r.Id == id, ct);
        if (record == null) return;

        record.Status = "Failed";
        record.EndTime = endTime;
        record.ErrorSummary = errorSummary;
        record.UpdateTime = updateTime;
        await EntityUpdateAsync(record, ct);
    }
}
