using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineReferenceException : InvalidOperationException
    {
        public PipelineReferenceException(string message) : base(message) { }
    }

    internal sealed class PipelineReferenceElement
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Label => Name + "（编码：" + Code + "；ID：" + Id + "；状态：" + Status + "）";
    }

    internal sealed class PipelineReferenceCandidate
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Label => Name + " [" + Code + "] = " + Value +
            (string.IsNullOrWhiteSpace(Unit) ? "（单位未提供）" : "（单位：" + Unit + "）");
    }

    /// <summary>从统一构件定义只读取得候选值；不建立模型或工程实例关联。</summary>
    internal sealed class PipelineElementReferenceSource
    {
        private readonly SolidWorksResourceApiService _api;
        private readonly Func<bool> _sessionIsCurrent;
        public PipelineElementReferenceSource(SolidWorksResourceApiService api, Func<bool>? sessionIsCurrent = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _sessionIsCurrent = sessionIsCurrent ?? (() => true);
        }

        public void EnsureSessionCurrent()
        {
            if (!_sessionIsCurrent())
                throw new PipelineReferenceException("统一登录会话已失效或切换，请关闭窗口并重新查询。");
        }

        public async Task<IReadOnlyList<PipelineReferenceElement>> SearchAsync(string keyword, CancellationToken token)
        {
            EnsureSessionCurrent();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var result = await _api.SearchElementsAsync(keyword, timeout.Token);
                    EnsureSessionCurrent();
                    if (!result.Success || result.Elements == null)
                        throw new PipelineReferenceException("数据库构件查询未成功，请检查服务器状态。");
                    if (result.Elements.Count > 200)
                        throw new PipelineReferenceException("匹配构件超过 200 个，请输入更精确的名称或编码。");
                    var ids = new HashSet<long>();
                    var elements = new List<PipelineReferenceElement>();
                    foreach (var item in result.Elements)
                    {
                        if (item == null || item.Id <= 0 || !ids.Add(item.Id) ||
                            string.IsNullOrWhiteSpace(item.ElementCode) ||
                            (string.IsNullOrWhiteSpace(item.DisplayName) && string.IsNullOrWhiteSpace(item.Name)))
                            throw new PipelineReferenceException("数据库返回的构件身份无效或重复。");
                        elements.Add(new PipelineReferenceElement
                        {
                            Id = item.Id, Code = item.ElementCode,
                            Name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName,
                            Status = string.IsNullOrWhiteSpace(item.Status) ? "未知" : item.Status
                        });
                    }
                    SolidWorksFileLogger.Info("数据库构件参考查询完成；结果数=" + elements.Count + "。");
                    return elements.AsReadOnly();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new PipelineReferenceException("数据库构件查询超时，请重试。");
                }
            }
        }

        public async Task<IReadOnlyList<PipelineReferenceCandidate>> GetPropertiesAsync(long elementId, CancellationToken token)
        {
            if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId));
            EnsureSessionCurrent();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var result = await _api.ListPropertiesAsync(elementId, timeout.Token);
                    EnsureSessionCurrent();
                    if (!result.Success || result.Data == null)
                        throw new PipelineReferenceException("数据库构件属性查询未成功，请重试。");
                    var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var candidates = new List<PipelineReferenceCandidate>();
                    int omitted = 0;
                    foreach (var property in result.Data)
                    {
                        if (property == null || property.ElementDefinitionId != elementId ||
                            string.IsNullOrWhiteSpace(property.PropertyCode) || !codes.Add(property.PropertyCode.Trim()))
                            throw new PipelineReferenceException("数据库返回的属性身份无效或字段重复。");
                        bool text = !string.IsNullOrWhiteSpace(property.ValueText);
                        int count = (text ? 1 : 0) + (property.ValueNumber.HasValue ? 1 : 0) + (property.ValueBool.HasValue ? 1 : 0);
                        string dataType = (property.DataType ?? string.Empty).Trim();
                        if (count != 1 || property.ValueBool.HasValue ||
                            string.Equals(dataType, "BOOL", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dataType, "BOOLEAN", StringComparison.OrdinalIgnoreCase))
                        {
                            omitted++;
                            continue;
                        }
                        string value = text ? property.ValueText.Trim() : property.ValueNumber!.Value.ToString(CultureInfo.InvariantCulture);
                        if (value.Any(char.IsControl))
                        {
                            omitted++;
                            continue;
                        }
                        candidates.Add(new PipelineReferenceCandidate
                        {
                            Code = property.PropertyCode.Trim(),
                            Name = string.IsNullOrWhiteSpace(property.PropertyName) ? property.PropertyCode.Trim() : property.PropertyName.Trim(),
                            Unit = property.Unit?.Trim() ?? string.Empty,
                            Value = value
                        });
                    }
                    SolidWorksFileLogger.Info("数据库构件参考属性读取完成；构件ID=" + elementId +
                        "；可选数量=" + candidates.Count + "；空值或不明确值数量=" + omitted + "。");
                    return candidates.AsReadOnly();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new PipelineReferenceException("数据库构件属性查询超时，请重试。");
                }
            }
        }
    }
}
