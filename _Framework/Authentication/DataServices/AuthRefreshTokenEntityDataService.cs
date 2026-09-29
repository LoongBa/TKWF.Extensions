using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Authentication.DTOs;

namespace TKWF.Ext.Authentication;

/// <summary>数据服务：刷新令牌实体——Refresh rotation（SHA256 落库 + TokenVersion 闭环，只增语义）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthRefreshTokenEntityDataService.g.cs 承载。
// 【只增语义（对齐 SecurityLog）】本分部【不】提供 Update/Delete 公开业务方法——
// 唯一写路径 = CreateAsync（签发落库）+ MarkRevokedAsync/RevokeAllByUserIdAsync（rotation 消费）。
partial class AuthRefreshTokenEntityDataService(IDomainUser user, IEntityDAC<AuthRefreshTokenEntity> dac)
        : DomainDataServiceBase<AuthRefreshTokenEntity, AuthRefreshTokenEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按 TokenHash 查询（SHA256 hex 唯一索引——明文不落库）。</summary>
    public async Task<AuthRefreshTokenEntity?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await EntityGetAsync(r => r.TokenHash == tokenHash, ct);

    /// <summary>签发落库（CreateTime UTC）。</summary>
    public async Task CreateAsync(AuthRefreshTokenEntity entity, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        await EntityCreateAsync(entity, ct);
    }

    /// <summary>标记撤销（rotation 消费旧 token 后置 true）。</summary>
    public async Task MarkRevokedAsync(long id, DateTime revokedAt, CancellationToken ct = default)
    {
        var row = await EntityGetAsync(r => r.Id == id, ct);
        if (row == null) return;
        row.IsRevoked = true;
        row.RevokedAt = revokedAt;
        await EntityUpdateAsync(row, ct);
    }

    /// <summary>撤销某用户全部 refresh（重用检测——判定泄露后清场）。</summary>
    public async Task RevokeAllByUserIdAsync(string userId, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(r => r.UserId == userId, 0, 10_000, null, ct);
        foreach (var row in rows.Where(r => !r.IsRevoked))
        {
            row.IsRevoked = true;
            row.RevokedAt = DateTime.UtcNow;
            await EntityUpdateAsync(row, ct);
        }
    }
}
