using GB_CadAndSWPlus_V.UploadApi.Filters;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GB_CadAndSWPlus_V_Server.Middleware;

/// <summary>阻止该接口的异常对象被框架异常日志直接记录。</summary>
public sealed class ElementPropertyDefinitionExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ElementPropertyDefinitionExceptionMiddleware> _logger;

    public ElementPropertyDefinitionExceptionMiddleware(RequestDelegate next, ILogger<ElementPropertyDefinitionExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ElementPropertyDefinitionLogProtection.ShouldHideRequest(context.Request))
        {
            // 响应尚未开始时使用相同的全局安全响应，不把原始异常交给框架日志。
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = ex });
                await GlobalExceptionHandler.HandleAsync(context).ConfigureAwait(false);
                return;
            }

            // 响应已开始无法安全替换，仅记录类型并抛出不含原始内容的异常。
            _logger.LogError("属性定义写入请求响应开始后发生异常。异常类型={ExceptionType}", ex.GetType().FullName);
            throw new InvalidOperationException("属性定义写入请求处理失败。");
        }
    }
}
