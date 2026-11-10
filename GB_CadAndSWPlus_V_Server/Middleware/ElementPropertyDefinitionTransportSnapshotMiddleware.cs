using GB_CadAndSWPlus_V.UploadApi.Filters;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.Extensions.Logging;

namespace GB_CadAndSWPlus_V_Server.Middleware;

/// <summary>捕获本应用转发头处理前的 IIS 服务器变量判定。</summary>
internal sealed class ElementPropertyDefinitionTransportSnapshot
{
    public ElementPropertyDefinitionTransportSnapshot(bool isIisHttps, bool hasForwardingHeaders)
    {
        IsIisHttps = isIisHttps;
        HasForwardingHeaders = hasForwardingHeaders;
    }

    public bool IsIisHttps { get; }
    public bool HasForwardingHeaders { get; }
}

public sealed class ElementPropertyDefinitionTransportSnapshotMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServer _server;
    private readonly ILogger<ElementPropertyDefinitionTransportSnapshotMiddleware> _logger;

    public ElementPropertyDefinitionTransportSnapshotMiddleware(RequestDelegate next, IServer server,
        ILogger<ElementPropertyDefinitionTransportSnapshotMiddleware> logger)
    {
        _next = next;
        _server = server;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (ElementPropertyDefinitionLogProtection.ShouldHideRequest(context.Request))
        {
            bool hasForwardingHeaders = context.Request.Headers.Keys.Any(header =>
                header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                || header.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                || header.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase)
                || header.Equals("Via", StringComparison.OrdinalIgnoreCase)
                || header.Equals("CF-Connecting-IP", StringComparison.OrdinalIgnoreCase)
                || header.Equals("True-Client-IP", StringComparison.OrdinalIgnoreCase));
            bool isIisHttps = false;
            try
            {
                // 只接受 IIS 进程内服务器提供的 HTTPS 变量，不使用请求协议或转发头。
                var iisEnvironment = _server.Features.Get<IIISEnvironmentFeature>();
                var variables = context.Features.Get<IServerVariablesFeature>();
                isIisHttps = iisEnvironment != null && variables != null
                    && string.Equals(variables["HTTPS"], "ON", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // 原因可能包含敏感内容；记录安全摘要并保持默认拒绝。
                _logger.LogWarning("读取 IIS HTTPS 服务器变量失败，属性定义写入将被拒绝。");
            }
            context.Features.Set(new ElementPropertyDefinitionTransportSnapshot(isIisHttps, hasForwardingHeaders));
        }
        return _next(context);
    }
}
