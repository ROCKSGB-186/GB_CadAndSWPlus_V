namespace GB_CadAndSWPlus_V.UploadApi.Models;

/// <summary>
/// 查询 SolidWorks 平台模板的请求参数。
/// </summary>
public sealed class SwPlatformTemplateRequest
{
    /// <summary>
    /// 平台业务代码，例如 GB-PIPELINE、GB-FLANGE。
    /// </summary>
    public string PlatformCode { get; init; } = string.Empty;
}

/// <summary>
/// SolidWorks 平台模板查询结果。
/// </summary>
public sealed class SwPlatformTemplateResponse
{
    /// <summary>
    /// 是否成功找到已配置的平台模板。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 返回给客户端的中文说明。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 平台业务代码。
    /// </summary>
    public string PlatformCode { get; init; } = string.Empty;

    /// <summary>
    /// 模板附件的数据库文件 ID；客户端不直接使用该 ID 访问数据库。
    /// </summary>
    public long? FileId { get; init; }

    /// <summary>
    /// 模板原始文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// 模板附件大小，单位为字节。
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 模板版本标识，用于后续缓存校验。
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// 模板文件下载 API 路径。
    /// </summary>
    public string DownloadPath { get; init; } = string.Empty;
}
