using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using Newtonsoft.Json;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>读取 CAD 和 SolidWorks 共用的管道字段目录。</summary>
    internal sealed class PipelineFieldCatalogService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public async Task<IReadOnlyList<PipelinePropertiesFieldDefinition>> GetFieldsAsync(
            SharedLoginSession session, CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(session.ServerHost))
                throw new InvalidOperationException("未配置服务器地址，无法读取共享管道字段目录。");
            if (session.ApiPort < 1 || session.ApiPort > 65535)
                throw new InvalidOperationException("API 端口无效，无法读取共享管道字段目录。");

            string url = UnifiedSolidWorksLoginService.BuildUrl(session.ServerHost, session.ApiPort, "api/pipelines/fields");
            SolidWorksFileLogger.Start("读取共享管道字段目录");
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS");
                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("读取共享管道字段目录失败，HTTP " + (int)response.StatusCode + "。");

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    CatalogResponse payload;
                    try
                    {
                        payload = JsonConvert.DeserializeObject<CatalogResponse>(json);
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidOperationException("服务器返回的管道字段目录格式无效。", ex);
                    }

                    if (payload == null || !payload.Success)
                        throw new InvalidOperationException("服务器未成功返回管道字段目录。");
                    if (payload.Fields == null || payload.Fields.Count == 0)
                        throw new InvalidOperationException("服务器返回的管道字段目录为空或格式无效。");

                    var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var fields = new List<PipelinePropertiesFieldDefinition>();
                    foreach (CatalogField field in payload.Fields)
                    {
                        if (field == null || string.IsNullOrWhiteSpace(field.Tag) ||
                            field.Tag != field.Tag.Trim() || !tags.Add(field.Tag) ||
                            !field.Required.HasValue || !field.Editable.HasValue)
                            throw new InvalidOperationException("服务器返回的管道字段目录包含无效或重复字段。");

                        fields.Add(new PipelinePropertiesFieldDefinition
                        {
                            Tag = field.Tag,
                            Label = field.Prompt ?? string.Empty,
                            Group = field.Group ?? string.Empty,
                            SortOrder = field.DisplayOrder,
                            Required = field.Required.Value,
                            Editable = field.Editable.Value,
                            Multiline = string.Equals(field.DataType, "multiline", StringComparison.OrdinalIgnoreCase),
                            Options = field.Options ?? new List<string>(),
                            SaveAsLastProperty = field.Editable.Value
                        });
                    }

                    SolidWorksFileLogger.Complete("读取共享管道字段目录；数量=" + fields.Count);
                    return fields.AsReadOnly();
                }
            }
        }

        private sealed class CatalogResponse
        {
            public bool Success { get; set; }
            public List<CatalogField> Fields { get; set; }
        }

        private sealed class CatalogField
        {
            public string Tag { get; set; }
            public string Prompt { get; set; }
            public string Group { get; set; }
            public string DataType { get; set; }
            public int DisplayOrder { get; set; }
            public bool? Required { get; set; }
            public bool? Editable { get; set; }
            public List<string> Options { get; set; }
        }
    }
}
