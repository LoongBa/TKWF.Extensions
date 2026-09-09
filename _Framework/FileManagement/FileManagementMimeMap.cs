using System;
using System.Collections.Generic;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 扩展名 → MIME ContentType 映射（轻量自建，零第三方依赖）。
    /// <para>键一律<b>小写归一含点</b>（如 ".jpg"）；调用方须先 <c>Path.GetExtension(...).ToLowerInvariant()</c>。</para>
    /// <para>ContentType 语义（C5 评审裁定）：一律由扩展名<b>服务端推导</b>——不信任客户端传入值
    /// （客户端值仅作提示，不一致以推导为准）；未知扩展名 <see cref="TryGet"/> 返回 null，
    /// 由调用方 fallback 为 <c>application/octet-stream</c>。</para>
    /// </summary>
    internal static class FileManagementMimeMap
    {
        /// <summary>扩展名（小写含点）→ ContentType 字典（常用 ~35 项）。</summary>
        private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 图片
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".bmp"] = "image/bmp",
            [".ico"] = "image/x-icon",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".psd"] = "image/vnd.adobe.photoshop",
            // 文档
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".css"] = "text/css",
            [".js"] = "application/javascript",
            // Office
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".xls"] = "application/vnd.ms-excel",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".doc"] = "application/msword",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".ppt"] = "application/vnd.ms-powerpoint",
            // 压缩包
            [".zip"] = "application/zip",
            [".rar"] = "application/vnd.rar",
            [".7z"] = "application/x-7z-compressed",
            [".gz"] = "application/gzip",
            [".tar"] = "application/x-tar",
            // 音视频
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".mp4"] = "video/mp4",
            [".avi"] = "video/x-msvideo",
            [".mov"] = "video/quicktime",
            [".webm"] = "video/webm",
            // 其他
            [".apk"] = "application/vnd.android.package-archive",
        };

        /// <summary>按扩展名（小写含点）查 ContentType；未知返回 null（调用方 fallback application/octet-stream）。</summary>
        public static string? TryGet(string extensionLower)
        {
            if (string.IsNullOrEmpty(extensionLower))
                return null;
            return Map.TryGetValue(extensionLower, out var contentType) ? contentType : null;
        }
    }
}
