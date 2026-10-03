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

/// <summary>数据服务：Blob 记录表实体——存储二进制大对象的元数据（名称、路径、内容类型、大小、标签、上传者等）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("BlobRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para></summary>
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