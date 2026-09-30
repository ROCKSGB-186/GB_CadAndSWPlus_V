using System.Text.Json.Serialization;

namespace GB_CadAndSWPlus_V.UploadApi.Models;

public sealed class SwInstanceRegisterRequest
{
    public string SwInstanceId { get; init; } = string.Empty;
    public string PidInstanceId { get; init; } = string.Empty;
    public int? LibraryItemId { get; init; }
    public string? DocumentPath { get; init; }
    public string? DocumentName { get; init; }
    public string? AssemblyName { get; init; }
    public string? ComponentName { get; init; }
    public string? ConfigurationName { get; init; }
    public string? TransformJson { get; init; }
    public Dictionary<string, string> CustomProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; init; } = 1;
    public bool HasExpectedVersion { get; init; }
}

public sealed class SwInstanceDto
{
    public string SwInstanceId { get; init; } = string.Empty;
    public string PidInstanceId { get; init; } = string.Empty;
    public int? LibraryItemId { get; init; }
    public string DocumentPath { get; init; } = string.Empty;
    public string DocumentName { get; init; } = string.Empty;
    public string AssemblyName { get; init; } = string.Empty;
    public string ComponentName { get; init; } = string.Empty;
    public string ConfigurationName { get; init; } = string.Empty;
    public string TransformJson { get; init; } = string.Empty;
    public Dictionary<string, string> CustomProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; init; }
    public string SyncStatus { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
}

public sealed class SwInstanceResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public SwInstanceDto? Instance { get; init; }
    public bool Conflict { get; init; }
}
