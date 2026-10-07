using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：密码重置码实体（V0.9.0 B.10/B.11——找回多通道自建链路落库底座；UId-keyed）。
/// <para>红线（数据访问）：找回码读写经 SG1 生成 <see cref="PasswordResetCodeEntityDataService"/>——零 ORM 直注入。</para>
/// <para>安全语义（对齐 SmsRecord 只增 + 单次消费）：CodeHash 单向散列落库，不存明文；消费 CAS（ADR89
/// <c>EntityUpdateWhereAsync</c>——<c>Id 匹配 &amp;&amp; IsConsumed=false, set IsConsumed=true</c>）防并发重放。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PasswordResetCodeEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
 partial class PasswordResetCodeEntityDataService(IDomainUser user, IEntityDAC<PasswordResetCodeEntity> dac)
        : DomainDataServiceBase<PasswordResetCodeEntity, PasswordResetCodeEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>创建重置码记录（CreateTime UTC 兜底；回写自增 Id）。</summary>
    public async Task CreateAsync(PasswordResetCodeEntity entity, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        await EntityCreateAsync(entity, ct);
    }

    /// <summary>按 UId + 通道查最近未消费记录（找回链路读取——SMS/Email 通道；扫码走 OAuthTicket 不入本表）。</summary>
    public async Task<PasswordResetCodeEntity?> GetLastUnconsumedAsync(string uid, string channel, CancellationToken ct = default)
        => await EntityGetAsync(c => c.UId == uid && c.Channel == channel && !c.IsConsumed, ct);

    /// <summary>单次消费（CAS 条件更新——ADR89：<c>WHERE Id=? AND IsConsumed=false → SET IsConsumed=true</c>；返回 false = 已被消费/不存在）。</summary>
    public async Task<bool> TryConsumeAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            c => c.Id == id && !c.IsConsumed,
            c => new PasswordResetCodeEntity { IsConsumed = true },
            ct) > 0;
}