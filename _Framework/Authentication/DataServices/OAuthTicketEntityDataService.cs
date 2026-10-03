using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Authentication.DTOs;

namespace TKWF.Ext.Authentication;

/// <summary>数据服务：&#x4E00;&#x6B21;&#x6027;&#x7968;&#x636E;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x7968;&#x636E;&#x6362;&#x4EE4;&#x724C;&#xFF08;TTL 5min &#x5355;&#x6B21;&#x6D88;&#x8D39; &#x2B; PKCE &#x2B; app_id/redirect_uri &#x767D;&#x540D;&#x5355; &#x2B; state &#x9632;&#x91CD;&#x653E;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x56DE;&#x8C03;&#x627F;&#x8F7D;&#x94C1;&#x5F8B;&#xFF08;&#x7528;&#x6237;&#x88C1;&#x5B9A; &#x2B; Oracle B1&#xFF09;&#xFF1A;URL &#x53EA;&#x5E26;&#x4E00;&#x6B21;&#x6027;&#x7968;&#x636E; &#x2B; redirect_uri&#xFF0C;&#x7EDD;&#x4E0D;&#x5E26;&#x654F;&#x611F;&#x4FE1;&#x606F;&#xFF1B;&#x7EAF;&#x524D;&#x7AEF;&#x9759;&#x6001;&#x7AD9;&#x8D70;&#x516C;&#x7F51; /oauth/exchange &#x2B; PKCE code_verifier&#x3002;&lt;/para&gt;</summary>
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
