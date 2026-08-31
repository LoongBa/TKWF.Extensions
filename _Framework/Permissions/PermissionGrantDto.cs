using System;
using TKW.Framework;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V0.3.0（权限管理 Service）：权限授予 DTO——手写骨架（DMP-Lite 模式，`.cs` 入库）。
    /// <para>实现 <see cref="IDomainDto{TEntity}"/>，提供 <see cref="ToEntity"/>/<see cref="ApplyToEntity"/>/<see cref="ValidateData"/>
    /// 显式实现（非 xCodeGen 生成，手写覆盖默认 throw 实现）。</para>
    /// <para>后续 xCodeGen 环境修复后，可生成 `.g.cs` 覆盖本骨架（`.cs` 优先级低于 `.g.cs`）。</para>
    /// </summary>
    public sealed class PermissionGrantDto : IDomainDto<PermissionGrantEntity>
    {
        /// <inheritdoc />
        public bool IsFromPersistentSource { get; set; }

        /// <summary>主键。</summary>
        public long Id { get; set; }

        /// <summary>权限名（如 "Order.Create"）。</summary>
        public string PermissionName { get; set; } = "";

        /// <summary>授予 provider（如 "User"/"Role"/"Member"）。</summary>
        public string ProviderName { get; set; } = "";

        /// <summary>provider 键（如 UserIdString）。</summary>
        public string ProviderKey { get; set; } = "";

        /// <summary>是否授予。</summary>
        public bool IsGranted { get; set; }

        /// <inheritdoc />
        public void ValidateData(EnumSceneFlags scene)
        {
            if (string.IsNullOrWhiteSpace(PermissionName))
                throw new InvalidOperationException("权限名不能为空");
            if (PermissionName.Length > 200)
                throw new InvalidOperationException("权限名长度不能超过 200");
            if (string.IsNullOrWhiteSpace(ProviderName))
                throw new InvalidOperationException("Provider 名不能为空");
            if (ProviderName.Length > 64)
                throw new InvalidOperationException("Provider 名长度不能超过 64");
            if (string.IsNullOrWhiteSpace(ProviderKey))
                throw new InvalidOperationException("Provider 键不能为空");
            if (ProviderKey.Length > 128)
                throw new InvalidOperationException("Provider 键长度不能超过 128");
        }

        /// <inheritdoc />
        public PermissionGrantEntity ToEntity(EnumSceneFlags scene)
        {
            return new PermissionGrantEntity
            {
                PermissionName = PermissionName,
                ProviderName = ProviderName,
                ProviderKey = ProviderKey,
                IsGranted = IsGranted
            };
        }

        /// <inheritdoc />
        public PermissionGrantEntity ApplyToEntity(PermissionGrantEntity entity, EnumSceneFlags scene = EnumSceneFlags.Update)
        {
            entity.PermissionName = PermissionName;
            entity.ProviderName = ProviderName;
            entity.ProviderKey = ProviderKey;
            entity.IsGranted = IsGranted;
            return entity;
        }

        /// <summary>Entity → DTO 映射（静态工厂方法）。</summary>
        public static PermissionGrantDto FromEntity(PermissionGrantEntity entity)
        {
            return new PermissionGrantDto
            {
                Id = entity.Id,
                PermissionName = entity.PermissionName,
                ProviderName = entity.ProviderName,
                ProviderKey = entity.ProviderKey,
                IsGranted = entity.IsGranted,
                IsFromPersistentSource = true
            };
        }
    }
}
