using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.Shared.Models
{
    /// <summary>CAD、SolidWorks 和未来客户端共用的部门模型。</summary>
    [DataContract]
    public sealed class UnifiedDepartmentModel
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; } = string.Empty;
        [DataMember(Name = "displayName")] public string DisplayName { get; set; } = string.Empty;
        [DataMember(Name = "description")] public string Description { get; set; } = string.Empty;
        [DataMember(Name = "sortOrder")] public int SortOrder { get; set; }
        [DataMember(Name = "isActive")] public bool IsActive { get; set; } = true;
        [DataMember(Name = "userCount")] public int UserCount { get; set; }
    }

    /// <summary>CAD、SolidWorks 和未来客户端共用的用户模型。</summary>
    [DataContract]
    public sealed class UnifiedUserModel
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "username")] public string Username { get; set; } = string.Empty;
        [DataMember(Name = "realName")] public string RealName { get; set; } = string.Empty;
        [DataMember(Name = "role")] public string Role { get; set; } = string.Empty;
        [DataMember(Name = "gender")] public string Gender { get; set; } = string.Empty;
        [DataMember(Name = "phone")] public string Phone { get; set; } = string.Empty;
        [DataMember(Name = "email")] public string Email { get; set; } = string.Empty;
        [DataMember(Name = "departmentId")] public int? DepartmentId { get; set; }
        [DataMember(Name = "departmentName")] public string DepartmentName { get; set; } = string.Empty;
        [DataMember(Name = "isActive")] public bool IsActive { get; set; }
    }

    [DataContract]
    public sealed class UnifiedDepartmentListResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
        [DataMember(Name = "departments")] public List<UnifiedDepartmentModel> Departments { get; set; } = new List<UnifiedDepartmentModel>();
    }

    [DataContract]
    public sealed class UnifiedUserListResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
        [DataMember(Name = "users")] public List<UnifiedUserModel> Users { get; set; } = new List<UnifiedUserModel>();
    }

    [DataContract]
    public sealed class UnifiedDepartmentMutationResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
        [DataMember(Name = "id")] public int Id { get; set; }
    }

    [DataContract]
    public sealed class UnifiedUserMutationResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
        [DataMember(Name = "id")] public int Id { get; set; }
    }
}
