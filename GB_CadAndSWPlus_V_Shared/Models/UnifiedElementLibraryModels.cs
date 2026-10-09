using System;
using System.Collections.Generic;

namespace GB_CadAndSWPlus_V.Shared.Models
{
    /// <summary>
    /// 构件资源所属平台。分类和构件定义共用，资源按平台分开保存。
    /// </summary>
    public enum UnifiedElementPlatform
    {
        CAD,
        SOLIDWORKS
    }

    /// <summary>
    /// 构件平台资源类型。
    /// </summary>
    public enum UnifiedElementResourceType
    {
        DWG,
        SLDPRT,
        SLDASM,
        Preview,
        Other
    }

    /// <summary>
    /// 构件定义和资源的生命周期状态。
    /// </summary>
    public enum UnifiedElementStatus
    {
        DRAFT,
        REVIEW,
        PUBLISHED,
        DISABLED,
        ARCHIVED
    }

    /// <summary>
    /// CAD 与 SolidWorks 共用的构件业务身份。
    /// </summary>
    public sealed class UnifiedElementDefinitionDto
    {
        public long Id { get; set; }
        public string ElementCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string ElementType { get; set; } = string.Empty;
        public string Discipline { get; set; } = string.Empty;
        public string StandardNo { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = UnifiedElementStatus.DRAFT.ToString();
        public int CurrentVersion { get; set; } = 1;
        public string CreatedBy { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// 构件定义的 CAD/SolidWorks 平台资源关联。
    /// </summary>
    public sealed class UnifiedElementResourceDto
    {
        public long Id { get; set; }
        public long ElementDefinitionId { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public int? FileStorageId { get; set; }
        public string ResourceName { get; set; } = string.Empty;
        public string ResourcePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public string ConfigurationName { get; set; } = string.Empty;
        public string Status { get; set; } = UnifiedElementStatus.DRAFT.ToString();
        public bool IsDefault { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// 通用构件属性定义和值，供 CAD 和 SolidWorks 共同使用。
    /// </summary>
    public sealed class UnifiedElementPropertyDto
    {
        public long Id { get; set; }
        public long ElementDefinitionId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string DataType { get; set; } = "TEXT";
        public string Unit { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
        public string ValueText { get; set; } = string.Empty;
        public decimal? ValueNumber { get; set; }
        public bool? ValueBool { get; set; }
        public string Source { get; set; } = "MANUAL";
        public int Version { get; set; } = 1;
    }

    /// <summary>
    /// SolidWorks 构件专属元数据，不污染 CAD 图元字段。
    /// </summary>
    public sealed class SolidWorksElementDefinitionDto
    {
        public long ElementDefinitionId { get; set; }
        public string ModelType { get; set; } = string.Empty;
        public string DefaultConfiguration { get; set; } = string.Empty;
        public string CoordinateSystemName { get; set; } = string.Empty;
        public string MateRuleJson { get; set; } = string.Empty;
        public int PortCount { get; set; }
        public string ModelStatus { get; set; } = UnifiedElementStatus.DRAFT.ToString();
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// SolidWorks 模型连接点或端口。
    /// </summary>
    public sealed class SolidWorksConnectionPointDto
    {
        public long Id { get; set; }
        public long ElementDefinitionId { get; set; }
        public string PointCode { get; set; } = string.Empty;
        public string PointType { get; set; } = string.Empty;
        public string PositionJson { get; set; } = string.Empty;
        public string DirectionJson { get; set; } = string.Empty;
        public decimal? Diameter { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 统一构件查询响应。
    /// </summary>
    public sealed class UnifiedElementListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<UnifiedElementDefinitionDto> Elements { get; set; }
            = new List<UnifiedElementDefinitionDto>();
    }

    /// <summary>
    /// 新增或修改统一构件定义的请求。
    /// </summary>
    public sealed class UnifiedElementDefinitionRequest
    {
        public string ElementCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string ElementType { get; set; } = string.Empty;
        public string Discipline { get; set; } = string.Empty;
        public string StandardNo { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = UnifiedElementStatus.DRAFT.ToString();
    }

    /// <summary>
    /// 构件资源绑定请求。FileStorageId 可绑定已有 CAD 资源，SolidWorks 资源可先使用路径或后续上传接口绑定。
    /// </summary>
    public sealed class UnifiedElementResourceRequest
    {
        public string Platform { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public int? FileStorageId { get; set; }
        public string ResourceName { get; set; } = string.Empty;
        public string ResourcePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public string ConfigurationName { get; set; } = string.Empty;
        public string Status { get; set; } = UnifiedElementStatus.DRAFT.ToString();
        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// 构件详情响应。
    /// </summary>
    public sealed class UnifiedElementDetailsResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public UnifiedElementDefinitionDto Element { get; set; }
            = new UnifiedElementDefinitionDto();
        public IReadOnlyList<UnifiedElementResourceDto> Resources { get; set; }
            = new List<UnifiedElementResourceDto>();
        public IReadOnlyList<UnifiedElementPropertyDto> Properties { get; set; }
            = new List<UnifiedElementPropertyDto>();
        public SolidWorksElementDefinitionDto SolidWorks { get; set; }
            = new SolidWorksElementDefinitionDto();
        public IReadOnlyList<SolidWorksConnectionPointDto> ConnectionPoints { get; set; }
            = new List<SolidWorksConnectionPointDto>();
    }

    /// <summary>
    /// 通用构件操作响应。
    /// </summary>
    public sealed class UnifiedElementMutationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public UnifiedElementDefinitionDto Element { get; set; }
            = new UnifiedElementDefinitionDto();
    }

    /// <summary>
    /// 平台资源列表响应。
    /// </summary>
    public sealed class UnifiedElementResourceListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<UnifiedElementResourceDto> Resources { get; set; }
            = new List<UnifiedElementResourceDto>();
    }
}
