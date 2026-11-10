using System.Text.Json;
using GB_CadAndSWPlus_V.UploadApi.Filters;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GB_CadAndSWPlus_V_Server.Middleware;

/// <summary>统一异常响应；属性定义写接口不记录用户可控的异常内容或查询参数。</summary>
public static class GlobalExceptionHandler
{
    public static async Task HandleAsync(HttpContext context)
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        if (exceptionFeature?.Error != null)
        {
            if (ElementPropertyDefinitionLogProtection.ShouldHideRequest(context.Request))
            {
                // 异常消息也可能包含查询参数，故仅记录异常类型而非异常对象。
                logger.LogError(
                    "未处理的 API 异常。TraceId={TraceId}; Method={Method}; Path={Path}; Query={Query}; ExceptionType={ExceptionType}",
                    context.TraceIdentifier, context.Request.Method, context.Request.Path, "(已隐藏)",
                    exceptionFeature.Error.GetType().FullName);
            }
            else
            {
                logger.LogError(exceptionFeature.Error,
                    "未处理的 API 异常。TraceId={TraceId}; Method={Method}; Path={Path}; Query={Query}",
                    context.TraceIdentifier, context.Request.Method, context.Request.Path,
                    context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty);
            }
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success = false,
            message = "服务器内部错误，请联系管理员。"
        })).ConfigureAwait(false);
    }
}
