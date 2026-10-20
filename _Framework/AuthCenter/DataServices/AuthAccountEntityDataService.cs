using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：平台账号实体——认证中心身份源（凭据白名单表：UId/TokenVersion/IsEnabled；V0.11.0 起
/// 手机号/密码/联邦锚点/冻结状态均迁 AuthFactorEntity 与 AuthAccountFreezeEntity，本表不再承载）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthAccountEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法（TokenService/SmsAuthenticationProvider 委托路径）。
partial class AuthAccountEntityDataService(IDomainUser user, IEntityDAC<AuthAccountEntity> dac)
        : DomainDataServiceBase<AuthAccountEntity, AuthAccountEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按平台内部 id 查询账号（JWT sub 引用；TokenService refresh/issue 路径）。</summary>
    public async Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => await EntityGetAsync(a => a.UId == uid, ct);

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

    /// <summary>TokenVersion 自增——密码/凭据变更后旧 Refresh Token 失效（闭环 DMP 缺口）。
    /// <para>V0.9.0 P1-4：改 CAS（ADR89 <c>EntityUpdateWhereAsync</c>）——消除 read-modify-write TOCTOU
    /// （<c>SET TokenVersion=TokenVersion+1 WHERE UId=@uid</c> 原子递增）。V0.11.0：密码写路径统一经
    /// <c>IAuthAccountService.WritePasswordAsync</c> 调用本方法（因素行 SecretHash 变更后失效旧 refresh）。</para></summary>
    public async Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            a => a.UId == uid,
            a => new AuthAccountEntity { TokenVersion = a.TokenVersion + 1, UpdateTime = DateTime.UtcNow },
            ct);

    /// <summary>按 Id 物理删除账号（管理删除路径，hasSoftDelete:false）。</summary>
    public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
        => await DeleteAsync(id, ct);
}
