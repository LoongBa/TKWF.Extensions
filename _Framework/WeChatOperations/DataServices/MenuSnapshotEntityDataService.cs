using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x83DC;&#x5355;&#x57FA;&#x51C6;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMenuSnapshot&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x672C;&#x5730;&#x6743;&#x5A01;&#x57FA;&#x7EBF;&#xFF08;&#x83DC;&#x5355;&#x7ED3;&#x6784;&#x7684;&#x672C;&#x5730;&#x57FA;&#x51C6;&#xFF0C;&#x4E0E; MenuVersion &#x540C;&#x4E00;&#x4E8B;&#x52A1;&#x540C;&#x6B65;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x6BCF; channel &#x4E00;&#x884C;&#xFF08;&#x552F;&#x4E00;&#x7D22;&#x5F15; ChannelId&#xFF09;&#x2014;&#x2014;&lt;see cref=&quot;MenuJson&quot;/&gt; &#x4E3A;&#x5FAE;&#x4FE1;&#x83DC;&#x5355;&#x5B8C;&#x6574; JSON&#xFF08;&#x5927;&#x6587;&#x672C;&#xFF09;&#xFF1B;      &#x53D1;&#x5E03;&#x6210;&#x529F; / &#x62C9;&#x53D6;&#x786E;&#x8BA4;&#x540E;&#x66F4;&#x65B0;&#xFF08;&#x65B9;&#x6848; &#xA7;3.3.1 Oracle P2&#xFF1A;MenuSnapshot &#x4E0E; MenuVersion &#x4E00;&#x81F4;&#x6027;&#x2014;&#x2014;&#x53D1;&#x5E03;&#x6210;&#x529F;&#x540E;&#x540C;&#x6B65;&#x66F4;&#x65B0;&#x4E24;&#x8005;&#xFF0C;&#x540C;&#x4E00;&#x4E8B;&#x52A1;&#x4FDD;&#x8BC1;&#x4E00;&#x81F4;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MenuSnapshotEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MenuSnapshotEntityDataService(IDomainUser user, IEntityDAC<MenuSnapshotEntity> dac)
        : DomainDataServiceBase<MenuSnapshotEntity, MenuSnapshotEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}