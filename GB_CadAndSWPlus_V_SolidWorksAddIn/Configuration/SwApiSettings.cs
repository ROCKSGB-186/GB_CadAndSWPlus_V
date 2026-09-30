using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration
{
    [DataContract]
    public sealed class SwApiSettings
    {
        [DataMember(Name = "serverHost")]
        public string ServerHost { get; set; } = string.Empty;

        [DataMember(Name = "apiPort")]
        public int ApiPort { get; set; } = 10010;

        public string GetBaseUrl()
        {
            string host = (ServerHost ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("请填写服务器地址。");

            if (ApiPort < 1 || ApiPort > 65535)
                throw new InvalidOperationException("API 端口必须在 1 到 65535 之间。");

            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("http://".Length);
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("https://".Length);

            return "http://" + host.TrimEnd('/') + ":" + ApiPort;
        }

        public void Save(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var serializer = new DataContractJsonSerializer(typeof(SwApiSettings));
            using (var stream = File.Create(path))
            {
                serializer.WriteObject(stream, this);
            }
        }

        public static SwApiSettings Load(string path)
        {
            if (!File.Exists(path))
                return new SwApiSettings();

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SwApiSettings));
                using (var stream = File.OpenRead(path))
                {
                    return serializer.ReadObject(stream) as SwApiSettings ?? new SwApiSettings();
                }
            }
            catch
            {
                return new SwApiSettings();
            }
        }
    }

    [DataContract]
    public sealed class SwUserSession
    {
        [DataMember(Name = "settings")]
        public SwApiSettings Settings { get; set; } = new SwApiSettings();

        [DataMember(Name = "user")]
        public Models.UserDto? User { get; set; }

        [DataMember(Name = "accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [DataMember(Name = "accessTokenExpiresAtUtc")]
        public DateTime? AccessTokenExpiresAtUtc { get; set; }

        public bool IsSignedIn => User != null && User.IsActive;

        public static string SessionPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GB_CADPLUS",
            "SolidWorksAddIn",
            "session.json");

        public void Save()
        {
            string? directory = Path.GetDirectoryName(SessionPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var serializer = new DataContractJsonSerializer(typeof(SwUserSession));
            using (var stream = File.Create(SessionPath))
            {
                serializer.WriteObject(stream, this);
            }
        }

        public static SwUserSession Load()
        {
            if (!File.Exists(SessionPath))
                return new SwUserSession();

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SwUserSession));
                using (var stream = File.OpenRead(SessionPath))
                {
                    return serializer.ReadObject(stream) as SwUserSession ?? new SwUserSession();
                }
            }
            catch
            {
                return new SwUserSession();
            }
        }

        public void Clear()
        {
            User = null;
            Save();
        }
    }
}
