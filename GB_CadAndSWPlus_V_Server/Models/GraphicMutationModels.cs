namespace GB_CadAndSWPlus_V.UploadApi.Models;

public sealed class GraphicMutationRequest
{
    public string? FileName { get; set; }
    public string? DisplayName { get; set; }
    public string? BlockName { get; set; }
    public string? LayerName { get; set; }
    public int? ColorIndex { get; set; }
    public double? Scale { get; set; }
    public string? Title { get; set; }
    public string? Keywords { get; set; }
    public string? Description { get; set; }
    public string? UpdatedBy { get; set; }
    public string? ConfigName { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GraphicDeleteResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int DeletedAttributeRows { get; init; }
    public int DeletedStorageRows { get; init; }
    public int DeletedPhysicalFiles { get; init; }
}
