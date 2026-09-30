using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>
    /// 服务器返回的 CAD 图元详情。
    /// </summary>
    [DataContract]
    public sealed class SwGraphicDetailsResponse
    {
        /// <summary>服务器查询是否成功。</summary>
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        /// <summary>服务器返回的中文说明。</summary>
        [DataMember(Name = "message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>图元文件及业务元数据。</summary>
        [DataMember(Name = "file")]
        public SwGraphicDto File { get; set; } = new SwGraphicDto();

        /// <summary>统一属性字典。</summary>
        [DataMember(Name = "attributes")]
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// CAD 图元基本信息。
    /// </summary>
    [DataContract]
    public sealed class SwGraphicDto
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "fileName")] public string? FileName { get; set; }
        [DataMember(Name = "displayName")] public string? DisplayName { get; set; }
        [DataMember(Name = "fileHash")] public string? FileHash { get; set; }
        [DataMember(Name = "fileType")] public string? FileType { get; set; }
        [DataMember(Name = "version")] public int Version { get; set; }
        [DataMember(Name = "description")] public string? Description { get; set; }
    }

    /// <summary>
    /// SolidWorks 实例注册请求。
    /// </summary>
    [DataContract]
    public sealed class SwInstanceRegisterRequest
    {
        [DataMember(Name = "swInstanceId")] public string SwInstanceId { get; set; } = string.Empty;
        [DataMember(Name = "pidInstanceId")] public string PidInstanceId { get; set; } = string.Empty;
        [DataMember(Name = "libraryItemId")] public int? LibraryItemId { get; set; }
        [DataMember(Name = "documentPath")] public string? DocumentPath { get; set; }
        [DataMember(Name = "documentName")] public string? DocumentName { get; set; }
        [DataMember(Name = "assemblyName")] public string? AssemblyName { get; set; }
        [DataMember(Name = "componentName")] public string? ComponentName { get; set; }
        [DataMember(Name = "configurationName")] public string? ConfigurationName { get; set; }
        [DataMember(Name = "transformJson")] public string? TransformJson { get; set; }
        [DataMember(Name = "customProperties")] public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>();
        [DataMember(Name = "version")] public int Version { get; set; } = 1;
        [DataMember(Name = "hasExpectedVersion")] public bool HasExpectedVersion { get; set; }
    }

    /// <summary>
    /// SolidWorks 实例注册响应。
    /// </summary>
    [DataContract]
    public sealed class SwInstanceResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
        [DataMember(Name = "conflict")] public bool Conflict { get; set; }
    }
}
