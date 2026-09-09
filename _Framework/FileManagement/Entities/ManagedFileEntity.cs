using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 受管文件实体——业务文件元数据（目录内同名唯一 + SHA256 去重 + 物理存储委托 BlobStoring，F4-F10）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("ManagedFile")]</c>；列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
    /// <para>StoredPath 为 <see cref="TKWF.Ext.BlobStoring.IBlobStorageService"/> 相对路径（形如 {guid}/{name}）——
    /// 物理文件操作全部经 Blob 契约委托，本实体为<b>唯一业务元数据</b>（P2 裁定：不注入 IBlobRecordStore）。</para>
    /// <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）；
    /// 删除 = 元数据删除 + Blob 删除（Blob 删失败日志警告，孤儿容忍）。</para>
    /// <para>索引：<c>UX_ManagedFile_Folder_Name</c>（目录内同名唯一——上传/重命名/移动的唯一约束冲突源）
    /// + <c>IX_ManagedFile_Sha256</c>（去重查询）。</para>
    /// <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明；不声明 IsDeleted（物理删除）。</para>
    /// </summary>
    [Table("ManagedFile")]
    [FreeSql.DataAnnotations.Index("UX_ManagedFile_Folder_Name", nameof(FolderId) + "," + nameof(Name), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("IX_ManagedFile_Sha256", nameof(Sha256))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class ManagedFileEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>归属目录 Id（可空 = 根级文件；引用守卫：创建/移动时校验目录存在）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2, IsNullable = true)]
        public long? FolderId { get; set; }

        /// <summary>文件名（目录内唯一——UX 唯一约束）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string Name { get; set; } = "";

        /// <summary>BlobStoring 存储相对路径（形如 {guid}/{name}，Download/Delete 直接透传）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(1024)]
        public string StoredPath { get; set; } = "";

        /// <summary>扩展名（小写含点，如 ".jpg"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(32)]
        public string Extension { get; set; } = "";

        /// <summary>MIME ContentType（服务端由扩展名推导，未知 fallback application/octet-stream，C5）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
        [MaxLength(128)]
        public string? ContentType { get; set; }

        /// <summary>文件大小（字节）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public long Size { get; set; }

        /// <summary>文件内容 SHA256（十六进制小写；去重键，IX 索引）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(64)]
        public string Sha256 { get; set; } = "";

        /// <summary>上传者名称（审计字段，v0.1.0 预留——Manager 未注入当前用户上下文时为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
        [MaxLength(128)]
        public string? UploaderName { get; set; }

        /// <summary>创建时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
