namespace GB_CadAndSWPlus_V.UploadApi.Models;

/// <summary>
/// 系统配置查询结果。
/// </summary>
public sealed class SystemConfigResponse
{
    public bool Success { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 系统配置写入请求。
/// </summary>
public sealed class SetSystemConfigRequest
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 系统配置写入结果。
/// </summary>
public sealed class SystemConfigMutationResponse
{
    public bool Success { get; set; }
    public int AffectedRows { get; set; }
    public string Message { get; set; } = string.Empty;
}
