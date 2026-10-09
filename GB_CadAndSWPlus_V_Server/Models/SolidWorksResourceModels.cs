namespace GB_CadAndSWPlus_V.UploadApi.Models;

/// <summary>SolidWorks 模型资源的管理字段。</summary>
public sealed class SolidWorksResourceDetailsRequest
{
    public long ElementDefinitionId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourcePath { get; init; }
    public string? FileHash { get; init; }
    public string? ResourceName { get; init; }
    public string? ConfigurationName { get; init; }
    public string? Status { get; init; }
    public bool? IsDefault { get; init; }
}

public sealed class SolidWorksResourceAttachmentListResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<SolidWorksResourceAttachmentDto> Attachments { get; init; } = Array.Empty<SolidWorksResourceAttachmentDto>();
}

/// <summary>SolidWorks 资源附件信息。</summary>
public sealed class SolidWorksResourceAttachmentDto
{
    public long Id { get; init; }
    public long ResourceId { get; init; }
    public string AttachmentType { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public int Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class SolidWorksResourceDetailsResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ElementResourceDto? Resource { get; init; }
}

public sealed class SolidWorksResourceAttachmentResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public SolidWorksResourceAttachmentDto? Attachment { get; init; }
}
