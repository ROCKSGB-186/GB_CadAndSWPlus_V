namespace GB_CadAndSWPlus_V.UploadApi.Models;

public sealed class ElementDefinitionRequest
{
    public string ElementCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public int? CategoryId { get; init; }
    public string? ElementType { get; init; }
    public string? Discipline { get; init; }
    public string? StandardNo { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = "DRAFT";
}

public sealed class ElementDefinitionDto
{
    public long Id { get; init; }
    public string ElementCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int? CategoryId { get; init; }
    public string ElementType { get; init; } = string.Empty;
    public string Discipline { get; init; } = string.Empty;
    public string StandardNo { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "DRAFT";
    public int CurrentVersion { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class ElementDefinitionListResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ElementDefinitionDto> Elements { get; init; } = Array.Empty<ElementDefinitionDto>();
}

public sealed class ElementDefinitionResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ElementDefinitionDto? Element { get; init; }
}
