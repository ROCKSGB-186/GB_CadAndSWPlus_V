namespace GB_CadAndSWPlus_V.UploadApi.Models;

/// <summary>
/// SolidWorks 平台模板绑定检查结果。
/// </summary>
public sealed class SwPlatformTemplateBindingCheckResponse
{
    /// <summary>检查是否成功完成；不代表模板一定已配置。</summary>
    public bool Success { get; init; }

    /// <summary>面向管理员的中文检查说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>规范化后的平台代码。</summary>
    public string PlatformCode { get; init; } = string.Empty;

    /// <summary>配置中读取到的附件 ID。</summary>
    public long? ConfiguredFileId { get; init; }

    /// <summary>附件是否存在且未被删除。</summary>
    public bool FileExists { get; init; }

    /// <summary>附件是否为 .sldasm 或 .sldprt。</summary>
    public bool IsSolidWorksFile { get; init; }

    /// <summary>附件内容是否大于零字节。</summary>
    public bool HasContent { get; init; }

    /// <summary>附件原始文件名。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>附件大小，单位为字节。</summary>
    public long FileSize { get; init; }

    /// <summary>附件 SHA256 哈希。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>模板下载 API 路径。</summary>
    public string DownloadPath { get; init; } = string.Empty;

    /// <summary>模板是否可以供 SolidWorks 客户端使用。</summary>
    public bool IsUsable => FileExists && IsSolidWorksFile && HasContent;
}
