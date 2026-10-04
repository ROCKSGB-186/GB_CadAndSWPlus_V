using System.Collections.Generic;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>SolidWorks 管理页使用的部门模型，与统一服务器部门接口字段保持一致。</summary>
    public sealed class SolidWorksDepartmentModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public int UserCount { get; set; }
    }

    /// <summary>SolidWorks 管理页使用的用户模型。</summary>
    public sealed class SolidWorksUserModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string RealName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>统一 API 变更接口返回结构。</summary>
    public sealed class SolidWorksMutationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Id { get; set; }
    }

    /// <summary>统一服务器部门列表返回结构。</summary>
    public sealed class SolidWorksDepartmentResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<SolidWorksDepartmentModel> Departments { get; set; } = new List<SolidWorksDepartmentModel>();
    }

    /// <summary>统一服务器用户列表返回结构。</summary>
    public sealed class SolidWorksUserResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<SolidWorksUserModel> Users { get; set; } = new List<SolidWorksUserModel>();
    }

    /// <summary>工具设置读写使用的配置返回结构。</summary>
    public sealed class SolidWorksConfigResponse
    {
        public bool Success { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
