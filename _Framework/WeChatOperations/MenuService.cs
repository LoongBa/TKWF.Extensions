using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 公众号菜单门面（两阶段底层能力——设计文档 §3.3.2）——
/// 拉取当前菜单（get_current_selfmenu_info）/ 全量覆盖发布（menu/create）/ 本地基准快照 / 版本备份 / 回滚 / 删除。
/// <para>职责分离（Oracle P1）：发布动作 MenuVersion（记内容 before/after JSON + 发布人/结果——回滚数据源）
/// + MenuPublishLog（记审计 who/when/action=Read/Create/Delete/Test，不含 JSON）同时记录——同一事务保证
/// MenuSnapshot 与 MenuVersion 一致性（Oracle P2）；非发布操作（读取/删除）只记 MenuPublishLog。</para>
/// <para>出站：经 <see cref="WeChatApiClient"/>（平台库——typed client DI 注入，凭证经 IWeChatCredentialResolver
/// 解析——DB 优先 → Options 降级；非 IDomainService 基础设施，可 ctor 注入）。</para>
/// <para>消费方：<c>User.Use&lt;IMenuService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IMenuService : IDomainService
{
    /// <summary>拉取当前生效菜单（get_current_selfmenu_info——拉微信 → 存 MenuSnapshot 本地基准 + 记 PublishLog Read）；
    /// 返回结构化结果（菜单未配置 → null）。
    /// <para>REST：<c>GET /api/wechat-ops/channels/{channelId}/menu</c>。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/channels/{channelId}/menu", RestMethod = "GET")]
    Task<WeChatSelfMenuInfoResult?> GetCurrentAsync(string channelId, CancellationToken ct = default);

    /// <summary>发布/全量覆盖自定义菜单（menu/create raw JSON 透传 → 拉取确认 → 事务：MenuSnapshot 更新 +
    /// MenuVersion 追加 + MenuPublishLog Create）——返回发布后当前菜单结构（确认拉取）。
    /// <para>REST：<c>POST /api/wechat-ops/channels/{channelId}/menu</c>（单复杂参数 <see cref="PublishMenuRequest"/> → [FromBody]——menuJson 走 body 防 URL/日志泄漏）。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/channels/{channelId}/menu", RestMethod = "POST")]
    Task<WeChatSelfMenuInfoResult?> PublishAsync(string channelId, PublishMenuRequest request, CancellationToken ct = default);

    /// <summary>本地基准快照（MenuSnapshot——最近一次发布/拉取确认的完整 JSON；无 → null）。
    /// <para><c>ExposeRest=false</c>：快照原子读取为内部能力（click key 收集数据源），不生成 REST 端点——对齐既有 15 端点映射。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<string?> GetSnapshotAsync(string channelId, CancellationToken ct = default);

    /// <summary>版本历史（MenuVersion——按 VersionNo 升序；回滚数据源列表）。
    /// <para>REST：<c>GET /api/wechat-ops/channels/{channelId}/menu/versions</c>。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/channels/{channelId}/menu/versions", RestMethod = "GET")]
    Task<IReadOnlyList<MenuVersionEntity>> ListVersionsAsync(string channelId, CancellationToken ct = default);

    /// <summary>回滚到指定版本（加载该版本 AfterJson 重新发布 → 追加新版本 + 审计——同 PublishAsync 全链）。
    /// <para>REST：<c>POST /api/wechat-ops/channels/{channelId}/menu/rollback</c>（单复杂参数 <see cref="RollbackMenuRequest"/> → [FromBody]）。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/channels/{channelId}/menu/rollback", RestMethod = "POST")]
    Task<WeChatSelfMenuInfoResult?> RollbackAsync(string channelId, RollbackMenuRequest request, CancellationToken ct = default);

    /// <summary>删除当前自定义菜单（menu/delete + PublishLog Delete 审计——本地快照/版本保留可追溯）。
    /// <para><c>ExposeRest=false</c>：删除为高操作权限能力，对齐既有 15 端点映射（现无删除端点）不新增暴露面。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(ExposeRest = false)]
    Task DeleteAsync(string channelId, CancellationToken ct = default);

    /// <summary>菜单树 click 类型按钮标识列表（递归收集 click 按钮 Key——DMP-Lite 5.4 自动生成映射行；
    /// 数据源 = 本地基准快照 JSON（<see cref="MenuSnapshotEntity.MenuJson"/>——发布/拉取确认的结构化 poco 序列化）；
    /// 快照空/解析失败 → 空列表）。
    /// <para><c>ExposeRest=false</c>：click key 为菜单映射管理内部能力（映射页数据源），不生成 REST 端点——对齐既有 15 端点映射。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<IReadOnlyList<string>> GetClickKeysAsync(string channelId, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
[GenerateController]
public sealed class MenuService : DomainServiceBase, IMenuService
{
    private readonly WeChatApiClient _apiClient;
    private readonly ITransactionManager _transactionManager;

    private WeChatChannelConfigEntityDataService? _cfgDs;
    private WeChatChannelConfigEntityDataService CfgDs => _cfgDs ??= User.Use<WeChatChannelConfigEntityDataService>();
    private MenuSnapshotEntityDataService? _snapDs;
    private MenuSnapshotEntityDataService SnapDs => _snapDs ??= User.Use<MenuSnapshotEntityDataService>();
    private MenuVersionEntityDataService? _verDs;
    private MenuVersionEntityDataService VerDs => _verDs ??= User.Use<MenuVersionEntityDataService>();
    private MenuPublishLogEntityDataService? _logDs;
    private MenuPublishLogEntityDataService LogDs => _logDs ??= User.Use<MenuPublishLogEntityDataService>();

    public MenuService(IDomainUser user, WeChatApiClient apiClient, ITransactionManager transactionManager)
        : base(user)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
    }

    /// <inheritdoc />
    public async Task<WeChatSelfMenuInfoResult?> GetCurrentAsync(string channelId, CancellationToken ct = default)
    {
        var cfg = await ResolveConfigAsync(channelId, ct);
        var token = await _apiClient.GetAccessTokenAsync(cfg.AppId, "mp", ct);
        var result = await _apiClient.GetCurrentSelfMenuInfoAsync(token, ct);

        // 本地基准快照（存在 → 更新；不存在 → 创建）+ PublishLog Read 审计
        await using var scope = await BeginTxAsync(ct);
        await UpsertSnapshotAsync(channelId, result, ct);
        await LogDs.EntityCreateAsync(new MenuPublishLogEntity
        {
            ChannelId = channelId,
            Operator = Operator(),
            OperateAt = DateTime.UtcNow,
            Action = 1, // Read
            Remark = "get_current_selfmenu_info 拉取确认",
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct);
        await scope.CommitAsync(ct);
        return result;
    }

    /// <inheritdoc />
    public async Task<WeChatSelfMenuInfoResult?> PublishAsync(string channelId, PublishMenuRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MenuJson);
        var menuJson = request.MenuJson;
        var cfg = await ResolveConfigAsync(channelId, ct);
        var token = await _apiClient.GetAccessTokenAsync(cfg.AppId, "mp", ct);
        var beforeJson = await GetSnapshotAsync(channelId, ct);

        await _apiClient.CreateMenuAsync(token, menuJson, ct);
        var confirmed = await _apiClient.GetCurrentSelfMenuInfoAsync(token, ct); // 拉取确认（发布成功语义）

        // 事务：MenuSnapshot 更新 + MenuVersion 追加（VersionNo 递增）+ PublishLog Create 审计
        await using var scope = await BeginTxAsync(ct);
        await UpsertSnapshotAsync(channelId, confirmed, ct);
        var versionNo = (await VerDs.EntitySelectAsync(v => v.ChannelId == channelId, 0, int.MaxValue,
                q => q.OrderByDescending(v => v.VersionNo), ct).ConfigureAwait(false)).FirstOrDefault()?.VersionNo ?? 0;
        await VerDs.EntityCreateAsync(new MenuVersionEntity
        {
            ChannelId = channelId,
            VersionNo = versionNo + 1,
            BeforeJson = beforeJson,
            AfterJson = menuJson,          // 本版本最终结构（raw JSON 透传原样——回滚数据源）
            Operator = Operator(),
            PublishedAt = DateTime.UtcNow,
            Result = 1,                    // Success
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct);
        await LogDs.EntityCreateAsync(new MenuPublishLogEntity
        {
            ChannelId = channelId,
            Operator = Operator(),
            OperateAt = DateTime.UtcNow,
            Action = 2,                    // Create（发布）
            Remark = $"menu/create 发布（v{versionNo + 1}）",
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct);
        await scope.CommitAsync(ct);
        return confirmed;
    }

    /// <inheritdoc />
    public async Task<string?> GetSnapshotAsync(string channelId, CancellationToken ct = default)
    {
        var snap = await SnapDs.EntityGetAsync(s => s.ChannelId == channelId, ct);
        return snap is null or { MenuJson: "" } ? null : snap.MenuJson;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuVersionEntity>> ListVersionsAsync(string channelId, CancellationToken ct = default)
        => await VerDs.EntitySelectAsync(v => v.ChannelId == channelId, 0, 1000,
            q => q.OrderBy(v => v.VersionNo), ct);

    /// <inheritdoc />
    public async Task<WeChatSelfMenuInfoResult?> RollbackAsync(string channelId, RollbackMenuRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.VersionId <= 0) throw new ArgumentException("versionId 必填", nameof(request));
        var version = await VerDs.EntityGetAsync(v => v.Id == request.VersionId, ct);
        if (version is null)
            throw new InvalidOperationException($"菜单版本不存在：Id={request.VersionId}");
        if (version.ChannelId != channelId)
            throw new InvalidOperationException($"菜单版本不属于该公众号：版本属 ChannelId={version.ChannelId}，请求属 {channelId}");
        return await PublishAsync(version.ChannelId, new PublishMenuRequest(version.AfterJson), ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string channelId, CancellationToken ct = default)
    {
        var cfg = await ResolveConfigAsync(channelId, ct);
        var token = await _apiClient.GetAccessTokenAsync(cfg.AppId, "mp", ct);
        await _apiClient.DeleteMenuAsync(token, ct);

        await using var scope = await BeginTxAsync(ct);
        // 本地快照置空（微信侧菜单已删——本地基准同步）
        var snap = await SnapDs.EntityGetAsync(s => s.ChannelId == channelId, ct);
        if (snap is not null)
        {
            snap.MenuJson = "";
            snap.UpdatedAt = DateTime.UtcNow;
            snap.UpdateTime = DateTime.UtcNow;
            await SnapDs.EntityUpdateAsync(snap, ct);
        }
        await LogDs.EntityCreateAsync(new MenuPublishLogEntity
        {
            ChannelId = channelId,
            Operator = Operator(),
            OperateAt = DateTime.UtcNow,
            Action = 3,                    // Delete
            Remark = "menu/delete 删除自定义菜单",
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct);
        await scope.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetClickKeysAsync(string channelId, CancellationToken ct = default)
    {
        var menuJson = await GetSnapshotAsync(channelId, ct);
        if (string.IsNullOrWhiteSpace(menuJson)) return Array.Empty<string>();

        WeChatSelfMenuInfoResult? result;
        try
        {
            result = JsonSerializer.Deserialize<WeChatSelfMenuInfoResult>(menuJson);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();   // 快照损坏 → 空列表（对齐"快照空/解析失败 → 空列表"语义）
        }
        if (result?.SelfMenuInfo?.Button is not { Length: > 0 } buttons)
            return Array.Empty<string>();

        var keys = new List<string>();
        CollectClickKeys(buttons, keys);
        return keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>递归收集 click 类型按钮 Key（SubButton 子菜单递归——菜单树全深度；非 click 类型跳过）。</summary>
    private static void CollectClickKeys(WeChatMenuButton[] buttons, List<string> keys)
    {
        foreach (var button in buttons)
        {
            if (button is null) continue;
            if (string.Equals(button.Type, "click", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(button.Key))
                keys.Add(button.Key);
            if (button.SubButton is { Length: > 0 })
                CollectClickKeys(button.SubButton, keys);
        }
    }

    // ─── 内部（框架红线合规：DataService 经 User.Use 懒加载，零 ORM） ───

    private Task<ITransactionScope> BeginTxAsync(CancellationToken ct)
        => _transactionManager.BeginAsync(ct: ct);

    private async Task UpsertSnapshotAsync(string channelId, WeChatSelfMenuInfoResult? current, CancellationToken ct)
    {
        var snap = await SnapDs.EntityGetAsync(s => s.ChannelId == channelId, ct);
        var menuJson = SerializeForSnapshot(current);
        if (snap is null)
        {
            await SnapDs.EntityCreateAsync(new MenuSnapshotEntity
            {
                ChannelId = channelId,
                MenuJson = menuJson,
                UpdatedAt = DateTime.UtcNow,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow,
            }, ct);
        }
        else
        {
            snap.MenuJson = menuJson;
            snap.UpdatedAt = DateTime.UtcNow;
            snap.UpdateTime = DateTime.UtcNow;
            await SnapDs.EntityUpdateAsync(snap, ct);
        }
    }

    /// <summary>快照承载（结构化 poco → JSON——本地权威基线；无菜单（is_menu_open=0/null）→ 空快照）。</summary>
    private static string SerializeForSnapshot(WeChatSelfMenuInfoResult? current)
        => current is null ? "" : System.Text.Json.JsonSerializer.Serialize(current);

    private async Task<WeChatChannelConfigEntity> ResolveConfigAsync(string channelId, CancellationToken ct)
    {
        var cfg = await CfgDs.EntityGetAsync(c => c.ChannelId == channelId, ct);
        if (cfg is null)
            throw new InvalidOperationException($"公众号配置不存在：ChannelId={channelId}（请先经 IChannelConfigService.SaveChannelAsync 配置）");
        return cfg;
    }

    private string Operator()
        => User.IsAuthenticated && !string.IsNullOrWhiteSpace(User.UserId) ? User.UserId : "system";
}