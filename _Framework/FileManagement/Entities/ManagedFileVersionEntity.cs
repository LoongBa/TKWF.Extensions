using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 受管文件版本实体（V0.2.0）——文件版本历史（上传新内容生成新版本，保留历史可回滚可追溯）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("ManagedFileVersion")]</c>；
    /// 列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
    /// <para>版本模型（Oracle P2-1 定稿）：不指定 SubDomain——对齐既有 <see cref="ManagedFileEntity"/> 风格；
    /// <c>Version</c> int 递增（首版 1，后续 max+1）；<c>UX_mfv_file_version</c>（FileId+Version）唯一——并发上传败者由 DB 约束兜底（P2-5）。</para>
    /// <para>正文在 Blob：版本行存 <c>StoredPath</c> 指针（每次上传新 {guid}/{name}——旧版本 Blob 天然保留），
    /// 不复制字节；回滚指针复用（不复制 Blob）。</para>
    /// <para>删除语义：物理删除（不声明 IsDeleted）——删除文件时版本行 + 版本 Blob 一并清理（Oracle P1-2）。</para>
    /// </summary>
    [Table("ManagedFileVersion")]
    [FreeSql.DataAnnotations.Index("UX_mfv_file_version", nameof(FileId) + "," + nameof(Version), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("IX_mfv_file", nameof(FileId))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class ManagedFileVersionEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属文件（对应 <see cref="ManagedFileEntity.Id"/>；IX 索引 + FileId+Version 联合唯一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long FileId { get; set; }

        /// <summary>版本号（1 起递增——首版 1，后续 max+1；与 FileId 联合唯一，并发兜底）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int Version { get; set; }

        /// <summary>该版本 Blob 存储相对路径（形如 {guid}/{name}——每次上传新路径，旧版本 Blob 天然保留）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(1024)]
        public string StoredPath { get; set; } = "";

        /// <summary>该版本扩展名（小写含点；对齐主表——回滚不回填主表 Extension，P2-3）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(32)]
        public string Extension { get; set; } = "";

        /// <summary>该版本 MIME ContentType（服务端推导）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
        [MaxLength(128)]
        public string? ContentType { get; set; }

        /// <summary>该版本文件大小（字节）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public long Size { get; set; }

        /// <summary>该版本文件内容 SHA256（十六进制小写）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(64)]
        public string Sha256 { get; set; } = "";

        /// <summary>该版本上传者名称（审计字段）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
        [MaxLength(128)]
        public string? UploaderName { get; set; }

        /// <summary>版本创建时间（UTC；CanUpdate=false——版本行 append-only 不可变，P2-7）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
