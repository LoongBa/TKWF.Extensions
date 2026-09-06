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

    /// <summary>重置失败批次为 Processing（原 raw SQL ①——StartTime 保留 CanUpdate=false 不覆盖，其余字段重置）。</summary>
    public async Task ResetToProcessingAsync(long id, DateTime startTime, DateTime updateTime, CancellationToken ct = default)
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

    /// <summary>更新批次状态（原 raw SQL ②——成功/部分成功路径）。</summary>
    public async Task UpdateStatusAsync(
        long id, string status, int successCount, int failedCount, int batchFailureCount,
        DateTime endTime, string? errorSummary, DateTime updateTime, CancellationToken ct = default)
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
        await EntityUpdateAsync(record, ct);
    }

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
