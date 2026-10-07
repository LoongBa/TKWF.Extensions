using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 用户档案实体——认证中心基础档案 1:1（V0.9.0 凭据/档案表级分离 A.1：Nickname/Avatar/Birthday/Gender/Email）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>红线（数据访问）：档案读写经 SG1 生成 <see cref="UserProfileEntityDataService"/>——零 IFreeSql/IEntityDAC 直注入。</para>
    /// <para>字段语义（ADR A.5/A.6/A.7）：<b>Phone 双角色</b>——凭据角色归 <see cref="AuthAccountEntity.Phone"/>（登录锚点），
    /// 联系方式角色由业务层自决（框架不提供"复制登录号"默认操作）；<b>Email</b> 联系方式列（不启用登录凭据——A.6 fail-closed
    /// 默认关闭，Email 找回通道与 Email 登录凭据是两件事可独立启用）；<b>Gender</b> 宽松自由文本（max 32，无枚举约束——A.7）。</para>
    /// <para>表名 <c>TKWF_UserProfile</c>（ADR100——AuthCenter 表前缀批次）。</para>
    /// </summary>
    [Table("TKWF_UserProfile")]
    [FreeSql.DataAnnotations.Index("TKWFIX_UserProfile_UId", nameof(UId), IsUnique = true)] // 1:1 到 AuthAccount.UId
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class UserProfileEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（1:1 关联 <see cref="AuthAccountEntity.UId"/>——档案仅此标识，无独立删除生命周期）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>昵称（V0.9.0 从 AuthAccount 迁入；微信授权拉取）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string? Nickname { get; set; }

        /// <summary>头像 URL（string 非二进制——DMP 实践确认；V0.9.0 从 AuthAccount 迁入）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string? Avatar { get; set; }

        /// <summary>生日（可空——档案宽松可选）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime? Birthday { get; set; }

        /// <summary>性别——宽松自由文本（可空 max 32，无枚举约束——B.7 "108 种性别"场景，框架不枚举业务分类）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(32)]
        public string? Gender { get; set; }

        /// <summary>邮箱——联系方式角色（A.5；可空。V0.9.0 不作为登录凭据——A.6 Email 可选凭据扩展点 fail-closed 默认关）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(64)]
        public string? Email { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
