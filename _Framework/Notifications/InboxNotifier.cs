using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 收件箱通知器——owns UserNotification 行写入（C5 修订）。
    /// <para>投递幂等：同 UserId+NotificationId 已存在则跳过（重复发布不产生重复 inbox 行）。</para>
    /// <para>M1 修订：不吞异常——Inbox 通道是发布事务的一部分（C4），写入失败必须传播
    /// 触发发布器回滚。经 <see cref="UserNotificationEntityDataService"/>（SG1 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）。</para>
    /// <para>V4.10.55（ADR92，T3 闭环）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
    /// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
    /// 注册改 <c>TryAddEnumerableConstructible&lt;INotificationNotifier, InboxNotifier&gt;</c>（集合版守卫工厂——
    /// 帧内（NotificationPublisher 经 User.Use&lt;INotificationPublisher&gt;() 帧内创建）经 CurrentAopUser 供给；
    /// 帧外枚举抛守卫）。<c>[DiContractIgnore]</c>：豁免 DI001 误报。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class InboxNotifier : DomainServiceBase, INotificationNotifier
    {
        private UserNotificationEntityDataService? _dataService;

        private UserNotificationEntityDataService DataService => _dataService ??= User.Use<UserNotificationEntityDataService>();

        public InboxNotifier(IDomainUser user) : base(user)
        {
        }

        /// <summary>通道名。</summary>
        public string Name => "Inbox";

        public async Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
        {
            var exists = await DataService.ExistsByUserIdAndNotificationAsync(request.UserId, request.NotificationId, ct);
            if (exists) return; // 幂等：已投递跳过

            await DataService.CreateAsync(new UserNotificationEntity
            {
                UserId = request.UserId,
                NotificationId = request.NotificationId,
                State = 0,
                CreateTime = DateTime.UtcNow
            }, ct);
        }
    }
}