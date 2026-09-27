namespace GB_CadAndSWPlus_V.UploadApi.Models;

using System.Text.Json.Serialization;

/// <summary>
/// 图层字典表初始化结果。
/// </summary>
public sealed class LayerDictionaryInitializationResponse
{
    /// <summary>
    /// 是否初始化成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 面向客户端的结果说明，不包含数据库连接信息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 图层映射项。
/// </summary>
public sealed class LayerMappingDto
{
    public string OriginalLayer { get; set; } = string.Empty;
    public string DicLayer { get; set; } = string.Empty;
}

/// <summary>
/// 图层字典数据项，与客户端 LayerDictionaryHelper 保持 JSON 字段兼容。
/// </summary>
public sealed class LayerDictionaryEntryDto
{
    public int Id { get; set; }
    public int Seq { get; set; }
    public string Major { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string MappingsJson { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int IsActive { get; set; } = 1;

    [JsonIgnore]
    public List<LayerMappingDto> Mappings { get; set; } = new();
}

/// <summary>
/// 保存个人图层字典请求。
/// </summary>
public sealed class SaveLayerDictionaryRequest
{
    public string Username { get; set; } = string.Empty;
    public List<LayerDictionaryEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// 删除图层字典请求。
/// </summary>
public sealed class DeleteLayerDictionaryRequest
{
    public List<int> Ids { get; set; } = new();
}

/// <summary>
/// 图层字典写入结果。
/// </summary>
public sealed class LayerDictionaryMutationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AffectedRows { get; set; }
}
