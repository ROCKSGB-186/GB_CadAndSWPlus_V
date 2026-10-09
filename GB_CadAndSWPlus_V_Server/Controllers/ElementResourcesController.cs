using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;


/// <summary>
/// 中心构件资源管理控制器，提供对构件资源的增删查操作。
/// </summary>
[ApiController]
[Route("api/elements/{elementId:long}/resources")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class ElementResourcesController : ControllerBase
{
    /// <summary>
    /// 中心构件资源服务，用于处理构件资源的业务逻辑。
    /// </summary>
    private readonly ElementResourceService _service;
    /// <summary>
    /// 初始化 <see cref="ElementResourcesController"/> 类的新实例。
    /// </summary>
    /// <param name="service">中心构件资源服务实例。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="service"/> 为 <c>null</c> 时抛出。</exception>
    public ElementResourcesController(ElementResourceService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service)); // 确保服务实例不为 null
    }

    /// <summary>
    /// 获取指定构件的资源列表。
    /// </summary>
    /// <param name="elementId">构件 ID。</param>
    /// <param name="platform">平台名称，可选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回构件资源列表的响应结果。</returns>
    [HttpGet]
    public async Task<ActionResult<ElementResourceListResponse>> ListAsync(long elementId, [FromQuery] string? platform, CancellationToken cancellationToken)
    {
        // 检查用户是否具有管理员权限
        try { return Ok(await _service.ListAsync(elementId, platform, cancellationToken).ConfigureAwait(false)); }
        // 处理参数超出范围的异常
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new ElementResourceListResponse { Success = false, Message = ex.Message }); }
    }

    /// <summary>
    /// 创建新的构件资源。
    /// </summary>
    /// <param name="elementId">构件 ID。</param>
    /// <param name="request">构件资源请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回创建的构件资源的响应结果。</returns>
    [HttpPost]
    public async Task<ActionResult<ElementResourceResponse>> CreateAsync(long elementId, [FromBody] ElementResourceRequest request, CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以绑定构件资源。" });
        if (request.ElementDefinitionId != elementId)
            return BadRequest(new ElementResourceResponse { Success = false, Message = "路径中的构件 ID 与请求体不一致。" });
        try { return Ok(await _service.CreateAsync(request, operatorName, cancellationToken).ConfigureAwait(false)); }
        catch (ArgumentException ex) { return BadRequest(new ElementResourceResponse { Success = false, Message = ex.Message }); }
    }

    [HttpDelete("{resourceId:long}")]
    public async Task<ActionResult> DeleteAsync(long elementId, long resourceId, CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out _))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以归档构件资源。" });
        try
        {
            bool success = await _service.DeleteAsync(resourceId, cancellationToken).ConfigureAwait(false);
            return success ? Ok(new { success = true, message = "构件资源已归档。" }) : NotFound(new { success = false, message = "构件资源不存在。" });
        }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
