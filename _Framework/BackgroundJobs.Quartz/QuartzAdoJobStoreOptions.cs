using System;

namespace TKWF.Ext.BackgroundJobs.Quartz;

/// <summary>
/// Quartz AdoJobStore 一键配置选项——封装 Quartz 12 表持久化存储的常用参数。
/// <para>配置级封装——Quartz 自管存储（SchemaProvisioning 自动建表），扩展不执行任何 SQL/不触碰 TKWF 数据访问（红线合规）。</para>
/// <para>绑定配置节：<c>TKWF:BackgroundJobs:Quartz:AdoJobStore</c></para>
/// </summary>
public sealed class QuartzAdoJobStoreOptions
{
    /// <summary>
    /// 数据库 Provider——支持 "sqlserver" / "sqlite" / "postgresql"（大小写不敏感）。
    /// <para>必填。非支持值抛 <see cref="NotSupportedException"/>。</para>
    /// </summary>
    public string DbProvider { get; set; } = "";

    /// <summary>
    /// 数据库连接字符串。
    /// <para>必填。为空抛 <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Quartz 表前缀（默认 "QRTZ_"）。
    /// </summary>
    public string TablePrefix { get; set; } = "QRTZ_";

    /// <summary>
    /// 是否自动建表（默认 true）。
    /// <para>true = 调用 ProvisionSchema()（等效 SchemaProvisioning.CreateIfMissing，开发环境自动建表）；false = 不自动建表（Quartz 默认 SchemaProvisioning.Validate——生产环境 DBA 手动建表脚本，Quartz 启动时校验 schema 存在性）。</para>
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// 是否启用集群模式（默认 false）。
    /// <para>true = UseClustering + InstanceId + ClusterCheckinInterval 生效。</para>
    /// </summary>
    public bool Clustering { get; set; }

    /// <summary>
    /// 集群实例 ID（默认 "AUTO"）。
    /// <para>仅 Clustering = true 时生效。</para>
    /// </summary>
    public string InstanceId { get; set; } = "AUTO";

    /// <summary>
    /// 集群心跳检查间隔。
    /// <para>null = 使用 Quartz 默认值（7500ms）；仅 Clustering = true 时生效。</para>
    /// </summary>
    public TimeSpan? ClusterCheckinInterval { get; set; }
}
