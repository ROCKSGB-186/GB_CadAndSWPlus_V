using System.Collections.Generic;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>SolidWorks 管理页使用的部门模型，与统一服务器部门接口字段保持一致。</summary>
    public sealed class SolidWorksDepartmentModel
    {
        /// <summary>部门数据库编号。</summary>
        public int Id { get; set; }
        /// <summary>部门内部名称。</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>部门显示名称。</summary>
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>部门说明。</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>部门排序值。</summary>
        public int SortOrder { get; set; }
        /// <summary>部门是否启用。</summary>
        public bool IsActive { get; set; } = true;
        /// <summary>部门当前人员数量。</summary>
        public int UserCount { get; set; }
    }

    /// <summary>SolidWorks 管理页使用的用户模型。</summary>
    public sealed class SolidWorksUserModel
    {
        /// <summary>用户数据库编号。</summary>
        public int Id { get; set; }
        /// <summary>用户登录账号。</summary>
        public string Username { get; set; } = string.Empty;
        /// <summary>用户真实姓名。</summary>
        public string RealName { get; set; } = string.Empty;
        /// <summary>用户角色。</summary>
        public string Role { get; set; } = string.Empty;
        /// <summary>用户性别。</summary>
        public string Gender { get; set; } = string.Empty;
        /// <summary>用户联系电话。</summary>
        public string Phone { get; set; } = string.Empty;
        /// <summary>用户电子邮箱。</summary>
        public string Email { get; set; } = string.Empty;
        /// <summary>所属部门名称。</summary>
        public string DepartmentName { get; set; } = string.Empty;
        /// <summary>用户是否启用。</summary>
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
