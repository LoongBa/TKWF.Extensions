using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 登录历史与异常检测查询服务实现（internal sealed，V0.3.0）——消费 SecurityLog 扩展查询 API。
    /// <para><b>数据访问红线合规（2026-09-07 用户裁定）</b>：本服务<b>只依赖 <see cref="IServiceProvider"/>
    /// + <see cref="ILogger{TCategoryName}"/></b>，经 <c>sp.GetService&lt;ISecurityLogQueryService&gt;()</c> /
    /// <c>sp.GetService&lt;ISecurityLogAnalyticsService&gt;()</c> 延迟解析——绝不注入 IFreeSql / IEntityDAC、
    /// 绝不经 Account 的 DataService 查 SecurityLog 表（跨扩展走契约服务，SecurityLog 是登录历史唯一数据源）。</para>
    /// <para><b>C1 模式（对齐 NotificationPublisher）</b>：SecurityLog 扩展可能未被消费方启用（发现不自动启用）——
    /// 构造不解析契约，方法内延迟解析；未注册 → 明确抛 <see cref="InvalidOperationException"/>（提示启用）。
    /// 查询/聚合异常 → 委托方已静默（本服务外层兜底 Warning + 空结果，不抛异常）。</para>
    /// </summary>
    internal sealed class LoginHistoryService : ILoginHistoryService
    {
        /// <summary>分页默认值（对齐 SecurityLog：默认 50，上限 200 防滥用）。</summary>
        private const int DefaultTake = 50;

        /// <summary>分页上限。</summary>
        private const int MaxTake = 200;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LoginHistoryService> _logger;

        public LoginHistoryService(IServiceProvider serviceProvider, ILogger<LoginHistoryService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<LoginHistoryPagedResult> GetLoginHistoryAsync(
            LoginHistoryQueryInput input, CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            // SecurityLog 未启用 → 明确异常（C1 模式，非静默）
            var queryService = GetRequiredQueryService();

            try
            {
                var securityInput = new SecurityLogQueryInput
                {
                    EventType = SecurityLogEventTypes.Login,   // 登录历史 = EventType "Login"（唯一数据源过滤）
                    UserName = input.UserName,
                    IpAddress = input.IpAddress,
                    Result = input.Result,
                    FromUtc = input.FromUtc,
                    ToUtc = input.ToUtc,
                    Skip = Math.Max(0, input.Skip),
                    Take = Math.Clamp(input.Take <= 0 ? DefaultTake : input.Take, 1, MaxTake),
                };

                var result = await queryService.GetListAsync(securityInput, ct);
                var items = result.Items.Select(MapItem).ToList();
                return new LoginHistoryPagedResult(result.Total, items);
            }
            catch (Exception ex)
            {
                // 委托方（SecurityLog 实现）已内部静默；此处兜底消费方自注册的非常规实现
                _logger.LogWarning(ex, "登录历史查询失败: GetLoginHistoryAsync");
                return new LoginHistoryPagedResult(0, Array.Empty<LoginHistoryItemDto>());
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<LoginFailureStat>> GetTopFailedUsersAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default)
        {
            var analytics = GetRequiredAnalytics();

            try
            {
                var stats = await analytics.GetTopFailedUsersAsync(topN, window, ct);
                return stats.Select(s => new LoginFailureStat(s.Dimension, s.Count)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "登录异常检测失败: GetTopFailedUsersAsync");
                return Array.Empty<LoginFailureStat>();
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<LoginFailureStat>> GetTopFailedIpsAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default)
        {
            var analytics = GetRequiredAnalytics();

            try
            {
                var stats = await analytics.GetTopFailedIpsAsync(topN, window, ct);
                return stats.Select(s => new LoginFailureStat(s.Dimension, s.Count)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "登录异常检测失败: GetTopFailedIpsAsync");
                return Array.Empty<LoginFailureStat>();
            }
        }

        /// <summary>延迟解析查询契约——SecurityLog 未启用抛明确异常（对齐 NotificationPublisher C1 模式）。</summary>
        private ISecurityLogQueryService GetRequiredQueryService()
            => _serviceProvider.GetService<ISecurityLogQueryService>()
               ?? throw new InvalidOperationException(
                   "登录历史查询需要 SecurityLog 扩展（ISecurityLogQueryService 未注册）——须启用 SecurityLog 扩展：" +
                   "[TKWFEnabledExtension(typeof(SecurityLogExtensionInitializer<>))]");

        /// <summary>延迟解析分析契约——SecurityLog 未启用抛明确异常（对齐 NotificationPublisher C1 模式）。</summary>
        private ISecurityLogAnalyticsService GetRequiredAnalytics()
            => _serviceProvider.GetService<ISecurityLogAnalyticsService>()
               ?? throw new InvalidOperationException(
                   "登录异常检测需要 SecurityLog 扩展（ISecurityLogAnalyticsService 未注册）——须启用 SecurityLog 扩展：" +
                   "[TKWFEnabledExtension(typeof(SecurityLogExtensionInitializer<>))]");

        /// <summary>从 SecurityLog 列表 DTO 投影（UserAgent 列表投影恒 null——列表 DTO 不含该字段，D5 安全决策）。</summary>
        private static LoginHistoryItemDto MapItem(SecurityLogListItemDto dto)
            => new LoginHistoryItemDto
            {
                Id = dto.Id,
                UserName = dto.UserName,
                UserId = dto.UserId,
                IpAddress = dto.IpAddress,
                UserAgent = null,
                Result = dto.Result,
                CreateTime = dto.CreateTime,
            };
    }
}
