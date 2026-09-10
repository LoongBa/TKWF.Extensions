using TKW.Framework.Domain;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 后台任务持久化扩展配置（V0.1.0）——<c>TKWF:BackgroundJobs</c> 配置节。
/// <para>RetentionDays 已启用（v0.2.0 历史清理任务使用）；CleanupBatchSize 控制分批大小。</para>
/// </summary>
[Options("TKWF:BackgroundJobs")]
public sealed class BackgroundJobsPersistenceOptions
{
    /// <summary>执行历史保留天数（默认 180 天；v0.2.0 起由 <see cref="IJobHistoryCleanupService"/> 消费）。</summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>历史清理分批大小（v0.2.0）——每轮每表最多删除的过期条数，防止大表一次性删爆事务/锁。</summary>
    public int CleanupBatchSize { get; set; } = 500;
}