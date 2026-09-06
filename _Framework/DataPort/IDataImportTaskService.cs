using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Utility.DataPort;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// 数据导入任务服务——封装导入执行 + 批次记录落库 + FileHash 幂等检查 + 状态跟踪。
    /// <para>消费方自管业务数据持久化与回滚钩子（R2/R3）。</para>
    /// </summary>
    public interface IDataImportTaskService
    {
        /// <summary>
        /// 执行导入并记录批次信息。
        /// <para>幂等检查：FileHash 唯一（同文件已导入 → 拒绝/返回已有批次）。</para>
        /// <para>流程：幂等检查 → 记录落库（Processing）→ 核心导入 → 更新记录（Succeeded/Failed/PartiallySucceeded）。</para>
        /// </summary>
        /// <typeparam name="T">导入目标实体类型。</typeparam>
        /// <param name="filePath">文件路径。</param>
        /// <param name="adapter">消费方派生适配器（模板方法模式）。</param>
        /// <param name="batchOptions">批次配置（可选，使用 Options.DefaultBatchSize）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>导入结果（含批次号/记录 ID）。</returns>
        Task<DataImportTaskResult> ImportAsync<T>(
            string filePath,
            ImportAdapterBase<T> adapter,
            ImportBatchOptions? batchOptions = null,
            CancellationToken ct = default) where T : new();

        /// <summary>
        /// 按批次号查询导入记录（回滚入口）。
        /// </summary>
        Task<DataImportRecordEntity?> GetRecordByBatchNoAsync(string batchNo, CancellationToken ct = default);
    }

    /// <summary>
    /// 导入任务结果——包含记录 ID + 批次号 + 导入结果。
    /// </summary>
    public sealed record DataImportTaskResult(
        long RecordId,
        string BatchNo,
        ImportResult ImportResult);
}