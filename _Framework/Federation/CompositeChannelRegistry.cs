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

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道注册表组合门面（多通道联邦 v0.3.0——运行时唯一暴露实现，Oracle M3 修订）。
/// <para>组合语义：**DB 命中优先 → 静态回退**（用户裁定读取语义）——Phase 2 起 <see cref="DbChannelRegistry"/>
/// 注入生效（<c>SsoChannelRegistryEntity</c> DB 动态权威层）；<see cref="StaticChannelRegistry"/> 为降级回退
/// （未落库通道仍可用静态 Options 配置——存量零破坏）。</para>
/// <para>职责边界：只做"组合"不读数据——<see cref="StaticChannelRegistry"/> 读静态 Options（<b>普通 DI 服务</b>，
/// 无 IDomainUser 依赖，经 <see cref="IServiceProvider"/> C1 解析——不走 <c>User.Use</c>：NoAop 路径会把当前 user
/// 作为额外构造参数传入，仅适用于 DomainServiceBase 派生类）；<see cref="DbChannelRegistry"/> 读 DB
/// （<see cref="DomainServiceBase"/> 派生——经基类 <c>User.Use&lt;DbChannelRegistry&gt;()</c> 帧内解析，
/// IDomainUser 框架内供给）；跨实现解耦归本组合单点。本实现继承 <see cref="DomainServiceBase"/>
/// （IChannelRegistry : IDomainService——tkwf-extension §4.3 铁律）。</para>
/// <para>降级容错（无 Db 注册时回退 Static）——<see cref="DbChannelRegistry"/> 不可解析（未注册/构造失败）或
/// DB 读取异常时 catch 回退静态层，保证未接线 DB 动态权威层的消费方零破坏（单测覆盖）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IChannelRegistry, CompositeChannelRegistry&gt;()</c> 经
/// FederationInitializer——消费方统一 <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析（门面标准形态）。</para>
/// </summary>
[DiContractIgnore]
public sealed class CompositeChannelRegistry : DomainServiceBase, IChannelRegistry
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CompositeChannelRegistry> _logger;
    private DbChannelRegistry? _db;

    /// <summary>构造——IDomainUser 帧内供给；StaticChannelRegistry 经 IServiceProvider C1 解析（普通 DI 服务）；Db 经基类 User 帧内懒加载。</summary>
    public CompositeChannelRegistry(IDomainUser user, IServiceProvider sp, ILogger<CompositeChannelRegistry> logger)
        : base(user)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>DB 命中优先 → Static 回退；DB 不可用（未注册/异常）→ 静默降级静态层。</remarks>
    public async Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
    {
        try
        {
            var dbCfg = await Db.GetAsync(channelId, ct);
            if (dbCfg != null) return dbCfg;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DB 通道注册表不可用（GetAsync 降级静态层）：{Ex}", ex.Message);
        }
        return await Static.GetAsync(channelId, ct);
    }

    /// <inheritdoc />
    /// <remarks>统一解析入口（方案 §3.7）——DB 命中优先（Db 实现先 alias → ChannelId）→ 未中回退 Static
    /// （同优先序）；DB 不可用（未注册/异常）→ 静默降级静态层（对齐 <see cref="GetAsync"/> 降级模式）。
    /// <b>本方法仅对外入口解析用——内部消费一律 <see cref="GetAsync"/>（ChannelId）</b>。</remarks>
    public async Task<ChannelConfig?> GetByAliasOrIdAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var dbCfg = await Db.GetByAliasOrIdAsync(key, ct);
            if (dbCfg != null) return dbCfg;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DB 通道注册表不可用（GetByAliasOrIdAsync 降级静态层）：{Ex}", ex.Message);
        }
        return await Static.GetByAliasOrIdAsync(key, ct);
    }

    /// <inheritdoc />
    /// <remarks>DB 全部 + Static 全部合并（DB 命中优先——同 ChannelId 去重保留 DB 值；DB 不可用 → 仅静态）。</remarks>
    public async Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var merged = new List<ChannelConfig>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var cfg in await Db.GetAllAsync(ct))
            {
                // DB 命中优先：记录 channelId，后续静态同 id 跳过
                if (!string.IsNullOrEmpty(cfg.ChannelId) && seen.Add(cfg.ChannelId))
                    merged.Add(cfg);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DB 通道注册表不可用（GetAllAsync 降级静态层）：{Ex}", ex.Message);
            merged.Clear();
            seen.Clear();
        }

        foreach (var cfg in await Static.GetAllAsync(ct))
        {
            if (!string.IsNullOrEmpty(cfg.ChannelId) && !seen.Contains(cfg.ChannelId))
                merged.Add(cfg);
        }
        return merged;
    }

    /// <inheritdoc />
    /// <remarks>DB 默认通道优先 → Static 回退（DB 无 IsDefault 标记行时回退静态默认选区）。</remarks>
    public async Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        try
        {
            var dbDefault = await Db.GetDefaultAsync(ct);
            if (dbDefault != null) return dbDefault;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DB 通道注册表不可用（GetDefaultAsync 降级静态层）：{Ex}", ex.Message);
        }
        return await Static.GetDefaultAsync(ct);
    }

    private DbChannelRegistry Db => _db ??= User.Use<DbChannelRegistry>();

    private StaticChannelRegistry Static => _sp.GetRequiredService<StaticChannelRegistry>();
}