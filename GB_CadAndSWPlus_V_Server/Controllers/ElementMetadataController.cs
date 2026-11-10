using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Net.Http.Headers;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>统一构件属性查询和保存接口。</summary>
[ApiController]
[Route("api/elements/{elementId:long}/properties")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class ElementMetadataController : ControllerBase
{
    private readonly ElementMetadataService _service;

    public ElementMetadataController(ElementMetadataService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpGet]
    public async Task<ActionResult<ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>>>> ListAsync(long elementId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ListAsync(elementId, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>> { Success = false, Message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult<ElementMetadataResponse<object>>> SaveAsync(long elementId, [FromBody] IReadOnlyList<ElementPropertyValueRequest> values, CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以保存构件属性。" });
        try
        {
            return Ok(await _service.SaveAsync(elementId, values ?? Array.Empty<ElementPropertyValueRequest>(), operatorName, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ElementMetadataResponse<object> { Success = false, Message = ex.Message });
        }
    }

    /// <summary>管理员批量新增属性定义，不创建或覆盖属性值。</summary>
    [HttpPost("definitions")]
    public async Task<ActionResult<ElementMetadataResponse<IReadOnlyList<ElementPropertyDto>>>> CreateDefinitionsAsync(
        long elementId, [FromBody] IReadOnlyList<CreateElementPropertyDefinitionRequest?>? definitions,
        [FromServices] AuthUserDepartmentService sessionService, [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        // 配置缺失时默认关闭；密钥验证不读取或记录明文配置。
        var keyResult = ElementPropertyDefinitionWriteKey.Validate(Request, configuration);
        if (keyResult == ElementPropertyDefinitionWriteKeyResult.NotConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "属性定义写入功能暂不可用。" });
        if (keyResult != ElementPropertyDefinitionWriteKeyResult.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "无权新增构件属性定义。" });

        // 仅信任服务器验证的会话，不读取客户端声称的操作用户名。
        var headers = Request.Headers.Authorization;
        if (headers.Count != 1 || headers[0] == null || headers[0]!.Length > 1024
            || !AuthenticationHeaderValue.TryParse(headers[0], out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter)
            || authorization.Parameter.Any(char.IsWhiteSpace))
            return Unauthorized(new { success = false, message = "请提供有效的登录会话。" });

        SessionResponse session;
        try
        {
            session = await sessionService.ValidateSessionAsync(authorization.Parameter, cancellationToken, hideDatabaseExceptionDetails: true).ConfigureAwait(false);
        }
        catch (DbException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "会话验证服务暂时不可用。" });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "会话验证服务暂时不可用。" });
        }
        catch (ArgumentException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "会话验证服务暂时不可用。" });
        }

        if (session?.Success != true || session.User == null)
            return Unauthorized(new { success = false, message = "登录会话无效，请重新登录。" });
        string operatorName = session.User.Username;
        if (!StandardManagementAuthorization.IsAdministratorName(operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以新增构件属性定义。" });
        try
        {
            return StatusCode(StatusCodes.Status201Created,
                await _service.CreateDefinitionsAsync(elementId, definitions, operatorName, cancellationToken).ConfigureAwait(false));
        }
        catch (ElementPropertyDefinitionValidationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (ElementPropertyElementNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (ElementPropertyDefinitionConflictException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ElementPropertyDefinitionDatabaseException ex) { return StatusCode(500, new { success = false, message = ex.Message }); }
    }
}
