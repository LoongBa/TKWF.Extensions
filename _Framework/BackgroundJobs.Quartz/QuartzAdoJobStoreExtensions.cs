using System;
using System.Collections.Generic;
using Quartz;

namespace TKWF.Ext.BackgroundJobs.Quartz;

/// <summary>
/// Quartz AdoJobStore 一键封装——配置级扩展（Quartz 自管存储，不触碰 TKWF 数据访问）。
/// </summary>
public static class QuartzAdoJobStoreExtensions
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlserver",
        "sqlite",
        "postgresql"
    };

    /// <summary>
    /// 一键配置 Quartz AdoJobStore（12 表自动建表 + 可选集群）。
    /// <para>配置级封装——Quartz 自管存储（SchemaProvisioning.CreateIfMissing 自动建表），扩展不执行任何 SQL/不触碰 TKWF 数据访问（红线合规）。</para>
    /// <para>生产建议 <c>AutoCreateSchema=false</c> + DBA 手动执行 Quartz 官方建表脚本（<c>tables_sqlServer.sql</c>/<c>tables_sqlite.sql</c>/<c>tables_postgres.sql</c>，随 Quartz 包附带）。</para>
    /// </summary>
    /// <param name="builder">Quartz 构建器。</param>
    /// <param name="configure">AdoJobStore 配置回调。</param>
    /// <returns><paramref name="builder"/> 以支持链式调用。</returns>
    /// <exception name="InvalidOperationException">ConnectionString 为空。</exception>
    /// <exception name="NotSupportedException">DbProvider 不在支持列表（sqlserver/sqlite/postgresql）。</exception>
    public static IQuartzBuilder UseTkfwAdoJobStore(
        this IQuartzBuilder builder,
        Action<QuartzAdoJobStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new QuartzAdoJobStoreOptions();
        configure(options);

        // ── 必填校验 ──
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                $"{nameof(QuartzAdoJobStoreOptions.ConnectionString)} 不能为空。" +
                $"请在配置中提供有效的数据库连接字符串（Quartz AdoJobStore 必填）。");

        if (!SupportedProviders.Contains(options.DbProvider))
            throw new NotSupportedException(
                $"不支持的 DbProvider \"{options.DbProvider}\"。" +
                $"支持的值：{string.Join(", ", SupportedProviders)}（大小写不敏感）。");

        // ── 展开 UsePersistentStore ──
        builder.UsePersistentStore(store =>
        {
            // ① 按 DbProvider 选方言（Quartz 4.0 扩展方法）
            switch (options.DbProvider.ToLowerInvariant())
            {
                case "sqlserver":
                    store.UseSqlServer(options.ConnectionString);
                    break;
                case "sqlite":
                    store.UseSqlite(options.ConnectionString);
                    break;
                case "postgresql":
                    store.UsePostgres(options.ConnectionString);
                    break;
            }

            // ② ConfigureStore：TablePrefix + SchemaProvisioning
            store.ConfigureStore(opts =>
            {
                opts.TablePrefix = options.TablePrefix;
            });

            // ③ SchemaProvisioning（Quartz 4.0: ProvisionSchema() = CreateIfMissing 自动建表）
            if (options.AutoCreateSchema)
                store.ProvisionSchema();

            // ③ 集群（可选）
            if (options.Clustering)
            {
                store.UseClustering(cluster =>
                {
                    if (options.ClusterCheckinInterval.HasValue)
                        cluster.CheckinInterval = options.ClusterCheckinInterval.Value;
                });
            }
        });

        // ④ InstanceId（QuartzSchedulerOptions，ConfigureScheduler 设置）
        var instanceId = string.Equals(options.InstanceId, "AUTO", StringComparison.OrdinalIgnoreCase)
            ? $"NODE_{Guid.NewGuid():N}"  // AUTO → 生成唯一 ID（集群多实例必须唯一）
            : options.InstanceId;

        builder.ConfigureScheduler(sched => sched.InstanceId = instanceId);

        return builder;
    }
}
