namespace GB_CadAndSWPlus_V.UploadApi.Models;

/// <summary>新增构件属性定义的输入；编码、名称和类型必须显式提供。</summary>
public sealed class CreateElementPropertyDefinitionRequest
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? DataType { get; init; }
    public string? Unit { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
}

public sealed class ElementPropertyRequest
{
    public long ElementDefinitionId { get; init; }
    public string PropertyCode { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string DataType { get; init; } = "TEXT";
    public string? Unit { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>统一构件属性定义及当前编辑值。</summary>
public sealed class ElementPropertyValueDto : ElementPropertyDto
{
    public string? ValueText { get; init; }
    public decimal? ValueNumber { get; init; }
    public bool? ValueBool { get; init; }
}

/// <summary>统一构件属性值保存请求。</summary>
public sealed class ElementPropertyValueRequest
{
    public long PropertyDefinitionId { get; init; }
    public string? ValueText { get; init; }
}

public class ElementPropertyDto
{
    public long Id { get; init; }
    public long ElementDefinitionId { get; init; }
    public string PropertyCode { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string DataType { get; init; } = "TEXT";
    public string Unit { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

public class SolidWorksMetadataRequest
{
    public string? ModelType { get; init; }
    public string? DefaultConfiguration { get; init; }
    public string? CoordinateSystemName { get; init; }
    public string? MateRuleJson { get; init; }
    public int PortCount { get; init; }
    public string ModelStatus { get; init; } = "DRAFT";
}

public sealed class SolidWorksMetadataDto : SolidWorksMetadataRequest
{
    public long ElementDefinitionId { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class ElementMetadataResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
}
