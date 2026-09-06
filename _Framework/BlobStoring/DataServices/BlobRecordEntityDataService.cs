using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.BlobStoring;
using TKWF.Ext.BlobStoring.DTOs;

namespace TKWF.Ext.BlobStoring;

/// <summary>数据服务：Blob &#x8BB0;&#x5F55;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5B58;&#x50A8;&#x4E8C;&#x8FDB;&#x5236;&#x5927;&#x5BF9;&#x8C61;&#x7684;&#x5143;&#x6570;&#x636E;&#xFF08;&#x540D;&#x79F0;&#x3001;&#x8DEF;&#x5F84;&#x3001;&#x5185;&#x5BB9;&#x7C7B;&#x578B;&#x3001;&#x5927;&#x5C0F;&#x3001;&#x6807;&#x7B7E;&#x3001;&#x4E0A;&#x4F20;&#x8005;&#x7B49;&#xFF09;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; &lt;see cref=&quot;!:TKW.Framework.Domain.IDomainEntity&quot;/&gt; &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;BlobRecord&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 BlobRecordEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class BlobRecordEntityDataService(IDomainUser user, IEntityDAC<BlobRecordEntity> dac)
        : DomainDataServiceBase<BlobRecordEntity, BlobRecordEntityDto>(user, dac, hasSoftDelete:false) 
{
      // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
      public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
      {
          // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
          return await DeleteAsync(id, ct);
      }

      // ── 数据访问红线整改：BlobRecordStore 委托路径的业务方法 ──

      /// <summary>按主键 Id 查询单条 Blob 记录实体（基类 <c>GetByIdAsync</c> 返回 DTO，此处返回实体供 Store 委托）。</summary>
      public async Task<BlobRecordEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
          => await EntityGetAsync(b => b.Id == id, ct);

      /// <summary>按名称（业务键）查询单条 Blob 记录。</summary>
      public async Task<BlobRecordEntity?> GetByNameAsync(string name, CancellationToken ct = default)
          => await EntityGetAsync(b => b.Name == name, ct);

      /// <summary>按内容类型过滤查询 Blob 记录列表（分页，按 Id 倒序——最新优先）。</summary>
      public async Task<List<BlobRecordEntity>> GetListByContentTypeAsync(
          string? contentType, int skip, int take, CancellationToken ct = default)
      {
          Expression<Func<BlobRecordEntity, bool>>? predicate = null;
          if (!string.IsNullOrEmpty(contentType))
              predicate = b => b.ContentType == contentType;
          return await EntitySelectAsync(predicate, skip, take, q => q.OrderByDescending(b => b.Id), ct);
      }

      /// <summary>Upsert——按 Id 查存在：存在则更新（保留自增 Id 与 CreateTime，不做先删后插），不存在则插入。</summary>
      public async Task UpsertAsync(BlobRecordEntity entity, CancellationToken ct = default)
      {
          var existing = await EntityGetAsync(b => b.Id == entity.Id, ct);
          if (existing != null)
          {
              existing.Name = entity.Name;
              existing.Path = entity.Path;
              existing.ContentType = entity.ContentType;
              existing.Size = entity.Size;
              existing.Tags = entity.Tags;
              existing.UploaderName = entity.UploaderName;
              existing.UpdateTime = entity.UpdateTime;
              await EntityUpdateAsync(existing, ct);
          }
          else
          {
              await EntityCreateAsync(entity, ct);
          }
      }

      /// <summary>按 Id 物理删除（hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛
      /// <c>InvalidOperationException</c>，故用 <c>EntityDeleteBatchAsync</c> 走物理删路径）。</summary>
      public async Task DeleteByIdAsync(long id, CancellationToken ct = default)
      {
          var existing = await EntityGetAsync(b => b.Id == id, ct);
          if (existing != null)
              await EntityDeleteBatchAsync(new[] { id }, ct);
      }
 }