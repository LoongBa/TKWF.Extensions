using System.Collections.Generic;
using System.Threading.Tasks;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限存储——持久化角色/用户权限值（对齐 D17 §5.1.2）。
    /// <para>V4.9.72 仅定义接口 + 默认 NoOp 实现；真实持久化（数据库/权限分配管理）留后续迭代。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// <para><b>V0.8.1 N+1 优化</b>：新增批量查询 <see cref="GetGrantedPermissionNamesAsync"/>——
    /// 权限检查热点路径（PermissionChecker）按 provider 一次加载全部已授予权限名，内存判定替代逐名逐键单条查询，
    /// 消除"循环角色逐次查库"的 N×M 放大（每个角色 × 每个权限名各一次 DB 往返）。</para>
    /// </summary>
    public interface IPermissionStore
    {
        /// <summary>读取权限授予结果（按提供者：角色/用户等）。</summary>
        Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey);

        /// <summary>写入权限授予值。</summary>
        Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted);

        /// <summary>按 provider 批量读取已授予权限名集合（<c>IsGranted == true</c>）——N+1 优化（V0.8.1）。
        /// <para><paramref name="providerKeys"/> 为空时返回该 provider 下全部已授予；否则限定指定键。
        /// 返回集合语义：仅含已授予的权限名（未授予/未设置 = 不在集合），调用方以此做内存 fail-closed 判定。</para>
        /// <para>⚠️ 多 providerKeys 时返回<b>扁平并集</b>（无按键归因）——多用户归因用
        /// <see cref="GetGrantedPermissionsByProviderKeyAsync"/>（V0.9.0）。</para></summary>
        Task<HashSet<string>> GetGrantedPermissionNamesAsync(
            string providerName, IEnumerable<string>? providerKeys = null);

        /// <summary>按 provider + 多键批量读取已授予权限名集合，<b>按 providerKey 分组归因</b>（V0.9.0，多用户批量）。
        /// <para>返回 <c>providerKey → 该键已授予权限名集合</c>——支持多用户权限检查按用户归因
        /// （对应 <see cref="GetGrantedPermissionNamesAsync"/> 的扁平并集缺口）。
        /// providerKeys 为空 = 全部；每键仅含已授予（未授予/未设置 = 该键不在字典或集合为空）。</para></summary>
        Task<Dictionary<string, HashSet<string>>> GetGrantedPermissionsByProviderKeyAsync(
            string providerName, IEnumerable<string>? providerKeys = null);
    }
}
