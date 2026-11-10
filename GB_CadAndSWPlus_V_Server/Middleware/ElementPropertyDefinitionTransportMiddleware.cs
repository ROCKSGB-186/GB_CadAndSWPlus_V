using GB_CadAndSWPlus_V.UploadApi.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GB_CadAndSWPlus_V_Server.Middleware;

/// <summary>验证属性定义写接口的 IIS HTTPS 服务器变量快照。</summary>
public sealed class ElementPropertyDefinitionTransportMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ElementPropertyDefinitionTransportMiddleware> _logger;

    public ElementPropertyDefinitionTransportMiddleware(RequestDelegate next, ILogger<ElementPropertyDefinitionTransportMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var snapshot = context.Features.Get<ElementPropertyDefinitionTransportSnapshot>();
        if (ElementPropertyDefinitionLogProtection.ShouldHideRequest(context.Request)
            && (snapshot == null || !snapshot.IsIisHttps || snapshot.HasForwardingHeaders))
        {
            // 不记录请求头、查询参数或正文，避免记录凭据。
            _logger.LogWarning("统一构件属性定义写入因连接不安全或存在代理转发信息被拒绝。");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"success\":false,\"message\":\"此接口仅允许直接 TLS 连接。\"}").ConfigureAwait(false);
            return;
        }
        await _next(context).ConfigureAwait(false);
    }

}
