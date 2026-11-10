global using Microsoft.AspNetCore.Http;
using GB_CadAndSWPlus_V.UploadApi.Controllers;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V_Server.Middleware;
using GB_CadAndSWPlus_V_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("应拒绝无效输入"); }
    static CreateElementPropertyDefinitionRequest Item(string? code = "DN", string? name = "通径", string? type = "TEXT", string? unit = null) =>
        new() { Code = code, Name = name, DataType = type, Unit = unit, IsRequired = true, SortOrder = 1 };
    static IReadOnlyList<ElementPropertyDto> Valid(params CreateElementPropertyDefinitionRequest?[] input) => ElementPropertyDefinitionCreation.Validate(1, input);

    static async Task Main()
    {
        Reject(() => ElementPropertyDefinitionCreation.Validate(0, new[] { Item() }));
        Reject(() => ElementPropertyDefinitionCreation.Validate(-1, new[] { Item() }));
        Reject(() => ElementPropertyDefinitionCreation.Validate(1, null));
        Reject(() => Valid());
        Reject(() => Valid((CreateElementPropertyDefinitionRequest?)null));
        Reject(() => Valid(Item(null)));
        Reject(() => Valid(Item(" ")));
        Reject(() => Valid(Item(new string('x', 129))));
        Reject(() => Valid(Item(name: null)));
        Reject(() => Valid(Item(name: new string('x', 256))));
        Reject(() => Valid(Item(type: null)));
        Reject(() => Valid(Item(type: "SELECT")));
        Reject(() => Valid(Item(type: "NUMBER")));
        Reject(() => Valid(Item(type: "BOOLEAN")));
        Reject(() => Valid(Item(unit: new string('x', 65))));
        Reject(() => Valid(Item(unit: "x\ny")));
        Reject(() => Valid(Item(), Item("dn", "另一名称")));
        Reject(() => Valid(Item(), Item("PN", "通径")));
        Reject(() => Valid(new CreateElementPropertyDefinitionRequest { Code = "X", Name = "名称", DataType = "TEXT" }));
        Reject(() => Valid(Enumerable.Range(0, 257).Select(i => Item("X" + i, "名称" + i)).ToArray()));
        var definitions = Valid(Item(" dn ", " 通径 ", "text"), Item("PN", "压力", "TEXT"), Item("MEDIUM", "介质", "TEXT"));
        Check(definitions[0].PropertyCode == "dn" && definitions[0].DataType == "TEXT", "应去空格并标准化类型");
        Check(Valid(Item(new string('x', 128), new string('y', 255), unit: new string('u', 64))).Count == 1, "边界长度应通过");
        Check(ElementPropertyDefinitionCreation.ValidateConnectionStrings(" same ", "same") == "same", "一致的连接配置应通过");
        Check(ElementPropertyDefinitionCreation.ValidateConnectionStrings(null, "same") == "same", "未配置专用连接时应使用通用连接");
        foreach (var pair in new (string? Typed, string? Common)[] { ("first", "second"), ("", "same"), (null, null), ("same", null), ("SAME", "same") })
        {
            try
            {
                ElementPropertyDefinitionCreation.ValidateConnectionStrings(pair.Typed, pair.Common);
                throw new Exception("不一致或缺失的数据库连接必须拒绝");
            }
            catch (ElementPropertyDefinitionDatabaseException ex)
            {
                Check(!ex.Message.Contains("same", StringComparison.OrdinalIgnoreCase)
                    && !ex.Message.Contains("first", StringComparison.Ordinal), "连接检查错误不得泄露连接详情");
            }
        }
        var service = new ElementMetadataService();
        var sessions = new AuthUserDepartmentService();
        var controller = new ElementMetadataController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        // 测试密钥仅在内存中随机生成，不使用任何实际凭据。
        string key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string otherKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        await TestTransportGate(key, digest);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        controller.Request.Headers["X-Element-Definition-Key"] = key;
        var denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult noConfig && noConfig.StatusCode == 503 && sessions.Calls == 0 && service.Calls == 0, "未配置摘要时必须默认关闭");
        string noConfigResponse = System.Text.Json.JsonSerializer.Serialize(((ObjectResult)denied.Result!).Value);
        Check(!noConfigResponse.Contains(key) && !noConfigResponse.Contains(digest, StringComparison.OrdinalIgnoreCase), "未配置时响应不得泄露密钥或摘要");
        foreach (string invalidConfig in new[] { "", new string('A', 63), new string('Z', 64), digest + " ", new string('０', 64) })
        {
            configuration["ElementPropertyDefinitions:WriteKeySha256"] = invalidConfig;
            denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
            Check(denied.Result is ObjectResult badConfig && badConfig.StatusCode == 503 && sessions.Calls == 0, "错误摘要配置必须默认关闭");
        }
        byte[] rawKey = Convert.FromBase64String(key.Replace('-', '+').Replace('_', '/') + "=");
        configuration["ElementPropertyDefinitions:WriteKeySha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rawKey));
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult rawDigest && rawDigest.StatusCode == 403 && sessions.Calls == 0, "密钥摘要必须针对 UTF-8 文本而非解码字节");
        configuration["ElementPropertyDefinitions:WriteKeySha256"] = digest.ToLowerInvariant();
        controller.Request.Headers.Remove("X-Element-Definition-Key");
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult missingKey && missingKey.StatusCode == 403 && sessions.Calls == 0, "缺少密钥必须拒绝");
        foreach (string invalidKey in new[] { otherKey, "", key + "=", key + " ", new string('A', 42) + "B", key[..42] + "!", key + "," + key })
        {
            controller.Request.Headers["X-Element-Definition-Key"] = invalidKey;
            denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
            Check(denied.Result is ObjectResult badKey && badKey.StatusCode == 403 && sessions.Calls == 0 && service.Calls == 0, "错误密钥或格式必须拒绝");
        }
        controller.Request.Headers["X-Element-Definition-Key"] = new StringValues(new[] { key, key });
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult duplicateKey && duplicateKey.StatusCode == 403 && sessions.Calls == 0, "重复密钥头必须拒绝");
        string duplicateResponse = System.Text.Json.JsonSerializer.Serialize(((ObjectResult)denied.Result!).Value);
        Check(!duplicateResponse.Contains(key) && !duplicateResponse.Contains(digest, StringComparison.OrdinalIgnoreCase), "密钥拒绝响应不得泄露凭据");
        controller.Request.Headers["X-Element-Definition-Key"] = key;
        controller.Request.Headers["X-Operator-Name"] = "admin";
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult missing && missing.StatusCode == 401 && service.Calls == 0 && sessions.Calls == 0, "伪造管理员头且缺少令牌必须拒绝");
        foreach (var header in new[] { "Basic abc", "Bearer", "Bearer a b", "Bearer a, Bearer b" })
        {
            controller.Request.Headers.Authorization = header;
            denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
            Check(denied.Result is ObjectResult bad && bad.StatusCode == 401 && sessions.Calls == 0, "错误的认证头必须拒绝");
        }
        controller.Request.Headers.Authorization = new StringValues(new[] { "Bearer a", "Bearer b" });
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult multiple && multiple.StatusCode == 401 && sessions.Calls == 0, "多个认证头必须拒绝");
        controller.Request.Headers.Authorization = "Bearer invalid";
        foreach (string reason in new[] { "无效", "过期", "已撤销" })
        {
            sessions.Response = new SessionResponse { Success = false, Message = reason, User = new UserDto { Username = "admin" } };
            denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
            Check(denied.Result is ObjectResult invalid && invalid.StatusCode == 401 && service.Calls == 0 && sessions.LastToken == "invalid", reason + "会话必须拒绝");
        }
        string source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "GB_CadAndSWPlus_V_Server", "Services", "AuthUserDepartmentService.cs")));
        Check(source.Contains("s.TOKEN_HASH={p}TokenHash AND s.REVOKED_AT IS NULL AND s.EXPIRES_AT > {now} AND u.IS_ACTIVE=1"),
            "真实会话验证必须查询令牌哈希并排除撤销、过期及失效账号");
        sessions.Response = new SessionResponse { Success = true };
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult noUser && noUser.StatusCode == 401 && service.Calls == 0, "会话缺少服务器用户必须拒绝");
        sessions.Response = new SessionResponse { Success = true, User = new UserDto { Username = "普通用户", Role = "admin" } };
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult nonAdmin && nonAdmin.StatusCode == 403 && service.Calls == 0, "普通用户不能借管理员头或角色声明提权");
        sessions.Error = new InvalidOperationException("不应返回的敏感信息");
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult unavailable && unavailable.StatusCode == 503 && service.Calls == 0, "认证服务故障必须安全拒绝");
        sessions.Error = null;
        sessions.Response = new SessionResponse { Success = true, User = new UserDto { Username = "SYSDBA" } };
        controller.Request.Headers["X-Element-Definition-Key"] = otherKey;
        denied = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(denied.Result is ObjectResult validSessionBadKey && validSessionBadKey.StatusCode == 403 && service.Calls == 0,
            "有效管理员会话不能绕过密钥");
        controller.Request.Headers["X-Element-Definition-Key"] = key;
        controller.Request.Headers.Remove("X-Operator-Name");
        controller.Request.Headers.Authorization = "Bearer valid";
        var allowed = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(allowed.Result is ObjectResult ok && ok.StatusCode == 201 && service.Calls == 1 && service.LastOperator == "SYSDBA", "有效管理员会话无需用户名头且操作人来自服务端");
        controller.Request.Headers["X-Operator-Name"] = "forged";
        allowed = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
        Check(allowed.Result is ObjectResult forged && forged.StatusCode == 201 && service.LastOperator == "SYSDBA", "伪造用户名头不得影响操作人");
        var legacyRequest = new DefaultHttpContext().Request;
        legacyRequest.Headers["X-Operator-Name"] = " admin ";
        Check(StandardManagementAuthorization.IsAdministrator(legacyRequest, out var oldName) && oldName == "admin", "旧接口管理员头行为不得改变");
        controller.Request.Method = "POST";
        controller.Request.Path = "/api/elements/1/properties/definitions";
        Check(ElementPropertyDefinitionLogProtection.ShouldHideRequest(controller.Request), "新增接口请求内容必须隐藏");
        controller.Request.Method = "PUT";
        Check(!ElementPropertyDefinitionLogProtection.ShouldHideRequest(controller.Request), "旧接口的日志行为不得改变");
        var logContext = new DefaultHttpContext();
        logContext.Request.Method = "POST";
        logContext.Request.Path = "/api/elements/1/properties/definitions";
        logContext.Request.QueryString = new QueryString("?other=credential");
        logContext.Request.Headers.Authorization = "Bearer credential";
        logContext.Request.Headers["X-Element-Definition-Key"] = key;
        logContext.Request.ContentType = "application/json";
        var bytes = System.Text.Encoding.UTF8.GetBytes("[{\"code\":\"credential\",\"token\":\"credential\"}]");
        logContext.Request.Body = new MemoryStream(bytes);
        logContext.Request.ContentLength = bytes.Length;
        var logs = new HourlyFileLogger();
        bool nextCalled = false;
        var middleware = new RequestLoggingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, logs);
        await middleware.InvokeAsync(logContext);
        Check(nextCalled && logs.Lines.All(x => !x.Contains("credential") && !x.Contains(key) && !x.Contains(digest, StringComparison.OrdinalIgnoreCase)) && logs.Lines.Any(x => x.Contains("已隐藏")), "中间件不得记录令牌、密钥或摘要");
        logs.Lines.Clear();
        var actionContext = new ActionContext(logContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(actionContext, filters, new Dictionary<string, object?>(), controller);
        var logFilter = new OperationLogFilter(logs);
        await logFilter.OnActionExecutionAsync(executing, () => Task.FromResult(new ActionExecutedContext(actionContext, filters, controller) { Result = new OkResult() }));
        Check(logs.Lines.All(x => !x.Contains("credential") && !x.Contains(key) && !x.Contains(digest, StringComparison.OrdinalIgnoreCase)) && logs.Lines.Any(x => x.Contains("已隐藏")), "操作日志过滤器不得记录令牌、密钥或摘要");
        logs.Lines.Clear();
        try { await logFilter.OnActionExecutionAsync(executing, () => throw new InvalidOperationException(key + digest)); }
        catch (InvalidOperationException) { }
        Check(logs.Lines.Count > 0 && logs.Lines.All(x => !x.Contains(key) && !x.Contains(digest, StringComparison.OrdinalIgnoreCase)),
            "操作日志异常路径不得记录密钥或摘要");
        logs.Lines.Clear();
        var failingMiddleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException(key + digest), logs);
        try { await failingMiddleware.InvokeAsync(logContext); }
        catch (InvalidOperationException) { }
        Check(logs.Lines.Count > 0 && logs.Lines.All(x => !x.Contains(key) && !x.Contains(digest, StringComparison.OrdinalIgnoreCase)),
            "请求日志异常路径不得记录密钥或摘要");
        await TestGlobalExceptionHandler(key, digest);
        foreach (var test in new (Exception error, int status)[]
        {
            (new ElementPropertyDefinitionValidationException("无效"), 400),
            (new ElementPropertyElementNotFoundException(), 404),
            (new ElementPropertyDefinitionConflictException("冲突"), 409),
            (new ElementPropertyDefinitionDatabaseException(), 500)
        })
        {
            service.Error = test.error;
            var result = await controller.CreateDefinitionsAsync(1, new[] { Item() }, sessions, configuration, default);
            Check(result.Result is ObjectResult response && response.StatusCode == test.status, "错误状态码不正确");
        }

        var db = new FakeDatabase();
        await Run(db, definitions);
        Check(db.Rows.Count == 3 && db.Commits == 1 && db.Rows.Select(x => x.Id).Distinct().Count() == 3 && db.Rows.All(x => x.Id > 0 && x.Id <= 9007199254740991L), "批次必须完整提交且 ID 唯一");
        await Fails<ElementPropertyDefinitionConflictException>(db, Valid(Item("DN", "其他")));
        await Fails<ElementPropertyDefinitionConflictException>(db, Valid(Item("NEW", "通径")));
        Check(db.Rows.Count == 3 && db.Commits == 1, "冲突不得更改记录");
        db.Status = null;
        await Fails<ElementPropertyElementNotFoundException>(db, definitions);
        foreach (var status in new[] { "ARCHIVED", "DISABLED", "INVALID" })
        {
            db.Status = status;
            await Fails<ElementPropertyDefinitionConflictException>(db, definitions);
        }
        db.Status = "DRAFT"; db.FailOnInsert = 2;
        await Fails<InvalidOperationException>(db, Valid(Item("X", "甲"), Item("Y", "乙")));
        Check(db.Rows.Count == 3 && db.Commits == 1 && db.Rollbacks == 7, "第二次插入失败时必须回滚前一条");
        db.FailOnInsert = 0; db.FailCommit = true;
        await Fails<InvalidOperationException>(db, Valid(Item("X", "甲")));
        Check(db.Rows.Count == 3 && db.Commits == 1, "提交失败时不得保留部分记录");
        Console.WriteLine($"属性定义回归通过：{checks} 项检查。");
    }

    static async Task Run(FakeDatabase db, IReadOnlyList<ElementPropertyDto> items)
    {
        await using var tx = new FakeTransaction(db);
        await ElementPropertyDefinitionCreation.ExecuteAsync(1, items, tx, default);
    }

    static async Task TestGlobalExceptionHandler(string key, string digest)
    {
        var capture = new CaptureLoggerProvider();
        using var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(capture)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/elements/42/properties/definitions";
        context.Request.QueryString = new QueryString("?other=" + key);
        context.Request.Headers["X-Element-Definition-Key"] = key;
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException(key + digest)
        });
        await GlobalExceptionHandler.HandleAsync(context);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var response = System.Text.Json.JsonDocument.Parse(body);
        Check(context.Response.StatusCode == 500 && response.RootElement.GetProperty("message").GetString() == "服务器内部错误，请联系管理员。",
            "全局异常必须返回安全的 500");
        Check(capture.Entries.Count == 1 && capture.Entries[0].Message.Contains("已隐藏")
            && !capture.Entries[0].Message.Contains(key) && !capture.Entries[0].Message.Contains(digest, StringComparison.OrdinalIgnoreCase) && capture.Entries[0].Exception == null,
            "新接口异常日志不得泄露查询参数或异常内容");

        capture.Entries.Clear();
        context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/elements/42/properties/definitions";
        context.Request.Headers["X-Element-Definition-Key"] = key;
        context.Response.Body = new MemoryStream();
        var guard = new ElementPropertyDefinitionExceptionMiddleware(
            _ => throw new InvalidOperationException(key + digest),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ElementPropertyDefinitionExceptionMiddleware>.Instance);
        await guard.InvokeAsync(context);
        Check(context.Response.StatusCode == 500 && capture.Entries.Count == 1 && capture.Entries[0].Exception == null
            && !capture.Entries[0].Message.Contains(key) && !capture.Entries[0].Message.Contains(digest, StringComparison.OrdinalIgnoreCase),
            "异常防护中间件不得将原异常交给框架日志");
        context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/elements/42/properties/definitions";
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        Exception? safeError = null;
        try { await guard.InvokeAsync(context); } catch (Exception ex) { safeError = ex; }
        Check(safeError is InvalidOperationException && safeError.InnerException == null
            && !safeError.Message.Contains(key) && !safeError.Message.Contains(digest, StringComparison.OrdinalIgnoreCase),
            "响应已开始时抛出的异常也不能包含密钥和摘要");
        var oldError = new InvalidOperationException("旧接口错误");
        context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/api/other";
        var oldGuard = new ElementPropertyDefinitionExceptionMiddleware(
            _ => throw oldError, Microsoft.Extensions.Logging.Abstractions.NullLogger<ElementPropertyDefinitionExceptionMiddleware>.Instance);
        Exception? observed = null;
        try { await oldGuard.InvokeAsync(context); } catch (Exception ex) { observed = ex; }
        Check(ReferenceEquals(observed, oldError), "异常防护不能改变其他接口的异常行为");

        capture.Entries.Clear();
        context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/api/other";
        context.Request.QueryString = new QueryString("?foo=ordinary-value");
        context.Response.Body = new MemoryStream();
        var original = new InvalidOperationException("旧接口异常");
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = original });
        await GlobalExceptionHandler.HandleAsync(context);
        Check(capture.Entries.Count == 1 && capture.Entries[0].Message.Contains("ordinary-value")
            && ReferenceEquals(capture.Entries[0].Exception, original),
            "其他接口原有异常日志行为必须不变");

        string program = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "GB_CadAndSWPlus_V_Server", "Program.cs")));
        Check(program.Contains("exceptionApp.Run(GlobalExceptionHandler.HandleAsync)"), "全局异常管道必须使用被测处理器");
        Check(program.Contains("app.UseMiddleware<ElementPropertyDefinitionExceptionMiddleware>()"), "全局异常之后必须安装专用防护");
    }

    static async Task TestTransportGate(string key, string digest)
    {
        var capture = new CaptureLoggerProvider();
        using var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(capture)).BuildServiceProvider();
        var logger = services.GetRequiredService<ILogger<ElementPropertyDefinitionTransportMiddleware>>();
        var snapshotLogger = services.GetRequiredService<ILogger<ElementPropertyDefinitionTransportSnapshotMiddleware>>();

        async Task<(int Status, bool Invoked, bool FinalHttps)> CheckConnection(string? address, bool originalHttps,
            string? forwardingHeader = null, string path = "/api/elements/42/properties/definitions", string forwardedValue = "https",
            bool hasIis = true, bool hasVariables = true, string? serverValue = null, bool throwRead = false,
            bool throwServer = false, bool noValue = false, bool addTls = false)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = "POST";
            context.Request.Path = path;
            context.Request.Scheme = originalHttps ? "https" : "http";
            context.Request.Headers["X-Element-Definition-Key"] = key;
            context.Request.QueryString = new QueryString("?test=" + digest);
            context.Connection.RemoteIpAddress = address == null ? null : System.Net.IPAddress.Parse(address);
            context.Response.Body = new MemoryStream();
            if (forwardingHeader != null) context.Request.Headers[forwardingHeader] = forwardedValue;
            context.Request.Headers["HTTPS"] = "ON";
            var server = new FakeServer { ThrowOnFeatures = throwServer };
            if (hasIis) server.ServerFeatures.Set<IIISEnvironmentFeature>(new FakeIisEnvironment());
            if (hasVariables)
                context.Features.Set<IServerVariablesFeature>(new FakeServerVariables(
                    noValue ? null : serverValue ?? (originalHttps ? "ON" : "OFF"), throwRead, key + digest));
            if (addTls) context.Features.Set<ITlsHandshakeFeature>(new FakeTlsHandshakeFeature());
            bool invoked = false;
            var gate = new ElementPropertyDefinitionTransportMiddleware(_ => { invoked = true; return Task.CompletedTask; }, logger);
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedProto,
                ForwardedProtoHeaderName = forwardingHeader ?? "X-Forwarded-Proto"
            };
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            var forwarder = new ForwardedHeadersMiddleware(gate.InvokeAsync,
                services.GetRequiredService<ILoggerFactory>(), Options.Create(options));
            var captureSnapshot = new ElementPropertyDefinitionTransportSnapshotMiddleware(forwarder.Invoke, server, snapshotLogger);
            await captureSnapshot.InvokeAsync(context);
            return (context.Response.StatusCode, invoked, context.Request.IsHttps);
        }

        var result = await CheckConnection("203.0.113.5", false);
        Check(result.Status == 403 && !result.Invoked, "远程明文必须在访问业务前拒绝");
        result = await CheckConnection("203.0.113.5", false, "X-Forwarded-Proto");
        Check(result.Status == 403 && !result.Invoked && result.FinalHttps, "伪造的标准转发协议不能绕过 IIS 变量检查");
        result = await CheckConnection("203.0.113.5", false, "X-Custom-Scheme");
        Check(result.Status == 403 && !result.Invoked && result.FinalHttps, "自定义转发协议头不能绕过 IIS 变量检查");
        result = await CheckConnection(null, false);
        Check(result.Status == 403 && !result.Invoked, "无法确认对端的明文连接必须拒绝");
        foreach (string address in new[] { "127.0.0.1", "::1", "::ffff:127.0.0.1" })
        {
            result = await CheckConnection(address, false);
            Check(result.Status == 403 && !result.Invoked, "明文环回连接也必须拒绝");
        }
        result = await CheckConnection("127.0.0.1", false, "X-Forwarded-Proto");
        Check(result.Status == 403 && !result.Invoked, "带转发信息的环回代理必须拒绝");
        result = await CheckConnection("203.0.113.5", true);
        Check(result.Invoked, "模拟 IIS 进程内且 HTTPS=ON 必须允许");
        result = await CheckConnection("203.0.113.5", true, hasIis: false);
        Check(result.Status == 403 && !result.Invoked, "无 IIS 进程内服务器特征不得允许");
        result = await CheckConnection("203.0.113.5", true, hasVariables: false);
        Check(result.Status == 403 && !result.Invoked, "无服务器变量特征不得允许");
        result = await CheckConnection("203.0.113.5", true, noValue: true);
        Check(result.Status == 403 && !result.Invoked, "缺失 HTTPS 变量不得允许");
        result = await CheckConnection("203.0.113.5", true, serverValue: "OFF", addTls: true);
        Check(result.Status == 403 && !result.Invoked, "HTTPS=OFF 不得回退至请求协议或 TLS 特征");
        foreach (string value in new[] { "", "OFF", "UNKNOWN", "true", " ON " })
        {
            result = await CheckConnection("203.0.113.5", true, serverValue: value);
            Check(result.Status == 403 && !result.Invoked, "服务器变量非 ON 不得允许");
        }
        result = await CheckConnection("203.0.113.5", true, throwRead: true);
        Check(result.Status == 403 && !result.Invoked, "服务器变量读取失败必须拒绝");
        result = await CheckConnection("203.0.113.5", true, throwServer: true);
        Check(result.Status == 403 && !result.Invoked, "IIS 特征读取失败必须拒绝");
        result = await CheckConnection("203.0.113.5", false, serverValue: "on");
        Check(result.Invoked && !result.FinalHttps, "仅 IIS 服务器变量决定 HTTPS，不回退至请求协议");
        result = await CheckConnection("203.0.113.5", true, "X-Custom-Scheme", forwardedValue: "http");
        Check(result.Invoked && !result.FinalHttps, "后续协议改写不得覆盖 IIS HTTPS 变量快照");
        result = await CheckConnection("127.0.0.1", true);
        Check(result.Invoked, "模拟本机原始 HTTPS 必须允许");
        result = await CheckConnection("203.0.113.5", true, "X-Forwarded-Proto");
        Check(result.Status == 403 && !result.Invoked, "未知代理声明不得绕过限制");
        result = await CheckConnection("203.0.113.5", false, path: "/api/other");
        Check(result.Invoked, "旧接口传输行为不得改变");
        var noSnapshot = new DefaultHttpContext { RequestServices = services };
        noSnapshot.Request.Method = "POST";
        noSnapshot.Request.Path = "/api/elements/42/properties/definitions";
        noSnapshot.Request.Scheme = "https";
        noSnapshot.Request.Headers["X-Original-Transport"] = "https";
        noSnapshot.Response.Body = new MemoryStream();
        bool invokedWithoutSnapshot = false;
        var directGate = new ElementPropertyDefinitionTransportMiddleware(_ => { invokedWithoutSnapshot = true; return Task.CompletedTask; }, logger);
        await directGate.InvokeAsync(noSnapshot);
        Check(noSnapshot.Response.StatusCode == 403 && !invokedWithoutSnapshot, "缺失原始协议快照必须默认拒绝");
        Check(capture.Entries.All(x => !x.Message.Contains(key) && !x.Message.Contains(digest, StringComparison.OrdinalIgnoreCase) && x.Exception == null),
            "连接拒绝日志不得记录凭据");

        string program = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "GB_CadAndSWPlus_V_Server", "Program.cs")));
        int gateIndex = program.IndexOf("app.UseMiddleware<ElementPropertyDefinitionTransportMiddleware>()", StringComparison.Ordinal);
        int snapshotIndex = program.IndexOf("app.UseMiddleware<ElementPropertyDefinitionTransportSnapshotMiddleware>()", StringComparison.Ordinal);
        int forwardIndex = program.IndexOf("app.UseForwardedHeaders()", StringComparison.Ordinal);
        Check(snapshotIndex >= 0 && forwardIndex > snapshotIndex && gateIndex > forwardIndex, "IIS 变量捕获必须先于转发头，验证必须使用捕获值");
    }

    sealed class FakeIisEnvironment : IIISEnvironmentFeature
    {
        public Version IISVersion => new(10, 0);
        public string AppPoolId => string.Empty;
        public string AppPoolConfigFile => string.Empty;
        public string AppConfigPath => string.Empty;
        public string ApplicationPhysicalPath => string.Empty;
        public string ApplicationVirtualPath => string.Empty;
        public string ApplicationId => string.Empty;
        public string SiteName => string.Empty;
        public uint SiteId => 1;
    }

    sealed class FakeTlsHandshakeFeature : ITlsHandshakeFeature
    {
        public System.Security.Authentication.SslProtocols Protocol => System.Security.Authentication.SslProtocols.Tls12;
        public System.Net.Security.TlsCipherSuite NegotiatedCipherSuite => default;
        public string HostName => string.Empty;
        public System.Security.Authentication.CipherAlgorithmType CipherAlgorithm => default;
        public int CipherStrength => 0;
        public System.Security.Authentication.HashAlgorithmType HashAlgorithm => default;
        public int HashStrength => 0;
        public System.Security.Authentication.ExchangeAlgorithmType KeyExchangeAlgorithm => default;
        public int KeyExchangeStrength => 0;
    }

    sealed class FakeServer : IServer
    {
        public IFeatureCollection ServerFeatures { get; } = new FeatureCollection();
        public bool ThrowOnFeatures;
        public IFeatureCollection Features => ThrowOnFeatures ? throw new InvalidOperationException("模拟服务器特征错误") : ServerFeatures;
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Dispose() { }
    }

    sealed class FakeServerVariables : IServerVariablesFeature
    {
        private readonly string? value;
        private readonly bool throwRead;
        private readonly string exceptionText;
        public FakeServerVariables(string? value, bool throwRead, string exceptionText) { this.value = value; this.throwRead = throwRead; this.exceptionText = exceptionText; }
        public string? this[string name]
        {
            get => throwRead ? throw new InvalidOperationException(exceptionText) : name == "HTTPS" ? value : null;
            set => throw new NotSupportedException();
        }
    }

    sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public readonly List<(string Message, Exception? Exception)> Entries = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
        public void Dispose() { }
    }
    sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
    sealed class CaptureLogger : ILogger
    {
        private readonly List<(string Message, Exception? Exception)> entries;
        public CaptureLogger(List<(string Message, Exception? Exception)> entries) => this.entries = entries;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => entries.Add((formatter(state, exception), exception));
    }
    static async Task Fails<T>(FakeDatabase db, IReadOnlyList<ElementPropertyDto> items) where T : Exception
    {
        try { await Run(db, items); } catch (T) { checks++; return; }
        throw new Exception("应抛出 " + typeof(T).Name);
    }

    sealed class FakeDatabase
    {
        public string? Status = "DRAFT";
        public List<ElementPropertyDto> Rows = new();
        public int Commits;
        public int Rollbacks;
        public int FailOnInsert;
        public bool FailCommit;
    }
    sealed class FakeTransaction : IElementPropertyDefinitionTransaction
    {
        readonly FakeDatabase db;
        readonly List<ElementPropertyDto> pending = new();
        public FakeTransaction(FakeDatabase db) => this.db = db;
        public Task<string?> LockElementAsync(long id, CancellationToken token) => Task.FromResult(db.Status);
        public Task<IReadOnlyList<ElementPropertyDto>> ListDefinitionsAsync(long id, CancellationToken token) => Task.FromResult<IReadOnlyList<ElementPropertyDto>>(db.Rows);
        public Task InsertAsync(ElementPropertyDto value, CancellationToken token)
        {
            if (db.FailOnInsert == pending.Count + 1) throw new InvalidOperationException("模拟插入失败");
            pending.Add(value); return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken token)
        {
            if (db.FailCommit) throw new InvalidOperationException("模拟提交失败");
            db.Rows.AddRange(pending); db.Commits++; return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken token) { db.Rollbacks++; pending.Clear(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { pending.Clear(); return ValueTask.CompletedTask; }
    }
}

namespace GB_CadAndSWPlus_V_Server.Services
{
    public enum LogLevel { Info, Warning, Error }
    public sealed class HourlyFileLogger
    {
        public readonly List<string> Lines = new();
        public void WriteLineForPlatform(string platform, LogLevel level, string line) => Lines.Add(line);
    }
}
namespace GB_CadAndSWPlus_V.UploadApi.Services
{
    public sealed class ElementMetadataService
    {
        public int Calls;
        public Exception? Error;
        public string? LastOperator;
        public Task<ElementMetadataResponse<IReadOnlyList<ElementPropertyDto>>> CreateDefinitionsAsync(long id, IReadOnlyList<CreateElementPropertyDefinitionRequest?>? values, string name, CancellationToken token)
        {
            Calls++;
            LastOperator = name;
            if (Error != null) throw Error;
            return Task.FromResult(new ElementMetadataResponse<IReadOnlyList<ElementPropertyDto>> { Success = true });
        }
        public Task<ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>>> ListAsync(long id, CancellationToken token) => throw new NotImplementedException();
        public Task<ElementMetadataResponse<object>> SaveAsync(long id, IReadOnlyList<ElementPropertyValueRequest> values, string name, CancellationToken token) => throw new NotImplementedException();
    }

    public sealed class AuthUserDepartmentService
    {
        public int Calls;
        public string? LastToken;
        public SessionResponse Response = new();
        public Exception? Error;
        public Task<SessionResponse> ValidateSessionAsync(string token, CancellationToken cancellationToken, bool hideDatabaseExceptionDetails = false)
        {
            if (!hideDatabaseExceptionDetails) throw new Exception("属性定义接口会话验证必须隐藏数据库异常详情");
            Calls++;
            LastToken = token;
            if (Error != null) throw Error;
            return Task.FromResult(Response);
        }
    }
}
