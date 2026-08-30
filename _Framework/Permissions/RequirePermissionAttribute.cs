using System;
using TKW.Framework.Domain.Interception;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W3)：方法级权限声明标记。
    /// 声明在服务接口方法或控制器接口上时，
    /// <see cref="PermissionFilterAttribute{TUserInfo}"/> 在 PreProceed 阶段调用 <c>IPermissionChecker</c>
    /// 检查权限是否授予。
    /// <para>
    /// 方法级和控制器级标记同时生效（两者均需通过检查）。
    /// 支持多个 <c>[RequirePermission]</c> 作用于同一方法（<c>AllowMultiple=true</c>）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 使用前需注册 <c>IPermissionChecker</c> 到 DI 容器（由 <see cref="PermissionExtensionInitializer{TUserInfo}"/>
    /// 默认注册）。
    /// <c>IPermissionChecker</c> 未注册时过滤器抛出 <see cref="InvalidOperationException"/>（fail-closed）。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequirePermissionAttribute : DomainFlagAttribute
    {
        /// <summary>所需权限名列表。</summary>
        public string[] Permissions { get; }

        /// <summary>权限检查逻辑：All — 全部授予；Any — 任一授予（默认 All）。</summary>
        public PermissionLogic Logic { get; set; } = PermissionLogic.All;

        /// <param name="permissions">所需权限名（可多个，点分层级如 "Order.Create"）。</param>
        public RequirePermissionAttribute(params string[] permissions)
        {
            Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        }
    }
}
