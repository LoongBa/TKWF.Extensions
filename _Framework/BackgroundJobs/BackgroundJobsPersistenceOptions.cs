using TKW.Framework.Domain;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 后台任务持久化扩展配置（V0.1.0）——<c>TKWF:BackgroundJobs</c> 配置节。
/// <para>RetentionDays 预留（v0.1.0 仅预留，清理任务 v0.2.0 实施）。</para>
/// </summary>
[Options("TKWF:BackgroundJobs")]
public sealed class BackgroundJobsPersistenceOptions
{
    /// <summary>执行历史保留天数（v0.1.0 预留，默认 180 天；v0.2.0 清理任务使用）。</summary>
    public int RetentionDays { get; set; } = 180;
}
