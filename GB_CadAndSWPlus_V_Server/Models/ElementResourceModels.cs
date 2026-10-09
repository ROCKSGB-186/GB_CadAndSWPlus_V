namespace GB_CadAndSWPlus_V.UploadApi.Models;

public sealed class ElementResourceRequest
{
    public long ElementDefinitionId { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public int? FileStorageId { get; init; }
    public string? ResourceName { get; init; }
    public string? ResourcePath { get; init; }
    public string? FileHash { get; init; }
    public string? ConfigurationName { get; init; }
    public string Status { get; init; } = "DRAFT";
    public bool IsDefault { get; init; }
}

public sealed class ElementResourceDto
{
    public long Id { get; init; }
    public long ElementDefinitionId { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public int? FileStorageId { get; init; }
    public string ResourceName { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string ResourcePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public int Version { get; init; }
    public string ConfigurationName { get; init; } = string.Empty;
    public string Status { get; init; } = "DRAFT";
    public bool IsDefault { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class ElementResourceListResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ElementResourceDto> Resources { get; init; } = Array.Empty<ElementResourceDto>();
}

public sealed class ElementResourceResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ElementResourceDto? Resource { get; init; }
}
