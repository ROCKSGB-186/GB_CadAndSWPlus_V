using GB_CadAndSWPlus_V.Shared.Models; // 引入统一登录请求、结果和会话模型。
using GB_CadAndSWPlus_V.Shared.Services; // 引入统一登录服务接口和共享会话存储。
using System; // 引入基础异常和日期类型。
using System.Collections.Generic; // 引入只读列表类型。
using System.Globalization; // 引入兼容服务端 ISO 8601 日期的解析类型。
using System.Net.Http; // 引入 HTTP 登录请求类型。
using System.Runtime.Serialization; // 引入 JSON 数据契约特性。
using System.Runtime.Serialization.Json; // 引入 .NET Framework 内置 JSON 序列化器。
using System.Text; // 引入 UTF-8 编码类型。
using System.Threading; // 引入取消令牌类型。
using System.Threading.Tasks; // 引入异步任务类型。

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services // 使用 SolidWorks 插件专用服务命名空间。
{
    /// <summary>为 SolidWorks 统一登录窗口提供服务端登录能力。</summary>
    public sealed class UnifiedSolidWorksLoginService : IUnifiedLoginService // 实现共享登录窗口需要的统一服务接口。
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; // 复用 HTTP 客户端并设置登录超时时间。

        /// <summary>使用登录窗口输入的账号信息调用服务端登录接口。</summary>
        public async Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken)) // 实现账号密码登录。
        {
            if (request == null) throw new ArgumentNullException(nameof(request)); // 防止调用方传入空登录请求。
            DateTime startedAt = DateTime.Now; // 记录本次登录开始时间，便于追踪每一步耗时。
            string url = BuildUrl(request.ServerHost, request.ApiPort, "api/auth/login"); // 根据登录页面配置构造登录地址。
            SolidWorksFileLogger.Info(string.Format("登录开始；服务器={0}；端口={1}；用户名={2}；接口={3}", request.ServerHost, request.ApiPort, request.Username, url)); // 记录登录开始，但不记录密码。
            var loginRequest = new LoginRequest // 使用强类型请求模型，确保密码中的特殊字符与 CAD 端一致地进行 JSON 转义。
            {
                Username = request.Username?.Trim() ?? string.Empty, // 账号只清理首尾空格，与服务器查询规则保持一致。
                Password = request.Password ?? string.Empty, // 密码不执行 Trim，避免改变用户实际密码。
                ClientPlatform = "SolidWorks" // 标记来源平台，供服务端日志分流，不参与密码校验。
            };
            string json = Serialize(loginRequest); // 使用 .NET 内置序列化器生成合法 JSON，避免手工拼接遗漏控制字符。
            SolidWorksFileLogger.Info("登录请求体已生成；密码字段已脱敏，未写入日志。"); // 记录请求体生成完成，明确敏感字段未写入日志。
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)) // 创建可携带平台标识的登录请求。
            using (var content = new StringContent(json, Encoding.UTF8, "application/json")) // 设置 JSON 请求内容和 UTF-8 编码。
            {
                httpRequest.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS"); // 标记 SolidWorks 来源，供服务端日志分流。
                httpRequest.Content = content;
                using (HttpResponseMessage response = await Client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false)) // 异步调用服务端登录接口。
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); // 读取服务端返回内容。
                SolidWorksFileLogger.Info(string.Format("登录响应已收到；HTTP状态={0}；耗时毫秒={1}", (int)response.StatusCode, (DateTime.Now - startedAt).TotalMilliseconds.ToString("0"))); // 记录响应状态和耗时，不记录响应正文。
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("服务器登录失败：HTTP " + (int)response.StatusCode + "。" + ExtractMessage(body)); // 将 HTTP 失败转换为中文提示并保留服务器原因。
                LoginPayload payload = Deserialize<LoginPayload>(body); // 反序列化服务端登录结果。
                if (payload == null || !payload.Success) throw new InvalidOperationException(string.IsNullOrWhiteSpace(payload?.Message) ? "用户名或密码错误。" : payload.Message); // 处理业务登录失败。
                SolidWorksFileLogger.Info("服务器认证成功，开始解析用户和会话信息。"); // 记录认证成功步骤。
                return new UnifiedLoginResult // 将服务端结果转换为共享登录结果。
                {
                    Success = true, // 标记登录成功。
                    Message = payload.Message ?? "登录成功。", // 设置登录结果消息。
                    DisplayName = payload.User?.DisplayName ?? request.Username, // 设置用户显示名称。
                    UserId = payload.User?.Id, // 保存用户编号。
                    DepartmentId = payload.User?.DepartmentId, // 保存部门编号。
                    DepartmentName = payload.User?.DepartmentName ?? string.Empty, // 保存部门名称。
                    AccessToken = payload.AccessToken ?? string.Empty, // 保存访问令牌供共享会话使用。
                    AccessTokenExpiresAtUtc = ParseServerDate(payload.AccessTokenExpiresAtUtc), // 兼容 .NET 8 返回的 ISO 8601 日期。
                    PlatformSession = request.Username // 保存 SolidWorks 平台会话标识。
                }; // 返回统一登录结果。
            }
            }
        }

        /// <summary>使用共享会话重新登录 SolidWorks。</summary>
        public Task<UnifiedLoginResult> LoginWithSharedSessionAsync(SharedLoginSession session, CancellationToken cancellationToken = default(CancellationToken)) // 实现共享会话登录接口。
        {
            UnifiedLoginRequest request = new SharedLoginSessionStore().ToLoginRequest(session, UnifiedLoginPlatform.SolidWorks); // 将共享会话转换为 SolidWorks 登录请求。
            return LoginAsync(request, cancellationToken); // 复用账号登录流程。
        }

        private static DateTime? ParseServerDate(string value) // 兼容 .NET 8 ISO 8601 与旧 .NET /Date(...)\/ 日期格式。
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                return parsed;

            string text = value.Trim();
            if (text.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase) && text.Contains(")"))
            {
                string millisecondsText = text.Substring(6, text.IndexOf(')') - 6);
                long milliseconds;
                if (long.TryParse(millisecondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
            }

            SolidWorksFileLogger.Warning("服务器返回的会话过期时间无法解析，已按无过期时间处理。"); // 日期异常不应阻断已经成功的登录。
            return null;
        }

        /// <summary>SolidWorks 当前不需要部门选择，因此返回空部门列表。</summary>
        public Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken)) // 实现部门加载接口。
        {
            return Task.FromResult<IReadOnlyList<UnifiedDepartmentOption>>(new UnifiedDepartmentOption[0]); // 返回空列表并保持共享窗口兼容。
        }

        private static string BuildUrl(string host, int port, string path) // 构造服务端 API 地址。
        {
            string normalizedHost = host?.Trim() ?? string.Empty; // 清理主机地址首尾空格。
            if (!normalizedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !normalizedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) normalizedHost = "http://" + normalizedHost; // 没有协议时使用 HTTP 默认协议。
            if (!Uri.TryCreate(normalizedHost, UriKind.Absolute, out Uri baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)) throw new InvalidOperationException("服务器地址无效，请填写 IP 或主机名。"); // 先验证输入，避免生成错误请求地址。
            var builder = new UriBuilder(baseUri) { Port = port }; // 明确使用登录页面输入的 API 端口，避免重复拼接端口。
            return new Uri(builder.Uri, path.TrimStart('/')).ToString(); // 使用 Uri 合并接口路径，兼容带协议和端口的服务器地址。
        }

        private static string Serialize(LoginRequest request) // 序列化登录请求，避免手工拼接 JSON 导致特殊密码字符失真。
        {
            using (var stream = new System.IO.MemoryStream()) // 使用内存流生成 UTF-8 JSON 内容。
            {
                new DataContractJsonSerializer(typeof(LoginRequest)).WriteObject(stream, request); // 由序列化器处理引号、反斜杠和控制字符。
                return Encoding.UTF8.GetString(stream.ToArray()); // 返回请求体字符串。
            }
        }

        private static string ExtractMessage(string json) // 从服务器错误响应中提取中文原因，不暴露密码等敏感信息。
        {
            try
            {
                LoginPayload payload = Deserialize<LoginPayload>(json); // 尝试读取服务器统一错误结构。
                return string.IsNullOrWhiteSpace(payload?.Message) ? string.Empty : payload.Message;
            }
            catch
            {
                return string.Empty; // 错误响应格式异常时保留原 HTTP 错误提示。
            }
        }

        private static T Deserialize<T>(string json) where T : class // 使用 .NET Framework 内置序列化器读取服务端 JSON。
        {
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty))) // 将 JSON 字符串转换为内存流。
            {
                return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T; // 反序列化并返回指定类型对象。
            }
        }

        [DataContract] // 标记服务端登录结果为 JSON 数据契约。
        private sealed class LoginRequest // 定义与服务器 LoginRequest 对应的请求结构。
        {
            [DataMember(Name = "username")] public string Username { get; set; } // 保存登录账号。
            [DataMember(Name = "password")] public string Password { get; set; } // 保存登录密码，仅用于本次请求。
            [DataMember(Name = "clientPlatform")] public string ClientPlatform { get; set; } // 保存客户端平台标识，供服务端日志分流。
        }

        [DataContract] // 标记服务端登录结果为 JSON 数据契约。
        private sealed class LoginPayload // 定义服务端登录结果结构。
        {
            [DataMember(Name = "success")] public bool Success { get; set; } // 保存登录成功标志。
            [DataMember(Name = "message")] public string Message { get; set; } // 保存服务端提示消息。
            [DataMember(Name = "accessToken")] public string AccessToken { get; set; } // 保存访问令牌。
            [DataMember(Name = "accessTokenExpiresAtUtc")] public string AccessTokenExpiresAtUtc { get; set; } // 按字符串接收 ISO 8601 日期，避免 .NET Framework 日期格式冲突。
            [DataMember(Name = "user")] public UserPayload User { get; set; } // 保存用户信息。
        }

        [DataContract] // 标记用户信息为 JSON 数据契约。
        private sealed class UserPayload // 定义服务端用户信息结构。
        {
            [DataMember(Name = "id")] public int? Id { get; set; } // 保存用户编号。
            [DataMember(Name = "displayName")] public string DisplayName { get; set; } // 保存用户显示名称。
            [DataMember(Name = "departmentId")] public int? DepartmentId { get; set; } // 保存部门编号。
            [DataMember(Name = "departmentName")] public string DepartmentName { get; set; } // 保存部门名称。
        }
    }
}
