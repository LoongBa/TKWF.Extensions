using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interception;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W3)：权限检查过滤器。
    /// 读取方法/控制器接口上的 <see cref="RequirePermissionAttribute"/> 标记，
    /// 在 PreProceed 阶段调用 <see cref="IPermissionChecker.IsGrantedAsync(string)"/> 检查权限是否授予。
    /// <para>
    /// 经 <see cref="PermissionExtensionInitializer{TUserInfo}.ConfigureFilters"/> 注册到
    /// <see cref="TKW.Framework.Domain.FilterTier.Security"/>（Tier-S，与 AuthorityFilter 同层）。
    /// 无标记的方法直接跳过（<see cref="CanWeGo"/> 短路），零开销。
    /// </para>
    /// <para>
    /// 设计决策（fail-closed，对齐 <c>FeatureFilterAttribute</c>）：
    /// <list type="bullet">
    /// <item><c>IPermissionChecker</c> 未注册 → 抛 <see cref="InvalidOperationException"/></item>
    /// <item>权限名未定义（<c>IPermissionDefinitionRepository</c> 无此权限）→ 视为未授予 → 拒绝</item>
    /// <item>权限未授予 → 抛 <see cref="DomainException"/>（<c>PERMISSION_DENIED</c>）</item>
    /// <item>未标记 <c>[RequirePermission]</c> → 不受影响</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PermissionFilterAttribute<TUserInfo> : DomainFilterAttribute<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        public override bool CanWeGo(DomainInvocationWhereType invocationWhere, DomainContext<TUserInfo> context)
            => context.MethodFlags.Any(f => f is RequirePermissionAttribute)
               || context.ControllerFlags.Any(f => f is RequirePermissionAttribute);

        public override async Task PreProceedAsync(DomainInvocationWhereType where, DomainContext<TUserInfo> context)
        {
            // 收集方法级和控制器级所有 RequirePermission 标记（两者均需通过检查）
            var flags = context.MethodFlags.Concat(context.ControllerFlags)
                .OfType<RequirePermissionAttribute>().ToList();
            if (flags.Count == 0) return;

            var checker = context.ServiceProvider.GetService<IPermissionChecker>()
                ?? throw new InvalidOperationException(
                    "IPermissionChecker 未注册，但方法标记了 [RequirePermission]。请先注册 IPermissionChecker 到 DI 容器（UsePermissions() 默认注册）。");

            foreach (var flag in flags)
            {
                var results = await checker.IsGrantedAsync(flag.Permissions);
                bool passed = flag.Logic switch
                {
                    PermissionLogic.All => flag.Permissions.All(p => results.GetValueOrDefault(p) == true),
                    PermissionLogic.Any => flag.Permissions.Any(p => results.GetValueOrDefault(p) == true),
                    _ => false
                };
                if (!passed)
                {
                    var message = flag.Logic switch
                    {
                        PermissionLogic.All => $"未获得全部所需权限（All 策略）: {string.Join(", ", flag.Permissions)}",
                        PermissionLogic.Any => $"未获得任一所需权限（Any 策略）: {string.Join(", ", flag.Permissions)}",
                        _ => $"权限检查失败: {string.Join(", ", flag.Permissions)}"
                    };
                    // V4.9.72：权限拒绝 = 已认证但无操作权限 → FORBIDDEN（对齐 DomainException.ErrorCodes）
                    throw new DomainException(message)
                    {
                        ErrorCode = DomainException.ErrorCodes.Forbidden
                    };
                }
            }
        }

        public override Task PostProceedAsync(DomainInvocationWhereType where, DomainContext<TUserInfo> context)
            => Task.CompletedTask;
    }
}
