using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：平台账号实体——认证中心身份源（手机号主键 + 微信绑定 + 认证声明）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthAccountEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法（TokenService/SmsAuthenticationProvider 委托路径）。
partial class AuthAccountEntityDataService(IDomainUser user, IEntityDAC<AuthAccountEntity> dac)
        : DomainDataServiceBase<AuthAccountEntity, AuthAccountEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按平台内部 id 查询账号（JWT sub 引用；TokenService refresh/issue 路径）。</summary>
    public async Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => await EntityGetAsync(a => a.UId == uid, ct);

    /// <summary>按手机号查询账号（短信登录/注册路径）。</summary>
    public async Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => await EntityGetAsync(a => a.Phone == phone, ct);

    /// <summary>按公众号 openid 查询账号（网页授权 snsapi_base 路径）。</summary>
    public async Task<AuthAccountEntity?> GetByWechatMpOpenIdAsync(string openId, CancellationToken ct = default)
        => await EntityGetAsync(a => a.WechatMpOpenId == openId, ct);

    /// <summary>按开放平台网站应用 openid 查询账号（扫码 snsapi_login 路径）。</summary>
    public async Task<AuthAccountEntity?> GetByWechatWebOpenIdAsync(string openId, CancellationToken ct = default)
        => await EntityGetAsync(a => a.WechatWebOpenId == openId, ct);

    /// <summary>按联盟锚点 openid 查询账号（SSO 联邦层自建 unionid——跨商户稳定锚点；唯一索引 UX_AuthAccount_FederationAnchorOpenId）。</summary>
    public async Task<AuthAccountEntity?> GetByFederationAnchorAsync(string federationAnchorOpenId, CancellationToken ct = default)
        => await EntityGetAsync(a => a.FederationAnchorOpenId == federationAnchorOpenId, ct);

    /// <summary>写/更新联盟锚点（一对一；唯一索引冲突 → 抛异常）。</summary>
    public async Task SetFederationAnchorAsync(string uid, string federationAnchorOpenId, CancellationToken ct = default)
    {
        var account = await GetByUIdAsync(uid, ct);
        if (account == null) throw new InvalidOperationException($"ACCOUNT_NOT_FOUND uid={uid}");
        account.FederationAnchorOpenId = federationAnchorOpenId;
        await UpdateAsync(account, ct);
    }

    /// <summary>创建账号（回写自增 Id；CreateTime/UpdateTime UTC）。</summary>
    public async Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default)
    {
        account.CreateTime = DateTime.UtcNow;
        account.UpdateTime = DateTime.UtcNow;
        await EntityCreateAsync(account, ct);
    }

    /// <summary>更新账号（先设 UpdateTime）。</summary>
    public async Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default)
    {
        account.UpdateTime = DateTime.UtcNow;
        await EntityUpdateAsync(account, ct);
    }

    /// <summary>TokenVersion 自增——密码/绑定变更后旧 Refresh Token 失效（闭环 DMP 缺口）。</summary>
    public async Task IncrementTokenVersionAsync(string uid, CancellationToken ct = default)
    {
        var account = await GetByUIdAsync(uid, ct);
        if (account == null) return;
        account.TokenVersion++;
        await UpdateAsync(account, ct);
    }

    /// <summary>按 Id 物理删除账号（管理删除路径，hasSoftDelete:false）。</summary>
    public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
        => await DeleteAsync(id, ct);
}
