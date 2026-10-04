using GB_CadAndSWPlus_V.UploadApi.Services;
using GB_CadAndSWPlus_V_Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using LogLevel = GB_CadAndSWPlus_V_Server.Services.LogLevel;

namespace GB_CadAndSWPlus_V.UploadApi.Filters
{
    /// <summary>
    /// 操作日志过滤器：记录每次 API 请求的详细信息，便于分析客户端/服务器端问题。
    /// 记录内容：时间、客户端IP、请求方法/路径、参数摘要、响应状态码、耗时、异常信息。
    /// </summary>
    public class OperationLogFilter : IAsyncActionFilter
    {
        private readonly HourlyFileLogger _logger;

        public OperationLogFilter(HourlyFileLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// 异步执行操作
        /// </summary>
        /// <param name="context"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var sw = Stopwatch.StartNew();
            var request = context.HttpContext.Request;

            // 1. 获取客户端信息
            string clientIp = GetClientIp(context.HttpContext);
            string method = request.Method;
            string path = request.Path;
            string queryString = request.QueryString.HasValue ? request.QueryString.Value! : string.Empty;
            string traceId = context.HttpContext.TraceIdentifier;
            string platform = GetPlatform(context.HttpContext);

            // 2. 获取关键请求参数摘要（避免日志过大，只取前 500 字符）
            string? bodySummary = null;
            if ((request.ContentLength ?? 0) > 0 && (method == "POST" || method == "PUT"))
            {
                try
                {
                    request.EnableBuffering();
                    using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                    string body = await reader.ReadToEndAsync();
                    request.Body.Position = 0;
                    bodySummary = SanitizeBody(body);
                }
                catch
                {
                    bodySummary = "(无法读取请求体)";
                }
            }

            // 3. 记录请求开始
            var sb = new StringBuilder();
            sb.AppendLine($"========== 请求开始 ==========");
            sb.AppendLine($"  客户端IP    : {clientIp}");
            sb.AppendLine($"  TraceId     : {traceId}");
            sb.AppendLine($"  请求方式    : {method}");
            sb.AppendLine($"  请求路径    : {path}");
            if (!string.IsNullOrEmpty(queryString))
                sb.AppendLine($"  查询字符串  : {queryString}");
            if (bodySummary != null)
                sb.AppendLine($"  请求体摘要  : {bodySummary}");
            sb.AppendLine($"  请求时间    : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

            sb.AppendLine($"  内容类型    : {request.ContentType ?? string.Empty}");
            sb.AppendLine($"  用户标识    : {context.HttpContext.User?.Identity?.Name ?? "匿名"}");

            _logger.WriteLineForPlatform(platform, LogLevel.Info, sb.ToString());

            // 4. 执行实际动作
            Exception? occurredException = null;
            ActionExecutedContext? resultContext = null;

            try
            {
                resultContext = await next();
            }
            catch (Exception ex)
            {
                occurredException = ex;
            }

            sw.Stop();

            // 5. 记录响应结果
            var resultSb = new StringBuilder();
            resultSb.AppendLine($"========== 请求结束 ==========");
            resultSb.AppendLine($"  请求路径    : {path}");
            resultSb.AppendLine($"  TraceId     : {traceId}");
            resultSb.AppendLine($"  耗时(ms)    : {sw.ElapsedMilliseconds}");
            resultSb.AppendLine($"  客户端IP    : {clientIp}");

            if (occurredException != null)
            {
                // ---- 服务器端异常 ----
                resultSb.AppendLine($"  结果        : ❌ 服务器端异常");
                resultSb.AppendLine($"  异常类型    : {occurredException.GetType().FullName}");
                resultSb.AppendLine($"  异常消息    : {occurredException.Message}");
                resultSb.AppendLine($"  堆栈摘要    : {occurredException.StackTrace?[..Math.Min(occurredException.StackTrace?.Length ?? 0, 1000)]}");
                _logger.WriteLineForPlatform(platform, LogLevel.Error, resultSb.ToString());
            }

            else if (resultContext != null)
            {
                // ActionFilter 执行结束时，ObjectResult 可能还没有经过响应执行器写入 Response.StatusCode。
                // 因此优先读取控制器返回的 StatusCodeObjectResult，避免把 HTTP 500 误记为 HTTP 200。
                int statusCode = resultContext.Result is ObjectResult objectResult
                    && objectResult.StatusCode.HasValue
                    ? objectResult.StatusCode.Value
                    : resultContext.HttpContext.Response.StatusCode;

                if (statusCode >= 200 && statusCode < 300)
                {
                    resultSb.AppendLine($"  结果        : ✅ 成功 (HTTP {statusCode})");
                    _logger.WriteLineForPlatform(platform, LogLevel.Info, resultSb.ToString());
                }
                else if (statusCode >= 400 && statusCode < 500)
                {
                    // 4xx = 客户端问题
                    resultSb.AppendLine($"  结果        : ⚠️ 客户端错误 (HTTP {statusCode})");
                    if (resultContext.Result != null)
                        resultSb.AppendLine($"  返回详情    : {resultContext.Result}");
                    _logger.WriteLineForPlatform(platform, LogLevel.Warning, resultSb.ToString());
                }
                else if (statusCode >= 500)
                {
                    // 5xx = 服务器端问题
                    resultSb.AppendLine($"  结果        : ❌ 服务器端错误 (HTTP {statusCode})");
                    if (resultContext.Exception != null)
                    {
                        resultSb.AppendLine($"  异常类型    : {resultContext.Exception.GetType().FullName}");
                        resultSb.AppendLine($"  异常消息    : {resultContext.Exception.Message}");
                    }
                    _logger.WriteLineForPlatform(platform, LogLevel.Error, resultSb.ToString());
                }
                else
                {
                    resultSb.AppendLine($"  结果        : HTTP {statusCode}");
                    _logger.WriteLineForPlatform(platform, LogLevel.Info, resultSb.ToString());
                }
            }

            // 如果是服务器端异常，重新抛出以让框架处理
            if (occurredException != null)
                throw occurredException;
        }

        private static string SanitizeBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                Dictionary<string, object?> sanitized = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    sanitized[property.Name] = IsSensitiveName(property.Name)
                        ? "***"
                        : property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.Clone();
                }

                string result = JsonSerializer.Serialize(sanitized);
                return result.Length > 500 ? result.Substring(0, 500) + "..." : result;
            }
            catch
            {
                return "(非 JSON 请求体，已隐藏)";
            }
        }

        private static bool IsSensitiveName(string name)
        {
            return name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pwd", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || name.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || name.Contains("configvalue", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取客户端真实 IP（考虑反向代理 / 负载均衡）
        /// </summary>
        private static string GetClientIp(HttpContext context)
        {
            // 优先从 X-Forwarded-For 头获取（适用于 Nginx/反向代理场景）
            string? forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            // 其次从 X-Real-IP 头获取
            string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
                return realIp.Trim();

            // 最后使用直接连接的远程 IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "未知";
        }

        private static string GetPlatform(HttpContext context)
        {
            string platform = context.Request.Headers["X-Client-Platform"].FirstOrDefault();
            return string.Equals(platform, "SOLIDWORKS", StringComparison.OrdinalIgnoreCase)
                ? "SOLIDWORKS"
                : "CAD";
        }
    }
}