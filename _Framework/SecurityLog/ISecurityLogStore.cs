using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志存储抽象——扩展侧自建（不修改主框架），只增不改语义（Oracle C2）：
    /// <b>仅写路径</b>（<see cref="SaveAsync"/>），无 Update/Delete 方法。
    /// </summary>
    public interface ISecurityLogStore
    {
        /// <summary>
        /// 追加写入一条安全日志事件（异常静默：落库失败记录 Warning，不阻断认证流程）。
        /// </summary>
        /// <param name="entry">安全日志事件（null 静默跳过）。</param>
        /// <param name="ct">取消令牌。</param>
        Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default);
    }
}
