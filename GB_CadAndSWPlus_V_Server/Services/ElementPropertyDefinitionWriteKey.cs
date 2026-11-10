using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public enum ElementPropertyDefinitionWriteKeyResult
{
    Allowed,
    Denied,
    NotConfigured
}

/// <summary>仅校验此写接口的独立密钥，配置中只保存摘要。</summary>
public static class ElementPropertyDefinitionWriteKey
{
    public static ElementPropertyDefinitionWriteKeyResult Validate(HttpRequest request, IConfiguration configuration)
    {
        string? configuredHash = configuration["ElementPropertyDefinitions:WriteKeySha256"];
        if (configuredHash == null || configuredHash.Length != 64 || !configuredHash.All(IsHex))
            return ElementPropertyDefinitionWriteKeyResult.NotConfigured;

        var headers = request.Headers["X-Element-Definition-Key"];
        if (headers.Count != 1 || headers[0] is not string key || key.Length != 43
            || !key.All(IsBase64Url) || !"AEIMQUYcgkosw048".Contains(key[42]))
            return ElementPropertyDefinitionWriteKeyResult.Denied;

        // 对 43 字符 Base64URL 文本本身取 SHA-256，按固定时间比较摘要。
        byte[] expected = Convert.FromHexString(configuredHash);
        Span<byte> keyBytes = stackalloc byte[43];
        Span<byte> actual = stackalloc byte[32];
        Encoding.UTF8.GetBytes(key.AsSpan(), keyBytes);
        SHA256.HashData(keyBytes, actual);
        CryptographicOperations.ZeroMemory(keyBytes);
        bool matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        CryptographicOperations.ZeroMemory(actual);
        CryptographicOperations.ZeroMemory(expected);
        return matches ? ElementPropertyDefinitionWriteKeyResult.Allowed : ElementPropertyDefinitionWriteKeyResult.Denied;
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsBase64Url(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
}
