using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 回归工程使用隔离桩，不读取真实会话或访问网络。
namespace GB_CadAndSWPlus_V.Shared.Services
{
    internal sealed class SharedLoginSession
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime? AccessTokenExpiresAtUtc { get; set; }
        public string Username { get; set; } = string.Empty;
        public string ServerHost { get; set; } = string.Empty;
        public int ApiPort { get; set; }
    }

    internal sealed class SharedLoginSessionStore
    {
        public SharedLoginSession Load() => throw new InvalidOperationException("回归测试禁止读取登录会话。");
    }
}

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class SolidWorksResourceApiService
    {
        public SolidWorksResourceApiService(GB_CadAndSWPlus_V.Shared.Services.SharedLoginSession session) =>
            throw new InvalidOperationException("回归测试禁止访问网络。");
        public SolidWorksResourceApiService() { }
        public Func<string, CancellationToken, Task<ElementListResponse>>? Search { get; set; }
        public Func<long, CancellationToken, Task<PropertyListResponse>>? List { get; set; }
        public Task<ElementListResponse> SearchElementsAsync(string keyword, CancellationToken token) =>
            (Search ?? throw new InvalidOperationException("未配置搜索测试替身。"))(keyword, token);
        public Task<PropertyListResponse> ListPropertiesAsync(long id, CancellationToken token) =>
            (List ?? throw new InvalidOperationException("未配置属性测试替身。"))(id, token);

        public sealed class ElementListResponse
        {
            public bool Success { get; set; }
            public List<ElementDto>? Elements { get; set; }
        }
        public sealed class ElementDto
        {
            public long Id { get; set; }
            public string ElementCode { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
        public sealed class PropertyListResponse
        {
            public bool Success { get; set; }
            public List<PropertyValueDto>? Data { get; set; }
        }
        public sealed class PropertyValueDto
        {
            public long ElementDefinitionId { get; set; }
            public string PropertyCode { get; set; } = string.Empty;
            public string PropertyName { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public string ValueText { get; set; } = string.Empty;
            public decimal? ValueNumber { get; set; }
            public bool? ValueBool { get; set; }
        }
    }
}

namespace Newtonsoft.Json
{
    // 窗口只按异常类型分类；测试不执行 JSON 解析。
    internal class JsonException : Exception { }
}
