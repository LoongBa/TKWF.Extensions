using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Enumerations;

namespace TKWF.Ext.Identity;

/// <summary>
/// Identity 登录衔接基类（V0.3.0）——预实现密码登录（<c>IUserManager.VerifyCredentialsAsync</c> +
/// <c>GetUserRolesAsync</c> + 工厂方法），消费方 UserHelper 样板从 ~171 行降至 ~8 行（仅实现
/// <c>CreateUserInfoFromEntity</c> 工厂）。游客会话由框架默认兜底（V0.3.0 abstract→virtual），无需 override。
/// <para>用户提出（2026-09-07）+ 调研实证：<c>IUserManager</c> 是 Scoped，UserHelper 宿主级
/// （<c>OnRegisterDomainServices</c> 返回）——须 <c>user.Use&lt;IUserManager&gt;()</c> 运行时解析
/// （DMP-Lite <c>user.Use&lt;UserInfoDataService&gt;()</c> 先例），非构造注入。</para>
/// <para>角色填充（<c>Roles</c>）→ 框架 IsInRole / Permissions 角色级判定自动工作（消费方工厂实现）。</para>
/// </summary>
public abstract class IdentityUserHelperBase<TUserInfo> : DomainUserHelperBase<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>唯一 abstract：UserEntity + 角色名 → TUserInfo 工厂（消费方实现）。
    /// 典型实现：<c>new TUserInfo(entity.Id.ToString(), entity.UserName) { DisplayName = ..., Roles = roleNames.ToList() }</c>。
    /// 签名已解耦（UserEntity + roleNames）——DMP-Lite 不用 Roles 的消费方忽略 roleNames 即可。</summary>
    protected abstract TUserInfo CreateUserInfoFromEntity(UserEntity entity, IEnumerable<string> roleNames);

    /// <summary>预实现密码登录（DMP-Lite 先例：user.Use&lt;TDomainService&gt;() 运行时解析）。</summary>
    protected override async Task<TUserInfo> OnLoginByPasswordAsync(
        DomainUser<TUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
    {
        var userManager = user.GetService<IUserManager>();             // 运行时解析（UserHelper 宿主级，IUserManager Scoped；Use<T> 需 IDomainService 约束，GetService<T> 无约束）
        var idUser = await userManager.VerifyCredentialsAsync(userName, credential);
        if (idUser == null) throw new AuthenticationException("用户名或密码错误");   // 对齐框架 AuthController 登录失败语义
        var roles = await userManager.GetUserRolesAsync(idUser.Id);
        return CreateUserInfoFromEntity(idUser, roles.Select(r => r.Name));
    }
}
