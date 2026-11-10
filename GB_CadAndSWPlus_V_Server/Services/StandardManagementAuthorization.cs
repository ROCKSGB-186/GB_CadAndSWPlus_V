using Microsoft.AspNetCore.Http;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// 规范库当前阶段的临时管理员识别。
/// 后续接入正式登录认证后，只替换此处即可，不改动业务接口。
/// </summary>
public static class StandardManagementAuthorization
{
    private static readonly HashSet<string> AdministratorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sa",
        "SYSDBA",
        "admin"
    };

    /// <summary>
    /// 从请求头读取操作用户名并判断是否为当前阶段管理员。
    /// </summary>
    public static bool IsAdministrator(HttpRequest request, out string operatorName)
    {
        operatorName = request.Headers["X-Operator-Name"].FirstOrDefault()?.Trim() ?? string.Empty;
        return IsAdministratorName(operatorName);
    }

    /// <summary>判断服务器认证后的用户名是否属于现有管理员名称集。</summary>
    public static bool IsAdministratorName(string? username) =>
        username != null && AdministratorNames.Contains(username);
}
