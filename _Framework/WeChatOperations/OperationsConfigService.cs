using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 运营配置管理门面（D03 1.10——按域多表 O4-4：每域单行配置表 Id=1，独立版本/并发写/schema 演进）。
/// <para>乐观并发（R7）：写请求带 <see cref="SaveOpsConfigTicketRequest.ExpectedVersion"/>——非空时 CAS 比对库中
/// <c>Version</c>（<c>EntityUpdateWhereAsync</c> 单语句原子更新），不匹配 → <c>OPS_CONFIG_VERSION_CONFLICT</c>
/// （HTTP 400——框架 DomainException 默认映射；如需 409 需框架扩展 ErrorCode→Status 映射）；空 = 强制覆盖（初始化/运维兜底）。</para>
/// <para>安全：<c>WeChatOperations.Query</c>/<c>WeChatOperations.Manage</c> 权限门控。
/// ⚠️ <b>审计未实现</b>（审核 P2-11 如实标注）——原计划写路径记 SecurityLog <c>OpsConfigUpdate</c> 事件，
/// 本期仅落 <c>UpdatedAt</c>/<c>UpdatedBy</c> 行内痕迹；跨系统审计待后续迭代（SecurityLog 事件枚举扩展面）。</para>
/// </summary>
public interface IOperationsConfigService : IDomainService
{
    /// <summary>图文回复域配置（<c>GET /api/wechat-ops/ops-config/article-reply</c>；无行 → null）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/ops-config/article-reply", RestMethod = "GET")]
    Task<ArticleReplyOpsConfigEntityDto?> GetArticleReplyAsync(CancellationToken ct = default);

    /// <summary>保存图文回复域配置（<c>PUT /api/wechat-ops/ops-config/article-reply</c>；乐观并发 R7）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/ops-config/article-reply", RestMethod = "PUT")]
    Task<long> SaveArticleReplyAsync(SaveOpsConfigArticleReplyRequest request, CancellationToken ct = default);

    /// <summary>回复文案域配置（S3 停用提示 + S5 兜底文案）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/ops-config/reply-text", RestMethod = "GET")]
    Task<ReplyTextOpsConfigEntityDto?> GetReplyTextAsync(CancellationToken ct = default);

    /// <summary>保存回复文案域配置。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/ops-config/reply-text", RestMethod = "PUT")]
    Task<long> SaveReplyTextAsync(SaveOpsConfigReplyTextRequest request, CancellationToken ct = default);

    /// <summary>降级行为域配置（S2 未知通道提示开关）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/ops-config/degrade", RestMethod = "GET")]
    Task<DegradeOpsConfigEntityDto?> GetDegradeAsync(CancellationToken ct = default);

    /// <summary>保存降级行为域配置。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/ops-config/degrade", RestMethod = "PUT")]
    Task<long> SaveDegradeAsync(SaveOpsConfigDegradeRequest request, CancellationToken ct = default);

    /// <summary>弱身份策略域配置（能力契约在 AuthCenter，策略经本运营配置面承载）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/ops-config/weak-identity", RestMethod = "GET")]
    Task<WeakIdentityOpsConfigEntityDto?> GetWeakIdentityAsync(CancellationToken ct = default);

    /// <summary>保存弱身份策略域配置。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/ops-config/weak-identity", RestMethod = "PUT")]
    Task<long> SaveWeakIdentityAsync(SaveOpsConfigWeakIdentityRequest request, CancellationToken ct = default);

    /// <summary>票据域配置（Mode/TtlSeconds——签票行为策略）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/ops-config/ticket", RestMethod = "GET")]
    Task<TicketOpsConfigEntityDto?> GetTicketAsync(CancellationToken ct = default);

    /// <summary>保存票据域配置。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/ops-config/ticket", RestMethod = "PUT")]
    Task<long> SaveTicketAsync(SaveOpsConfigTicketRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
[GenerateController]
public sealed class OperationsConfigService : DomainServiceBase, IOperationsConfigService
{
    private const string VersionConflictError = "OPS_CONFIG_VERSION_CONFLICT";
    private readonly ILogger<OperationsConfigService> _logger;

    private ArticleReplyOpsConfigEntityDataService? _articleReplyDs;
    private ArticleReplyOpsConfigEntityDataService ArticleReplyDs
        => _articleReplyDs ??= User.Use<ArticleReplyOpsConfigEntityDataService>();

    private ReplyTextOpsConfigEntityDataService? _replyTextDs;
    private ReplyTextOpsConfigEntityDataService ReplyTextDs
        => _replyTextDs ??= User.Use<ReplyTextOpsConfigEntityDataService>();

    private DegradeOpsConfigEntityDataService? _degradeDs;
    private DegradeOpsConfigEntityDataService DegradeDs
        => _degradeDs ??= User.Use<DegradeOpsConfigEntityDataService>();

    private WeakIdentityOpsConfigEntityDataService? _weakIdentityDs;
    private WeakIdentityOpsConfigEntityDataService WeakIdentityDs
        => _weakIdentityDs ??= User.Use<WeakIdentityOpsConfigEntityDataService>();

    private TicketOpsConfigEntityDataService? _ticketDs;
    private TicketOpsConfigEntityDataService TicketDs
        => _ticketDs ??= User.Use<TicketOpsConfigEntityDataService>();

    public OperationsConfigService(IDomainUser user, ILogger<OperationsConfigService>? logger = null) : base(user)
    {
        _logger = logger ?? NullLogger<OperationsConfigService>.Instance;
    }

    /// <inheritdoc />
    public async Task<ArticleReplyOpsConfigEntityDto?> GetArticleReplyAsync(CancellationToken ct = default)
    {
        var entity = await ArticleReplyDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null ? null : ArticleReplyOpsConfigEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<long> SaveArticleReplyAsync(SaveOpsConfigArticleReplyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = TruncateToSecond(DateTime.UtcNow);
        var existing = await ArticleReplyDs.EntityGetAsync(e => e.Id == 1, ct);
        if (existing is null)
        {
            existing = new ArticleReplyOpsConfigEntity { Id = 1, UrlTemplate = request.UrlTemplate, Version = 1, UpdatedAt = now, UpdatedBy = Operator };
            await ArticleReplyDs.EntityCreateAsync(existing, ct);
            return existing.Version;
        }

        // CAS 原子更新（ADR89 EntityUpdateWhereAsync——单语句 WHERE 版本比对 + Version 原子自增，并发下不丢写不覆盖）
        var affected = await ArticleReplyDs.EntityUpdateWhereAsync(
            e => e.Id == 1 && (request.ExpectedVersion == null || e.Version == request.ExpectedVersion),
            e => new ArticleReplyOpsConfigEntity
            {
                UrlTemplate = request.UrlTemplate,
                Version = e.Version + 1,
                UpdatedAt = now,
                UpdatedBy = Operator,
            }, ct);
        AssertCasAffected(affected, request.ExpectedVersion, existing.Version);
        return request.ExpectedVersion == null ? existing.Version + 1 : request.ExpectedVersion.Value + 1;
    }

    /// <inheritdoc />
    public async Task<ReplyTextOpsConfigEntityDto?> GetReplyTextAsync(CancellationToken ct = default)
    {
        var entity = await ReplyTextDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null ? null : ReplyTextOpsConfigEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<long> SaveReplyTextAsync(SaveOpsConfigReplyTextRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = TruncateToSecond(DateTime.UtcNow);
        var existing = await ReplyTextDs.EntityGetAsync(e => e.Id == 1, ct);
        if (existing is null)
        {
            existing = new ReplyTextOpsConfigEntity
            {
                Id = 1,
                DisabledChannelReplyTemplate = request.DisabledChannelReplyTemplate,
                DefaultReplyText = request.DefaultReplyText,
                Version = 1, UpdatedAt = now, UpdatedBy = Operator,
            };
            await ReplyTextDs.EntityCreateAsync(existing, ct);
            return existing.Version;
        }

        var affected = await ReplyTextDs.EntityUpdateWhereAsync(
            e => e.Id == 1 && (request.ExpectedVersion == null || e.Version == request.ExpectedVersion),
            e => new ReplyTextOpsConfigEntity
            {
                DisabledChannelReplyTemplate = request.DisabledChannelReplyTemplate,
                DefaultReplyText = request.DefaultReplyText,
                Version = e.Version + 1,
                UpdatedAt = now,
                UpdatedBy = Operator,
            }, ct);
        AssertCasAffected(affected, request.ExpectedVersion, existing.Version);
        return request.ExpectedVersion == null ? existing.Version + 1 : request.ExpectedVersion.Value + 1;
    }

    /// <inheritdoc />
    public async Task<DegradeOpsConfigEntityDto?> GetDegradeAsync(CancellationToken ct = default)
    {
        var entity = await DegradeDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null ? null : DegradeOpsConfigEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<long> SaveDegradeAsync(SaveOpsConfigDegradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = TruncateToSecond(DateTime.UtcNow);
        var existing = await DegradeDs.EntityGetAsync(e => e.Id == 1, ct);
        if (existing is null)
        {
            existing = new DegradeOpsConfigEntity
            {
                Id = 1, UnknownChannelReplyEnabled = request.UnknownChannelReplyEnabled,
                Version = 1, UpdatedAt = now, UpdatedBy = Operator,
            };
            await DegradeDs.EntityCreateAsync(existing, ct);
            return existing.Version;
        }

        var affected = await DegradeDs.EntityUpdateWhereAsync(
            e => e.Id == 1 && (request.ExpectedVersion == null || e.Version == request.ExpectedVersion),
            e => new DegradeOpsConfigEntity
            {
                UnknownChannelReplyEnabled = request.UnknownChannelReplyEnabled,
                Version = e.Version + 1,
                UpdatedAt = now,
                UpdatedBy = Operator,
            }, ct);
        AssertCasAffected(affected, request.ExpectedVersion, existing.Version);
        return request.ExpectedVersion == null ? existing.Version + 1 : request.ExpectedVersion.Value + 1;
    }

    /// <inheritdoc />
    public async Task<WeakIdentityOpsConfigEntityDto?> GetWeakIdentityAsync(CancellationToken ct = default)
    {
        var entity = await WeakIdentityDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null ? null : WeakIdentityOpsConfigEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<long> SaveWeakIdentityAsync(SaveOpsConfigWeakIdentityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = TruncateToSecond(DateTime.UtcNow);
        var existing = await WeakIdentityDs.EntityGetAsync(e => e.Id == 1, ct);
        if (existing is null)
        {
            existing = new WeakIdentityOpsConfigEntity
            {
                Id = 1,
                Enabled = request.Enabled,
                InactiveDays = request.InactiveDays,
                Action = request.Action,
                RecoveryWindowDays = request.RecoveryWindowDays,
                ExcludeBound = request.ExcludeBound,
                Version = 1, UpdatedAt = now, UpdatedBy = Operator,
            };
            await WeakIdentityDs.EntityCreateAsync(existing, ct);
            return existing.Version;
        }

        var affected = await WeakIdentityDs.EntityUpdateWhereAsync(
            e => e.Id == 1 && (request.ExpectedVersion == null || e.Version == request.ExpectedVersion),
            e => new WeakIdentityOpsConfigEntity
            {
                Enabled = request.Enabled,
                InactiveDays = request.InactiveDays,
                Action = request.Action,
                RecoveryWindowDays = request.RecoveryWindowDays,
                ExcludeBound = request.ExcludeBound,
                Version = e.Version + 1,
                UpdatedAt = now,
                UpdatedBy = Operator,
            }, ct);
        AssertCasAffected(affected, request.ExpectedVersion, existing.Version);
        return request.ExpectedVersion == null ? existing.Version + 1 : request.ExpectedVersion.Value + 1;
    }

    /// <inheritdoc />
    public async Task<TicketOpsConfigEntityDto?> GetTicketAsync(CancellationToken ct = default)
    {
        var entity = await TicketDs.EntityGetAsync(e => e.Id == 1, ct);
        return entity is null ? null : TicketOpsConfigEntityDto.FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<long> SaveTicketAsync(SaveOpsConfigTicketRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = TruncateToSecond(DateTime.UtcNow);
        var existing = await TicketDs.EntityGetAsync(e => e.Id == 1, ct);
        if (existing is null)
        {
            existing = new TicketOpsConfigEntity
            {
                Id = 1, Mode = request.Mode, TtlSeconds = request.TtlSeconds,
                Version = 1, UpdatedAt = now, UpdatedBy = Operator,
            };
            await TicketDs.EntityCreateAsync(existing, ct);
            return existing.Version;
        }

        var affected = await TicketDs.EntityUpdateWhereAsync(
            e => e.Id == 1 && (request.ExpectedVersion == null || e.Version == request.ExpectedVersion),
            e => new TicketOpsConfigEntity
            {
                Mode = request.Mode,
                TtlSeconds = request.TtlSeconds,
                Version = e.Version + 1,
                UpdatedAt = now,
                UpdatedBy = Operator,
            }, ct);
        AssertCasAffected(affected, request.ExpectedVersion, existing.Version);
        return request.ExpectedVersion == null ? existing.Version + 1 : request.ExpectedVersion.Value + 1;
    }

    /// <summary>
    /// CAS 影响行数校验（R7 乐观并发——EntityUpdateWhereAsync 命中 0 行 = ExpectedVersion 不匹配/行被并发改写
    /// → OPS_CONFIG_VERSION_CONFLICT）。<paramref name="current"/> 为读时版本（日志参考，非判定依据——判定在 CAS WHERE）。
    /// </summary>
    private static void AssertCasAffected(int affected, long? expected, long current)
    {
        if (affected == 0)
        {
            throw new DomainException(
                $"运营配置版本冲突：期望 {expected?.ToString() ?? "任意"}，读时 {current}（请刷新后重试）")
            {
                ErrorCode = VersionConflictError,
            };
        }
    }

    /// <summary>
    /// 审计时间秒级截断——框架 <c>EntityUpdateWhereAsync</c>（CAS SET 表达式）对 DateTime 参数按秒精度落库，
    /// 而 <c>EntityCreateAsync</c>（SetSource）保留毫秒；两条路径统一秒级避免 UpdatedAt 读回倒退
    /// （框架缺陷已登记：UpdateWhere 参数化 DateTime 精度截断）。
    /// </summary>
    private static DateTime TruncateToSecond(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    /// <summary>变更操作人（UId——未认证/系统上下文为空；运营写路径来自已认证管理用户）。</summary>
    private string? Operator => User.IsAuthenticated ? User.UserId?.ToString() : null;
}

/// <summary>图文回复域写请求（<paramref name="ExpectedVersion"/> = 乐观并发令牌，null = 强制覆盖）。</summary>
public sealed record SaveOpsConfigArticleReplyRequest(string? UrlTemplate, long? ExpectedVersion);

/// <summary>回复文案域写请求（同前——ExpectedVersion 乐观并发令牌）。</summary>
public sealed record SaveOpsConfigReplyTextRequest(string? DisabledChannelReplyTemplate, string? DefaultReplyText, long? ExpectedVersion);

/// <summary>降级行为域写请求（同前）。</summary>
public sealed record SaveOpsConfigDegradeRequest(bool? UnknownChannelReplyEnabled, long? ExpectedVersion);

/// <summary>弱身份策略域写请求（同前）。</summary>
public sealed record SaveOpsConfigWeakIdentityRequest(
    bool? Enabled, int? InactiveDays, string? Action, int? RecoveryWindowDays, bool? ExcludeBound, long? ExpectedVersion);

/// <summary>票据域写请求（同前）。</summary>
public sealed record SaveOpsConfigTicketRequest(string? Mode, int? TtlSeconds, long? ExpectedVersion);