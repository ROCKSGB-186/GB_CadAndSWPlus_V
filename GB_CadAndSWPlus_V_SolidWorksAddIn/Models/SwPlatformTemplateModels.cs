using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>
    /// 服务器返回的 SolidWorks 平台模板信息。
    /// </summary>
    [DataContract]
    public sealed class SwPlatformTemplateResponse
    {
        /// <summary>服务器是否找到已配置的模板附件。</summary>
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        /// <summary>服务器返回的中文说明。</summary>
        [DataMember(Name = "message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>平台业务代码。</summary>
        [DataMember(Name = "platformCode")]
        public string PlatformCode { get; set; } = string.Empty;

        /// <summary>模板附件数据库 ID。</summary>
        [DataMember(Name = "fileId")]
        public long? FileId { get; set; }

        /// <summary>模板原始文件名。</summary>
        [DataMember(Name = "fileName")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>模板文件大小，单位为字节。</summary>
        [DataMember(Name = "fileSize")]
        public long FileSize { get; set; }

        /// <summary>模板版本或文件哈希。</summary>
        [DataMember(Name = "version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>模板文件下载 API 路径。</summary>
        [DataMember(Name = "downloadPath")]
        public string DownloadPath { get; set; } = string.Empty;
    }
}
