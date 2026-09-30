using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    [DataContract]
    public sealed class LoginRequest
    {
        [DataMember(Name = "username")]
        public string Username { get; set; } = string.Empty;

        [DataMember(Name = "password")]
        public string Password { get; set; } = string.Empty;

        [DataMember(Name = "clientPlatform")]
        public string ClientPlatform { get; set; } = "SolidWorks";
    }

    [DataContract]
    public sealed class LoginResponse
    {
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; } = string.Empty;

        [DataMember(Name = "user")]
        public UserDto? User { get; set; }

        [DataMember(Name = "accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [DataMember(Name = "accessTokenExpiresAtUtc")]
        public System.DateTime? AccessTokenExpiresAtUtc { get; set; }
    }

    [DataContract]
    public sealed class UserDto
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "username")]
        public string Username { get; set; } = string.Empty;

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [DataMember(Name = "realName")]
        public string RealName { get; set; } = string.Empty;

        [DataMember(Name = "role")]
        public string Role { get; set; } = string.Empty;

        [DataMember(Name = "departmentId")]
        public int? DepartmentId { get; set; }

        [DataMember(Name = "departmentName")]
        public string DepartmentName { get; set; } = string.Empty;

        [DataMember(Name = "isActive")]
        public bool IsActive { get; set; }
    }
}
