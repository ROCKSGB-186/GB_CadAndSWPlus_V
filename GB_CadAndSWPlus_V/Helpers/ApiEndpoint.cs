using GB_CadAndSWPlus_V.UniFiedStandards;
using System;
using System.Configuration;

namespace GB_CadAndSWPlus_V.Helpers
{
    internal static class ApiEndpoint
    {
        /// <summary>
        /// 数据库相关 HTTP API 的统一服务端口。
        /// 客户端不直接连接数据库，所有图元、用户、部门、分类及规范数据操作均通过此端口访问服务器。
        /// </summary>
        public const int DefaultApiPort = 10010;

        /// <summary>
        /// 当前客户端使用的 API 端口。端口由登录窗口设置，未配置时回退到默认端口。
        /// </summary>
        public static int ApiPort
        {
            get
            {
                return IsValidPort(VariableDictionary._apiPort)
                    ? VariableDictionary._apiPort
                    : DefaultApiPort;
            }
        }

        public static TimeSpan ConnectTimeout =>
            TimeSpan.FromSeconds(GetPositiveSetting("ApiConnectTimeoutSeconds", 10));

        public static TimeSpan RequestTimeout =>
            TimeSpan.FromSeconds(GetPositiveSetting("ApiRequestTimeoutSeconds", 60));

        public static void LoadDefaults()
        {
            if (string.IsNullOrWhiteSpace(VariableDictionary._serverIP))
                VariableDictionary._serverIP = GetAppSetting("ApiServerHost", string.Empty);

            // 保留登录窗口/历史配置中的端口；只有缺失或非法时才使用兼容默认值。
            if (!IsValidPort(VariableDictionary._apiPort))
                VariableDictionary._apiPort = DefaultApiPort;
        }

        public static string Build(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("API 路径不能为空。", nameof(relativePath));

            LoadDefaults();
            string scheme = GetScheme();
            string host = VariableDictionary._serverIP?.Trim() ?? string.Empty;
            int port = ApiPort;

            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("请先在登录窗口中填写服务器 IP 地址。也可以在 App.config 的 ApiServerHost 中配置。" );

            if (!Uri.CheckHostName(host).Equals(UriHostNameType.Dns) &&
                !Uri.CheckHostName(host).Equals(UriHostNameType.IPv4) &&
                !Uri.CheckHostName(host).Equals(UriHostNameType.IPv6))
                throw new InvalidOperationException("API 服务器地址无效。请检查登录窗口中的服务器 IP 或主机名。");

            if (!IsValidPort(port))
                throw new InvalidOperationException("API 服务器端口必须在 1 到 65535 之间。");

            var baseUri = new UriBuilder(scheme, host, port).Uri;
            return new Uri(baseUri, relativePath.TrimStart('/')).ToString();
        }

        public static string GetBaseUrl()
        {
            LoadDefaults();
            string scheme = GetScheme();
            string host = VariableDictionary._serverIP?.Trim() ?? string.Empty;
            int port = ApiPort;
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("请先在登录窗口中填写服务器 IP 地址。也可以在 App.config 的 ApiServerHost 中配置。" );
            if (!IsValidPort(port))
                throw new InvalidOperationException("API 服务器端口必须在 1 到 65535 之间。");
            return new UriBuilder(scheme, host, port).Uri.GetLeftPart(UriPartial.Authority);
        }

        private static bool IsValidPort(int port)
        {
            return port >= 1 && port <= 65535;
        }

        public static string GetDisplayAddress()
        {
            LoadDefaults();
            string host = VariableDictionary._serverIP?.Trim() ?? string.Empty;
            return Uri.CheckHostName(host) == UriHostNameType.IPv6
                ? $"[{host}]:{ApiPort}"
                : $"{host}:{ApiPort}";
        }

        private static string GetScheme()
        {
            string scheme = GetAppSetting("ApiServerScheme", "http").ToLowerInvariant();
            if (scheme != "http" && scheme != "https")
                throw new InvalidOperationException("API 协议只能配置为 http 或 https。");
            return scheme;
        }

        private static int GetPositiveSetting(string key, int fallback)
        {
            string value = GetAppSetting(key, fallback.ToString());
            return int.TryParse(value, out int seconds) && seconds > 0 && seconds <= 3600
                ? seconds
                : fallback;
        }

        private static string GetAppSetting(string key, string fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
