using GB_CadAndSWPlus_V.Shared.Models;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace GB_CadAndSWPlus_V.Shared.Services
{
    /// <summary>
    /// 用于在本地存储和加载共享登录会话信息的类。
    /// 会话文件默认保存在 %LocalAppData%\GB_CADPLUS\Auth\shared-session.json。
    /// </summary>
    public sealed class SharedLoginSessionStore
    {
        /// <summary>
        /// 用于同步访问共享登录会话文件的锁对象，避免多线程同时读写导致文件损坏。
        /// </summary>
        private static readonly object SyncRoot = new object();

        /// <summary>
        /// 获取共享登录会话文件的完整路径。
        /// 路径为：%LocalAppData%\GB_CADPLUS\Auth\shared-session.json
        /// </summary>
        public static string SessionPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GB_CADPLUS", "Auth", "shared-session.json");

        /// <summary>
        /// 从本地文件加载共享登录会话。
        /// 如果文件不存在或读取失败，则返回一个新的空会话对象。
        /// </summary>
        /// <returns>共享登录会话对象。</returns>
        public SharedLoginSession Load()
        {
            lock (SyncRoot)
            {
                // 统一登录文件不存在时，兼容读取 CAD 旧登录页保存的配置文件。
                if (!File.Exists(SessionPath))
                    return LoadLegacyCadSession();
                try
                {
                    // 使用 DataContractJsonSerializer 反序列化 JSON 会话文件
                    var serializer = new DataContractJsonSerializer(typeof(SharedLoginSession));
                    using (var stream = File.OpenRead(SessionPath))
                        return serializer.ReadObject(stream) as SharedLoginSession ?? new SharedLoginSession();
                }
                catch (Exception)
                {
                    // 统一文件损坏时尝试读取旧配置，避免已有登录配置丢失。
                    return LoadLegacyCadSession();
                }
            }
        }

        private static SharedLoginSession LoadLegacyCadSession()
        {
            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GB_CADPLUS",
                "login_config.json");
            if (!File.Exists(legacyPath)) return new SharedLoginSession();

            try
            {
                var legacySerializer = new DataContractJsonSerializer(typeof(LegacyCadLoginConfig));
                LegacyCadLoginConfig legacy;
                using (var stream = File.OpenRead(legacyPath))
                    legacy = legacySerializer.ReadObject(stream) as LegacyCadLoginConfig;
                if (legacy == null || string.IsNullOrWhiteSpace(legacy.ServerIP)) return new SharedLoginSession();

                var session = new SharedLoginSession
                {
                    ServerHost = legacy.ServerIP.Trim(),
                    ApiPort = ParsePort(legacy.ApiPort, 10010),
                    DatabaseType = string.IsNullOrWhiteSpace(legacy.DatabaseType) ? "DM" : legacy.DatabaseType,
                    DatabasePort = ParsePort(legacy.DataBaseserverPort, 5236),
                    Username = legacy.Username?.Trim() ?? string.Empty,
                    EncryptedPassword = legacy.SavePassword ? legacy.EncryptedPassword ?? string.Empty : string.Empty,
                    UpdatedUtc = DateTime.UtcNow
                };
                return session;
            }
            catch
            {
                return new SharedLoginSession();
            }
        }

        private static int ParsePort(string value, int fallback)
            => int.TryParse(value, out int port) && port > 0 && port <= 65535 ? port : fallback;

        [DataContract]
        private sealed class LegacyCadLoginConfig
        {
            [DataMember(Name = "ServerIP")] public string ServerIP { get; set; }
            [DataMember(Name = "DataBaseserverPort")] public string DataBaseserverPort { get; set; }
            [DataMember(Name = "ApiPort")] public string ApiPort { get; set; }
            [DataMember(Name = "Username")] public string Username { get; set; }
            [DataMember(Name = "SavePassword")] public bool SavePassword { get; set; }
            [DataMember(Name = "EncryptedPassword")] public string EncryptedPassword { get; set; }
            [DataMember(Name = "DatabaseType")] public string DatabaseType { get; set; }
        }

        /// <summary>
        /// 保存服务器 IP、端口、用户名、密码等信息到本地会话文件。
        /// 密码是否加密保存由 <paramref name="rememberPassword"/> 决定。
        /// </summary>
        /// <param name="request">统一登录请求，包含服务器、端口、用户名等信息。</param>
        /// <param name="result">统一登录结果，包含显示名、令牌、部门等信息。</param>
        /// <param name="rememberPassword">是否记住密码；为 true 时加密保存密码。</param>
        /// <exception cref="ArgumentNullException">当 request 或 result 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">当服务器地址、端口或用户名为空/无效时抛出。</exception>
        /// <exception cref="IOException">当会话文件写入失败时抛出。</exception>
        public void Save(UnifiedLoginRequest request, UnifiedLoginResult result, bool rememberPassword)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(request.ServerHost)) throw new InvalidOperationException("服务器地址不能为空。");
            if (request.ApiPort < 1 || request.ApiPort > 65535) throw new InvalidOperationException("API 端口无效。");
            if (string.IsNullOrWhiteSpace(request.Username)) throw new InvalidOperationException("用户名不能为空。");

            // 根据请求和结果构造要保存的会话对象
            var session = new SharedLoginSession
            {
                ServerHost = request.ServerHost?.Trim() ?? string.Empty,
                ApiPort = request.ApiPort,
                DatabaseType = request.DatabaseType ?? "DM",
                DatabasePort = request.DatabasePort ?? 5236,
                Username = request.Username?.Trim() ?? string.Empty,
                DisplayName = result.DisplayName ?? string.Empty,
                UserId = result.UserId,
                DepartmentId = result.DepartmentId,
                DepartmentName = result.DepartmentName ?? string.Empty,
                AccessToken = result.AccessToken ?? string.Empty,
                AccessTokenExpiresAtUtc = result.AccessTokenExpiresAtUtc,
                UpdatedUtc = DateTime.UtcNow,
                // 如果选择记住密码，则对密码进行加密保护后保存；否则保存空字符串
                EncryptedPassword = rememberPassword ? Protect(request.Password ?? string.Empty) : string.Empty
            };

            lock (SyncRoot)
            {
                // 确保目标目录存在
                string directory = Path.GetDirectoryName(SessionPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                // 先写入临时文件，再替换正式文件，尽量避免写入过程中断导致文件损坏
                string temporaryPath = SessionPath + ".tmp";
                var serializer = new DataContractJsonSerializer(typeof(SharedLoginSession));
                using (var stream = File.Create(temporaryPath))
                    serializer.WriteObject(stream, session);

                // 如果正式文件已存在，则进行替换；否则直接移动临时文件
                if (File.Exists(SessionPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, SessionPath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // 某些平台不支持 File.Replace，则退化为先删除再移动
                        File.Delete(SessionPath);
                        File.Move(temporaryPath, SessionPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, SessionPath);
                }

                // 最终检查文件是否确实写入成功
                if (!File.Exists(SessionPath)) throw new IOException("共享登录会话文件写入失败。");

                // 同步写入旧 CAD 登录页使用的配置，确保两套入口打开时显示相同信息。
                SaveLegacyCadSession(request, rememberPassword, session.EncryptedPassword);
            }
        }

        private static void SaveLegacyCadSession(UnifiedLoginRequest request, bool rememberPassword, string encryptedPassword)
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GB_CADPLUS");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "login_config.json");
            var legacy = new LegacyCadLoginConfig
            {
                ServerIP = request.ServerHost?.Trim() ?? string.Empty,
                DataBaseserverPort = (request.DatabasePort ?? 5236).ToString(),
                ApiPort = request.ApiPort.ToString(),
                Username = request.Username?.Trim() ?? string.Empty,
                SavePassword = rememberPassword,
                EncryptedPassword = rememberPassword ? encryptedPassword : null,
                DatabaseType = request.DatabaseType ?? "DM"
            };
            var serializer = new DataContractJsonSerializer(typeof(LegacyCadLoginConfig));
            using (var stream = File.Create(path))
                serializer.WriteObject(stream, legacy);
        }

        /// <summary>
        /// 将本地保存的共享登录会话转换为统一登录请求。
        /// 密码会从加密存储中解密后填入请求。
        /// </summary>
        /// <param name="session">本地保存的共享登录会话。</param>
        /// <param name="platform">目标登录平台。</param>
        /// <returns>可用于发起登录的统一登录请求。</returns>
        /// <exception cref="ArgumentNullException">当 session 为 null 时抛出。</exception>
        public UnifiedLoginRequest ToLoginRequest(SharedLoginSession session, UnifiedLoginPlatform platform)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return new UnifiedLoginRequest
            {
                Platform = platform,
                ServerHost = session.ServerHost,
                ApiPort = session.ApiPort,
                Username = session.Username,
                Password = GetPassword(session),
                DatabaseType = session.DatabaseType,
                // 数据库端口若无效则回退到默认的 5236
                DatabasePort = session.DatabasePort > 0 ? session.DatabasePort : 5236,
                DepartmentId = session.DepartmentId
            };
        }

        /// <summary>
        /// 从共享登录会话中获取解密后的密码。
        /// 如果会话为空、未保存密码或解密失败，则返回空字符串。
        /// </summary>
        /// <param name="session">共享登录会话。</param>
        /// <returns>解密后的密码；失败时返回空字符串。</returns>
        public string GetPassword(SharedLoginSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.EncryptedPassword)) return string.Empty;
            try
            {
                return Unprotect(session.EncryptedPassword);
            }
            catch
            {
                // 解密失败时返回空字符串，避免暴露异常
                return string.Empty;
            }
        }

        /// <summary>
        /// 清除本地保存的共享登录会话文件。
        /// </summary>
        public void Clear()
        {
            lock (SyncRoot)
            {
                if (File.Exists(SessionPath)) File.Delete(SessionPath);
            }
        }

        /// <summary>
        /// 退出登录但保留服务器地址、端口、数据库类型和用户名。
        /// </summary>
        public void ClearAuthentication()
        {
            lock (SyncRoot)
            {
                SharedLoginSession session = Load();

                session.EncryptedPassword = string.Empty;
                session.DisplayName = string.Empty;
                session.UserId = null;
                session.DepartmentId = null;
                session.DepartmentName = string.Empty;
                session.AccessToken = string.Empty;
                session.AccessTokenExpiresAtUtc = null;
                session.UpdatedUtc = DateTime.UtcNow;
                WriteSession(session);
            }
        }

        private static void WriteSession(SharedLoginSession session)
        {
            string directory = Path.GetDirectoryName(SessionPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = SessionPath + ".tmp";
            var serializer = new DataContractJsonSerializer(typeof(SharedLoginSession));
            using (var stream = File.Create(temporaryPath)) serializer.WriteObject(stream, session);
            if (File.Exists(SessionPath))
            {
                try { File.Replace(temporaryPath, SessionPath, null); }
                catch (PlatformNotSupportedException) { File.Delete(SessionPath); File.Move(temporaryPath, SessionPath); }
            }
            else File.Move(temporaryPath, SessionPath);
        }

        /// <summary>
        /// 使用当前用户的 Windows 数据保护接口加密字符串。
        /// </summary>
        /// <param name="value">待加密的明文。</param>
        /// <returns>Base64 编码的加密字符串。</returns>
        private static string Protect(string value)
            => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? string.Empty), null, DataProtectionScope.CurrentUser));

        /// <summary>
        /// 使用当前用户的 Windows 数据保护接口解密字符串。
        /// </summary>
        /// <param name="value">Base64 编码的加密字符串。</param>
        /// <returns>解密后的明文。</returns>
        public static string Unprotect(string value)
            => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }
}