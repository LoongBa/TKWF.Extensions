using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：一次性票据实体——票据换令牌（TTL 5min 单次消费 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；纯前端静态站走公网 /oauth/exchange + PKCE code_verifier。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 OAuthTicketEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class OAuthTicketEntityDataService(IDomainUser user, IEntityDAC<OAuthTicketEntity> dac)
        : DomainDataServiceBase<OAuthTicketEntity, OAuthTicketEntityDto>(user, dac, hasSoftDelete:false) 
{
     /// <summary>创建一次性票据（签发落库）。</summary>
     public async Task<OAuthTicketEntity> CreateAsync(OAuthTicketEntity entity, CancellationToken ct = default)
     {
         return await EntityCreateAsync(entity, ct);
     }

     /// <summary>按票据串查询（换取校验——防枚举/重放）。</summary>
     public async Task<OAuthTicketEntity?> GetByTicketAsync(string ticket, CancellationToken ct = default)
     {
         return await EntityGetAsync(e => e.Ticket == ticket, ct);
     }

     /// <summary>条件标记已消费（Oracle M2 修复——防 TOCTOU 竞态）：条件 select <c>Id == id &amp;&amp; !IsConsumed</c>，
     /// 并发换取同票据时输者返回 false → 调用方（OAuthTicketService.ExchangeAsync）抛 TICKET_CONSUMED——恢复"单次消费"保障。</summary>
     public async Task<bool> MarkConsumedAsync(long id, DateTime consumedAt, CancellationToken ct = default)
     {
         var entity = await EntityGetAsync(e => e.Id == id && !e.IsConsumed, ct);
         if (entity is null)
         {
             return false; // 已消费/不存在——输了竞态
         }
         entity.IsConsumed = true;
         entity.ConsumedAt = consumedAt;
         await EntityUpdateAsync(entity, ct);
         return true;
     }

     /// <summary>更新票据（签发方绑定用户 UserId 后写入——票据签发时用户未定，绑定后回填）。</summary>
     public async Task UpdateAsync(OAuthTicketEntity entity, CancellationToken ct = default)
         => await EntityUpdateAsync(entity, ct);
}