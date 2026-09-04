using System;
using System.Collections.Generic;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.95 (ADR49 v3): 权限扩展实体注册器——声明 EF Core 消费方需要注册的扩展实体类型。
    /// <para>主框架 <c>EFCoreModelConfigurator.ApplyCore</c> 经 SG 编译期清单
    /// (<c>GeneratedEntityRegistrars.CreateInstances()</c>) 发现本实现，逐一
    /// <c>modelBuilder.Entity(type)</c> 注册——消费方零知晓扩展表结构（无需显式 <c>DbSet&lt;T&gt;</c>）。</para>
    /// <para>FreeSql 侧无需本接口（<c>SyncStructure(assembly)</c> 按程序集扫描 <c>[Table]</c>
    /// 天然覆盖扩展实体）——本接口补齐 EF Core 唯一缺口，两 ORM 统一到 SG EntityAssemblies 清单来源。</para>
    /// <para>⚠️ 边界：本接口仅声明实体类型（注册进 model）；索引/导航/转换配置由扩展在 EF Core 侧
    /// 自主声明（EF Core 原生 fluent API），不覆盖消费方自定义配置。</para>
    /// </summary>
    public sealed class PermissionsEntityRegistrar : IExtensionEntityRegistrar
    {
        /// <summary>声明 EF Core 需注册的权限扩展实体：<see cref="PermissionGrantEntity"/>。</summary>
        public IEnumerable<Type> GetEntityTypes()
        {
            yield return typeof(PermissionGrantEntity);
        }
    }
}