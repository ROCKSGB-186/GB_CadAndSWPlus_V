using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GB_CadAndSWPlus_V_Server.Services;
using FileLogLevel = GB_CadAndSWPlus_V_Server.Services.LogLevel;

namespace GB_CadAndSWPlus_V_Server.Middleware;

/// <summary>
/// 统一记录 API 请求生命周期，覆盖控制器过滤器未覆盖的请求，例如健康检查、Swagger 和异常处理中间件。
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HourlyFileLogger _logger;

    public RequestLoggingMiddleware(RequestDelegate next, HourlyFileLogger logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string traceId = context.TraceIdentifier;
        string clientIp = GetClientIp(context);
        string method = context.Request.Method;
        string path = context.Request.Path.ToString();
        string platform = GetPlatform(context);
        string query = context.Request.QueryString.HasValue
            ? SanitizeQuery(context.Request.Query)
            : string.Empty;
        string requestBody = await ReadBodySummaryAsync(context).ConfigureAwait(false);

        _logger.WriteLineForPlatform(platform, FileLogLevel.Info,
            $"[请求开始] 跟踪号={traceId}；客户端地址={clientIp}；请求方式={method}；请求路径={path}；" +
            $"查询参数={query}；内容长度={context.Request.ContentLength?.ToString() ?? "0"}；请求体={requestBody}");

        Exception? exception = null;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            FileLogLevel level = exception != null || context.Response.StatusCode >= 500
                ? FileLogLevel.Error
                : context.Response.StatusCode >= 400
                    ? FileLogLevel.Warning
                    : FileLogLevel.Info;

            string exceptionSummary = exception == null
                ? string.Empty
                : $"；异常类型={exception.GetType().FullName}；异常消息={SanitizeText(exception.Message)}";

            _logger.WriteLineForPlatform(platform, level,
                $"[请求结束] 跟踪号={traceId}；客户端地址={clientIp}；请求方式={method}；请求路径={path}；" +
                $"状态码={context.Response.StatusCode}；耗时毫秒={stopwatch.ElapsedMilliseconds}{exceptionSummary}");
        }
    }

    private static string GetPlatform(HttpContext context)
    {
        string platform = context.Request.Headers["X-Client-Platform"].FirstOrDefault();
        if (string.Equals(platform, "SOLIDWORKS", StringComparison.OrdinalIgnoreCase))
            return "SOLIDWORKS";

        return "CAD";
    }

    private static async Task<string> ReadBodySummaryAsync(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0
            || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method)
            || !(context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
            return "(none)";

        try
        {
            context.Request.EnableBuffering();
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            context.Request.Body.Position = 0;
            return SanitizeJsonBody(body);
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private static string SanitizeJsonBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty)";

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            string sanitized = SanitizeElement(document.RootElement);
            return sanitized.Length > 1000 ? sanitized[..1000] + "..." : sanitized;
        }
        catch
        {
            return "(non-json-hidden)";
        }
    }

    private static string SanitizeElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                values[property.Name] = IsSensitiveName(property.Name)
                    ? "***"
                    : JsonSerializer.Deserialize<object>(property.Value.GetRawText());
            }

            return JsonSerializer.Serialize(values);
        }

        return element.GetRawText();
    }

    private static string SanitizeQuery(IQueryCollection query)
    {
        IEnumerable<string> values = query.Select(pair =>
            $"{pair.Key}={(IsSensitiveName(pair.Key) ? "***" : string.Join(",", pair.Value))}");
        string result = string.Join("&", values);
        return result.Length > 500 ? result[..500] + "..." : result;
    }

    private static bool IsSensitiveName(string name)
    {
        return name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("pwd", StringComparison.OrdinalIgnoreCase)
            || name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || name.Contains("key", StringComparison.OrdinalIgnoreCase)
            || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || name.Contains("configvalue", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string value = text;
        value = System.Text.RegularExpressions.Regex.Replace(value, "(?i)(bearer\\s+)[^\\s;。]+", "$1***");
        value = System.Text.RegularExpressions.Regex.Replace(value, "(?i)((?:password|pwd|token|secret|authorization|access_token)\\s*[=:：]\\s*)[^;，。\\s]+", "$1***");
        value = System.Text.RegularExpressions.Regex.Replace(value, "(?i)(连接字符串\\s*[=:：]\\s*)[^；。]+", "$1***");
        return value;
    }

    private static string GetClientIp(HttpContext context)
    {
        string? forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();

        string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(realIp)
            ? realIp.Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
