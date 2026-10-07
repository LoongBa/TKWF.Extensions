using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthSurface.DTOs;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 数据服务：应用目录实体——"我的应用"聚合的 AppName 来源（AppId 唯一索引查询）。
/// <para>提示：标准 CRUD 逻辑和构造函数已由 AuthAppEntityDataService.g.cs 承载，本分部仅编写业务原子方法。</para>
/// </summary>
partial class AuthAppEntityDataService(IDomainUser user, IEntityDAC<AuthAppEntity> dac)
        : DomainDataServiceBase<AuthAppEntity, AuthAppEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按 AppId 查询（唯一索引 UX_AuthApp_AppId——目录维护判定前置）。</summary>
    public async Task<AuthAppEntity?> GetByAppIdAsync(string appId, CancellationToken ct = default)
        => await EntityGetAsync(e => e.AppId == appId, ct);
}
