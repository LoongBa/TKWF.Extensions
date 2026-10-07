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

    /// <summary>按联盟锚点 openid 查询账号（SSO 联邦层自建 unionid——跨商户稳定锚点；唯一索引 TKWFIX_AuthAccount_FederationAnchorOpenId）。</summary>
    public async Task<AuthAccountEntity?> GetByFederationAnchorAsync(string federationAnchorOpenId, CancellationToken ct = default)
        => await EntityGetAsync(a => a.FederationAnchorOpenId == federationAnchorOpenId, ct);

    /// <summary>写/更新联盟锚点（一对一；唯一索引冲突 → 抛异常）。
    /// <para>V0.9.0（ADR-密码策略与口令协议 决策 4/A.8）：TokenVersion++ 触发点收口——联邦绑定变更属凭据面变更，
    /// 统一自增 TokenVersion（对齐列注释"密码/绑定变更自增"承诺）——CAS 原子更新防 TOCTOU。</para></summary>
    public async Task<string> SetFederationAnchorAsync(string uid, string federationAnchorOpenId, CancellationToken ct = default)
    {
        var account = await GetByUIdAsync(uid, ct);
        if (account == null) throw new InvalidOperationException($"ACCOUNT_NOT_FOUND uid={uid}");
        if (string.Equals(account.FederationAnchorOpenId, federationAnchorOpenId, StringComparison.Ordinal))
            return account.FederationAnchorOpenId; // 幂等：值未变不写库

        account.FederationAnchorOpenId = federationAnchorOpenId;
        account.TokenVersion++;   // ⑤ 收口：绑定变更 + TokenVersion++（旧 refresh 失效）
        await UpdateAsync(account, ct);
        return account.FederationAnchorOpenId!;
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

    /// <summary>TokenVersion 自增——密码/绑定变更后旧 Refresh Token 失效（闭环 DMP 缺口）。
    /// <para>V0.9.0 P1-4：改 CAS（ADR89 <c>EntityUpdateWhereAsync</c>）——消除 read-modify-write TOCTOU
    /// （<c>SET TokenVersion=TokenVersion+1 WHERE UId=@uid</c> 原子递增，与 <see cref="SetPasswordHashAsync"/> 同字段一致）。</para></summary>
    public async Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            a => a.UId == uid,
            a => new AuthAccountEntity { TokenVersion = a.TokenVersion + 1, UpdateTime = DateTime.UtcNow },
            ct);

    /// <summary>字段级写密码散列——CAS 原子更新 PasswordHash + TokenVersion++（V0.9.0 B.9：
    /// 替代裸 UpdateAsync 整实体；只触碰凭据列，不并发读写整行——ADR89）。影响行数 0 = UId 不存在。</summary>
    public async Task<int> SetPasswordHashAsync(string uid, string? passwordHash, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            a => a.UId == uid,
            a => new AuthAccountEntity { PasswordHash = passwordHash, TokenVersion = a.TokenVersion + 1, UpdateTime = DateTime.UtcNow },
            ct);

    /// <summary>清初始密码强制改密标记（V0.9.0 ADR-密码策略与口令协议 决策 5——改密落地后清 false；CAS 字段级更新）。</summary>
    public async Task<int> ClearMustChangePasswordAsync(string uid, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            a => a.UId == uid,
            a => new AuthAccountEntity { MustChangePassword = false, UpdateTime = DateTime.UtcNow },
            ct);

    /// <summary>按 Id 物理删除账号（管理删除路径，hasSoftDelete:false）。</summary>
    public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
        => await DeleteAsync(id, ct);
}
