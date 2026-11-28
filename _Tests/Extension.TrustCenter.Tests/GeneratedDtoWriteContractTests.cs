using System;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter.DTOs;
using TKWF.Ext.TrustCenter.Generated;
using Xunit;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// 生成 DTO 写契约回归网（代码审核 2026-11-23 P1 项 4）：
/// 镜像 Extension.AuditLogging.Tests/GeneratedDtoCreateContractTests 的 MetaContext 安装方式，
/// 锁定 xCodeGen 产物 <c>SealedTicketEntityDto.g.cs</c> 的 IDomainDto.ApplyToEntity（Create 写入底座）：
/// 业务字段逐一映射到实体，<b>Id 绝不映射</b>（主键由数据库分配，DTO 不可伪造）。
/// </summary>
public class GeneratedDtoWriteContractTests
{
    static GeneratedDtoWriteContractTests()
        => MetaContextInstaller.Install(ProjectMetaContext.GetOrCreateInstance());

    [Fact]
    public void SealedTicketEntityDto_ApplyToEntity_MapsFields_ButNotId()
    {
        var createTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updateTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var expiresAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dto = new SealedTicketEntityDto
        {
            Id = 999,                       // 伪造主键——必须不被映射
            CodeHash = "contract-hash",
            ExpectedClaimant = "alice",
            PayloadEncrypted = "cipher-text",
            ExpiresAt = expiresAt,
            UsedCount = 2,
            MaxRedeems = 3,
            CreateTime = createTime,
            UpdateTime = updateTime
        };
        var entity = new SealedTicketEntity();

        var mapped = ((IDomainDto<SealedTicketEntity>)dto).ApplyToEntity(entity, EnumSceneFlags.Create);

        Assert.Same(entity, mapped);
        // Id 不映射（DTO 置 999 不生效）——由数据库分配
        Assert.Equal(0, entity.Id);
        Assert.Equal("contract-hash", entity.CodeHash);
        Assert.Equal("alice", entity.ExpectedClaimant);
        Assert.Equal("cipher-text", entity.PayloadEncrypted);
        Assert.Equal(expiresAt, entity.ExpiresAt);
        Assert.Equal(2, entity.UsedCount);
        Assert.Equal(3, entity.MaxRedeems);
        // CreateTime/UpdateTime 属框架托管字段（CreateMapper 覆写为 UtcNow，DTO 值不生效）——
        // 与 GeneratedDtoCreateContractTests 的口径一致，不纳入"DTO 映射字段"断言。
    }

    /// <summary>ProjectMetaContextBase.Instance 安装器（setter 为 protected，经子类公开）。</summary>
    private sealed class MetaContextInstaller : ProjectMetaContextBase
    {
        public static void Install(IProjectMetaContext context) => Instance = context;

        public override ProjectConfiguration Configuration => null!;
        public override MetadataChangeLog ChangeLog => null!;
        public override string MetadataSchemaVersion => "test";
    }
}
