using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 用户邮箱提供者——消费方实现（从用户存储查邮箱），供 Email 通道投递解析收件地址。
/// <para>v0.2.0 多通道路由：<see cref="NotificationPublisher"/> 按定义 UseChannels("Email") 投递时，
/// <see cref="EmailNotifier"/> 经 IServiceProvider 可空解析本接口——消费方注册（AddSingleton/AddScoped），
/// <b>扩展不注册</b>（对齐 IUserEmailProvider「消费方提供」语义）。未实现时 Email 通道前置缺失 → 日志警告跳过。</para>
/// </summary>
public interface IUserEmailProvider
{
    /// <summary>获取用户邮箱（无邮箱返回 null，Email 通道跳过投递）。</summary>
    Task<string?> GetEmailAsync(long userId, CancellationToken ct = default);
}