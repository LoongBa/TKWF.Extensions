using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：令牌黑名单实体——持久化黑名单（摒弃 DMP 内存 ConcurrentDictionary，只增语义）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthTokenBlacklistEntityDataService.g.cs 承载。
// 【只增语义（对齐 SecurityLog）】本分部【不】提供 Update/Delete 公开业务方法——
// 唯一写路径 = CreateAsync（撤销落库）；清理走 v0.2.0 保留天数任务（方案 §十）。
partial class AuthTokenBlacklistEntityDataService(IDomainUser user, IEntityDAC<AuthTokenBlacklistEntity> dac)
        : DomainDataServiceBase<AuthTokenBlacklistEntity, AuthTokenBlacklistEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按 jti 查询（验签黑名单判定；唯一索引）。</summary>
    public async Task<AuthTokenBlacklistEntity?> GetByJtiAsync(string jti, CancellationToken ct = default)
        => await EntityGetAsync(b => b.Jti == jti, ct);

    /// <summary>撤销落库（ExpiresAt = 该 token 自然过期时间）。</summary>
    public async Task CreateAsync(AuthTokenBlacklistEntity entity, CancellationToken ct = default)
    {
        entity.RevokedAt = entity.RevokedAt == default ? DateTime.UtcNow : entity.RevokedAt;
        await EntityCreateAsync(entity, ct);
    }
}
