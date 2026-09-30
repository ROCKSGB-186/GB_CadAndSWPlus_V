using System;
using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.Shared.Models
{
    public enum UnifiedLoginPlatform
    {
        Cad,
        SolidWorks
    }

    public sealed class UnifiedLoginRequest
    {
        public UnifiedLoginPlatform Platform { get; set; }
        public string ServerHost { get; set; } = string.Empty;
        public int ApiPort { get; set; } = 10010;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DatabaseType { get; set; } = "DM";
        public int? DatabasePort { get; set; } = 5236;
        public int? DepartmentId { get; set; }
    }

    public sealed class UnifiedLoginResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public object PlatformSession { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public DateTime? AccessTokenExpiresAtUtc { get; set; }
    }

    [DataContract]
    public sealed class SharedLoginSession
    {
        [DataMember(Name = "serverHost")] public string ServerHost { get; set; } = string.Empty;
        [DataMember(Name = "apiPort")] public int ApiPort { get; set; } = 10010;
        [DataMember(Name = "databaseType")] public string DatabaseType { get; set; } = "DM";
        [DataMember(Name = "databasePort")] public int DatabasePort { get; set; } = 5236;
        [DataMember(Name = "username")] public string Username { get; set; } = string.Empty;
        [DataMember(Name = "encryptedPassword")] public string EncryptedPassword { get; set; } = string.Empty;
        [DataMember(Name = "displayName")] public string DisplayName { get; set; } = string.Empty;
        [DataMember(Name = "userId")] public int? UserId { get; set; }
        [DataMember(Name = "departmentId")] public int? DepartmentId { get; set; }
        [DataMember(Name = "departmentName")] public string DepartmentName { get; set; } = string.Empty;
        [DataMember(Name = "updatedUtc")] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        [DataMember(Name = "accessToken")] public string AccessToken { get; set; } = string.Empty;
        [DataMember(Name = "accessTokenExpiresAtUtc")] public DateTime? AccessTokenExpiresAtUtc { get; set; }
    }

    public sealed class UnifiedDepartmentOption
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}