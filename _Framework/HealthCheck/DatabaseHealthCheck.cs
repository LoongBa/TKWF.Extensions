using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.HealthCheck
{
    /// <summary>
    /// V0.2.0：健康检查探针注册扩展——内置 DB 连通性探针。
    /// </summary>
    public static class HealthCheckBuilderExtensions
    {
        /// <summary>
        /// 注册 DB 连通性探针（V0.2.0）——经 <see cref="IEntityReadOnlyDAC{TEntity}"/> 轻量表级探测，
        /// 红线合规路径（数据访问红线规则 2 仅禁 <see cref="IEntityDAC{TEntity}"/> 读写接口，未禁只读接口；
        /// 接线型扩展无持久化，适用红线不适用边界）。
        /// <para>探针语义：<c>SELECT COUNT(*) FROM {Entity 表}</c>——验证连接串有效 + DB 可达 + 表存在 + 查询可执行
        /// （表级连通性检查，非数据库级 "SELECT 1"）。DB 宕机/连接串错误/表缺失 → 查询异常 → Unhealthy。</para>
        /// <para><b>边界声明</b>：此 <see cref="IEntityReadOnlyDAC{TEntity}"/> 探针合规路径仅限接线型扩展的
        /// 基础设施连通性探测，不作为业务扩展数据访问的先例——业务扩展仍须走 SG1 DataService。</para>
        /// </summary>
        /// <typeparam name="TEntity">消费方任一 SG1 声明实体（<c>[DomainGenerateCode]</c> 自动实现
        /// <see cref="IDomainEntity"/>；编译期门禁——手动 POCO 不满足约束编译失败）。</typeparam>
        /// <param name="builder">健康检查构建器（<c>AddTkfwHealthChecks</c> 返回）。</param>
        /// <param name="name">探针名称（出现在 Detailed 响应 entries 中）。</param>
        /// <param name="failureStatus">探针异常时的报告状态（默认 null → Unhealthy）。</param>
        /// <param name="timeout">探针超时（默认 5 秒——防 DB 连接 hang 致 /health 无限等待）。</param>
        /// <returns>同一 <paramref name="builder"/>（链式）。</returns>
        public static IHealthChecksBuilder AddDatabaseHealthCheck<TEntity>(
            this IHealthChecksBuilder builder,
            string name,
            HealthStatus? failureStatus = null,
            TimeSpan? timeout = null)
            where TEntity : class, IDomainEntity, new()
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("探针名称不能为空。", nameof(name));

            timeout ??= TimeSpan.FromSeconds(5);
            return builder.AddCheck<DatabaseHealthCheck<TEntity>>(
                name, failureStatus, tags: null, timeout: timeout);
        }
    }

    /// <summary>
    /// V0.2.0：DB 连通性探针——经 <see cref="IEntityReadOnlyDAC{TEntity}"/> 表级探测（红线合规路径）。
    /// <para>运行时解析：经 <c>IHealthChecksBuilder.AddCheck&lt;T&gt;()</c> 注册为 HealthCheckRegistration
    /// （非 DI 服务注册）——DefaultHealthCheckService 每次 CheckHealthAsync 创建新 scope，探针从请求 scope
    /// 经 ActivatorUtilities 创建/解析，Scoped <see cref="IEntityReadOnlyDAC{TEntity}"/> 正确注入
    /// （无 captive dependency）。</para>
    /// </summary>
    internal sealed class DatabaseHealthCheck<TEntity>(
        IEntityReadOnlyDAC<TEntity> dac,
        ILogger<DatabaseHealthCheck<TEntity>> logger) : IHealthCheck
        where TEntity : class, IDomainEntity, new()
    {
        /// <summary>执行连通性探测：COUNT(*) 表级查询——连接串有效 + DB 可达 + 表存在 + 查询可执行。</summary>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
        {
            try
            {
                // 轻量表级探测：COUNT(*) 恒可行（空表返回 0 不抛异常）；参与 UoW 连接（Scoped 解析）
                _ = await dac.CountAsync(dac.Query, ct);
                return HealthCheckResult.Healthy();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // OCE 穿透（对齐 FileManagement/BlobStoring 先例——取消不误报 Unhealthy）
            }
            catch (Exception ex)
            {
                // 实体类名作日志标识（非实际表名——FreeSql [Table] 可指定不同表名；探针关注哪个实体探活失败，类名足够）
                logger.LogWarning(ex, "数据库健康检查失败（实体 {Entity}）", typeof(TEntity).Name);
                return HealthCheckResult.Unhealthy("数据库不可达", ex);
            }
        }
    }
}
