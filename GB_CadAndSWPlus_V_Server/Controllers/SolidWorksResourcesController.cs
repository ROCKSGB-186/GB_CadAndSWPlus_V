using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// SolidWorks 模型和预览附件管理接口。
/// 分类仍使用统一的 CAD 分类接口，模型资源只保存 SOLIDWORKS 平台记录。
/// </summary>
[ApiController]
[Route("api/solidworks-resources")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class SolidWorksResourcesController : ControllerBase
{
    private readonly ElementResourceService _resourceService;
    private readonly SolidWorksResourceService _solidWorksResourceService;
    private readonly ILogger<SolidWorksResourcesController> _logger;

    public SolidWorksResourcesController(
        ElementResourceService resourceService,
        SolidWorksResourceService solidWorksResourceService,
        ILogger<SolidWorksResourcesController> logger)
    {
        _resourceService = resourceService ?? throw new ArgumentNullException(nameof(resourceService));
        _solidWorksResourceService = solidWorksResourceService ?? throw new ArgumentNullException(nameof(solidWorksResourceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>下载当前 SolidWorks 模型到客户端临时缓存，客户端不能直接访问服务器物理路径。</summary>
    [HttpGet("resources/{resourceId:long}/model")]
    public async Task<IActionResult> DownloadModelAsync(long resourceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("收到 SolidWorks 模型下载请求。ResourceId={ResourceId}", resourceId);
        _logger.LogInformation("开始调用 SolidWorks 模型存储服务。ResourceId={ResourceId}", resourceId);
        SolidWorksResourceService.SolidWorksModelFile? file = await _solidWorksResourceService.OpenModelAsync(resourceId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SolidWorks 模型存储服务调用完成。ResourceId={ResourceId}, HasFile={HasFile}", resourceId, file != null);
        if (file == null)
        {
            _logger.LogWarning("SolidWorks 模型下载失败，文件不存在或不可用。ResourceId={ResourceId}", resourceId);
            return NotFound(new { success = false, message = "SolidWorks 模型文件不存在。" });
        }
        _logger.LogInformation("SolidWorks 模型下载响应已生成。ResourceId={ResourceId}, FileName={FileName}", resourceId, file.FileName);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    /// <summary>替换已有 SolidWorks 资源的模型文件。</summary>
    [HttpPut("resources/{resourceId:long}/model")]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<ActionResult<ElementResourceResponse>> ReplaceModelAsync(
        long resourceId,
        [FromForm] IFormFile file,
        [FromForm] string? resourceName,
        [FromForm] string? configurationName,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以替换 SolidWorks 模型。" });
        try
        {
            ElementResourceResponse response = await _solidWorksResourceService
                .ReplaceModelAsync(resourceId, file, resourceName, configurationName, operatorName, cancellationToken)
                .ConfigureAwait(false);
            return response.Success ? Ok(response) : NotFound(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ElementResourceResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>更新 SolidWorks 模型资源名称、配置、状态和默认标识。</summary>
    [HttpPut("resources/{resourceId:long}/details")]
    public async Task<ActionResult<SolidWorksResourceDetailsResponse>> UpdateDetailsAsync(
        long resourceId,
        [FromBody] SolidWorksResourceDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以更新 SolidWorks 模型信息。" });
        if (request.ElementDefinitionId <= 0)
            return BadRequest(new SolidWorksResourceDetailsResponse { Success = false, Message = "构件 ID 必须大于 0。" });

        ElementResourceResponse current = await _resourceService.GetAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (!current.Success || current.Resource == null || current.Resource.Platform != "SOLIDWORKS")
            return NotFound(new SolidWorksResourceDetailsResponse { Success = false, Message = "SolidWorks 模型资源不存在。" });

        ElementResourceResponse response = await _resourceService.UpdateAsync(resourceId, new ElementResourceRequest
        {
            ElementDefinitionId = current.Resource.ElementDefinitionId,
            Platform = "SOLIDWORKS",
            ResourceType = string.IsNullOrWhiteSpace(request.ResourceType) ? current.Resource.ResourceType : request.ResourceType,
            ResourceName = request.ResourceName,
            ConfigurationName = request.ConfigurationName,
            ResourcePath = string.IsNullOrWhiteSpace(request.ResourcePath) ? current.Resource.ResourcePath : request.ResourcePath,
            FileHash = string.IsNullOrWhiteSpace(request.FileHash) ? current.Resource.FileHash : request.FileHash,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "DRAFT" : request.Status,
            IsDefault = request.IsDefault ?? false
        }, operatorName, cancellationToken).ConfigureAwait(false);
        return response.Success
            ? Ok(new SolidWorksResourceDetailsResponse { Success = true, Message = "SolidWorks 模型信息更新成功。", Resource = response.Resource })
            : NotFound(new SolidWorksResourceDetailsResponse { Success = false, Message = response.Message });
    }

    /// <summary>查询指定统一构件的 SolidWorks 资源。</summary>
    [HttpGet("elements/{elementId:long}")]
    public async Task<ActionResult<ElementResourceListResponse>> ListAsync(long elementId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始查询 SolidWorks 资源。ElementId={ElementId}", elementId);
        try
        {
            ElementResourceListResponse response = await _resourceService.ListAsync(elementId, "SOLIDWORKS", cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 资源查询完成。ElementId={ElementId}, ResourceCount={ResourceCount}", elementId, response.Resources?.Count ?? 0);
            return Ok(response);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 资源查询参数无效。ElementId={ElementId}", elementId);
            return BadRequest(new ElementResourceListResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>读取图片或轻量三维预览附件，客户端不能直接访问服务器物理路径。</summary>
    [HttpGet("attachments/{attachmentId:long}/content")]
    public async Task<IActionResult> DownloadAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("收到 SolidWorks 预览附件下载请求。AttachmentId={AttachmentId}", attachmentId);
        SolidWorksResourceService.SolidWorksAttachmentFile? file = await _solidWorksResourceService.OpenAttachmentAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (file == null)
        {
            _logger.LogWarning("SolidWorks 预览附件不存在。AttachmentId={AttachmentId}", attachmentId);
            return NotFound(new { success = false, message = "预览附件不存在。" });
        }
        _logger.LogInformation("SolidWorks 预览附件下载响应已生成。AttachmentId={AttachmentId}, FileName={FileName}", attachmentId, file.FileName);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>查询指定 SolidWorks 资源的当前预览附件。</summary>
    [HttpGet("resources/{resourceId:long}/attachments")]
    public async Task<ActionResult<SolidWorksResourceAttachmentListResponse>> ListAttachmentsAsync(long resourceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始查询 SolidWorks 预览附件。ResourceId={ResourceId}", resourceId);
        try
        {
            IReadOnlyList<SolidWorksResourceAttachmentDto> attachments = await _solidWorksResourceService.ListAttachmentsAsync(resourceId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 预览附件查询完成。ResourceId={ResourceId}, AttachmentCount={AttachmentCount}", resourceId, attachments.Count);
            return Ok(new SolidWorksResourceAttachmentListResponse { Success = true, Message = "预览附件查询成功。", Attachments = attachments });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 预览附件查询参数无效。ResourceId={ResourceId}", resourceId);
            return BadRequest(new SolidWorksResourceAttachmentListResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>上传 SolidWorks 零件或装配体模型。</summary>
    [HttpPost("elements/{elementId:long}/model")]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<ActionResult<ElementResourceResponse>> UploadModelAsync(
        long elementId,
        [FromForm] IFormFile file,
        [FromForm] string? resourceName,
        [FromForm] string? configurationName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始上传 SolidWorks 模型。ElementId={ElementId}, FileSize={FileSize}", elementId, file?.Length ?? 0);
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
        {
            _logger.LogWarning("SolidWorks 模型上传被拒绝，当前账号不是管理员。ElementId={ElementId}", elementId);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以上传 SolidWorks 模型。" });
        }

        try
        {
            ElementResourceResponse response = await _solidWorksResourceService
                .UploadModelAsync(elementId, file, resourceName, configurationName, operatorName, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 模型上传处理完成。ElementId={ElementId}, ResourceId={ResourceId}, Success={Success}", elementId, response.Resource?.Id, response.Success);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 模型上传参数无效。ElementId={ElementId}", elementId);
            return BadRequest(new ElementResourceResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>上传或替换 SolidWorks 模型的图片或轻量三维预览附件。</summary>
    [HttpPost("resources/{resourceId:long}/attachments")]
    [RequestSizeLimit(100L * 1024L * 1024L)]
    public async Task<ActionResult<SolidWorksResourceAttachmentResponse>> UploadAttachmentAsync(
        long resourceId,
        [FromForm] IFormFile file,
        [FromForm] string attachmentType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始上传 SolidWorks 预览附件。ResourceId={ResourceId}, AttachmentType={AttachmentType}, FileSize={FileSize}", resourceId, attachmentType, file?.Length ?? 0);
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
        {
            _logger.LogWarning("SolidWorks 预览附件上传被拒绝，当前账号不是管理员。ResourceId={ResourceId}", resourceId);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以上传 SolidWorks 预览附件。" });
        }

        try
        {
            SolidWorksResourceAttachmentResponse response = await _solidWorksResourceService
                .UploadAttachmentAsync(resourceId, file, attachmentType, operatorName, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 预览附件上传处理完成。ResourceId={ResourceId}, Success={Success}", resourceId, response.Success);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 预览附件上传参数无效。ResourceId={ResourceId}", resourceId);
            return BadRequest(new SolidWorksResourceAttachmentResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>归档 SolidWorks 模型资源。</summary>
    [HttpDelete("resources/{resourceId:long}")]
    public async Task<ActionResult> ArchiveResourceAsync(long resourceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始归档 SolidWorks 模型资源。ResourceId={ResourceId}", resourceId);
        if (!StandardManagementAuthorization.IsAdministrator(Request, out _))
        {
            _logger.LogWarning("SolidWorks 模型资源归档被拒绝，当前账号不是管理员。ResourceId={ResourceId}", resourceId);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以归档 SolidWorks 模型。" });
        }

        try
        {
            bool archived = await _resourceService.DeleteAsync(resourceId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 模型资源归档处理完成。ResourceId={ResourceId}, Archived={Archived}", resourceId, archived);
            return archived
                ? Ok(new { success = true, message = "SolidWorks 模型已归档。" })
                : NotFound(new { success = false, message = "SolidWorks 模型资源不存在。" });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 模型资源归档参数无效。ResourceId={ResourceId}", resourceId);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>归档当前预览附件，文件记录保留以便审计。</summary>
    [HttpDelete("attachments/{attachmentId:long}")]
    public async Task<ActionResult> ArchiveAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始归档 SolidWorks 预览附件。AttachmentId={AttachmentId}", attachmentId);
        if (!StandardManagementAuthorization.IsAdministrator(Request, out _))
        {
            _logger.LogWarning("SolidWorks 预览附件归档被拒绝，当前账号不是管理员。AttachmentId={AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以删除 SolidWorks 预览附件。" });
        }

        try
        {
            bool archived = await _solidWorksResourceService.ArchiveAttachmentAsync(attachmentId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 预览附件归档处理完成。AttachmentId={AttachmentId}, Archived={Archived}", attachmentId, archived);
            return archived
                ? Ok(new { success = true, message = "SolidWorks 预览附件已归档。" })
                : NotFound(new { success = false, message = "SolidWorks 预览附件不存在。" });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 预览附件归档参数无效。AttachmentId={AttachmentId}", attachmentId);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
