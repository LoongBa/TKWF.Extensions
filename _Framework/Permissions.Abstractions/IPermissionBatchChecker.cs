using System.Collections.Generic;
using System.Threading.Tasks;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V0.9.0：多用户批量权限检查器——判定多个用户对指定权限的授予状态（单权限 × 多用户）。
    /// <para>与 <see cref="IPermissionChecker"/>（当前用户检查）分离——独立接口（Oracle P1-1）：
    /// ①当前用户检查依赖 <c>DomainUserContext.CurrentAopUser</c>（ambient 上下文），多用户检查按 <c>long userId</c>
    /// 逐用户评估，是不同关注点（ISP 合规）；②独立接口零迁移——既有 IPermissionChecker 实现/测试桩不破坏。</para>
    /// <para>实现：<c>PermissionChecker&lt;TUserInfo&gt;</c> 同时实现本接口——内部复用既有安全逻辑
    /// （<c>EvaluatePermission</c>：Admin.All 放行 + fail-closed + 用户级优先/角色级回退），
    /// 角色经 <c>IRoleProvider&lt;TUserInfo&gt;</c> 按 userId 解析（IdentityRoleProvider 已支持）。
    /// 供 Notifications v0.3.0 逐用户权限门控消费（发布通知时过滤无权限收件人）。</para>
    /// </summary>
    public interface IPermissionBatchChecker
    {
        /// <summary>
        /// 多用户批量检查——返回 用户ID → 是否授予 字典（单权限 × 多用户）。
        /// <para>fail-closed：未认证/未定义权限/未授予 → false；Admin.All（用户或角色）→ true。</para>
        /// <para>实现注意：独立批量查询（不得复用 IPermissionChecker 的当前用户 Scoped 缓存）。</para>
        /// </summary>
        Task<Dictionary<long, bool>> IsGrantedAsync(IReadOnlyList<long> userIds, string permissionName);
    }
}
