namespace GB_CadAndSWPlus_V.UploadApi.Filters;

/// <summary>新写接口不记录用户可控的请求内容，防止意外记录凭据。</summary>
public static class ElementPropertyDefinitionLogProtection
{
    public static bool ShouldHideRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)) return false;
        string[] parts = (request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 5
            && string.Equals(parts[0], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "elements", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[3], "properties", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[4], "definitions", StringComparison.OrdinalIgnoreCase);
    }
}
