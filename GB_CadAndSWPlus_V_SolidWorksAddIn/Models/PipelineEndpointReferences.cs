using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>仅为本次路线记录的参考值及其来源，不代表设备或管口关联。</summary>
    internal sealed class PipelineEndpointReferences
    {
        public PipelineEndpointReferences(IDictionary<string, string> start, IDictionary<string, string> end,
            IDictionary<string, string>? startSources = null, IDictionary<string, string>? endSources = null)
        {
            Start = new Dictionary<string, string>(start, StringComparer.OrdinalIgnoreCase);
            End = new Dictionary<string, string>(end, StringComparer.OrdinalIgnoreCase);
            StartSources = new Dictionary<string, string>(startSources ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            EndSources = new Dictionary<string, string>(endSources ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, string> Start { get; }
        public IReadOnlyDictionary<string, string> End { get; }
        public IReadOnlyDictionary<string, string> StartSources { get; }
        public IReadOnlyDictionary<string, string> EndSources { get; }
    }

    internal sealed class PipelineEndpointReferenceChange
    {
        public PipelineEndpointReferenceChange(string tag, string value, string current, string other)
        {
            Tag = tag;
            Value = value;
            Current = current;
            Other = other;
        }

        public string Tag { get; }
        public string Value { get; }
        public string Current { get; }
        public string Other { get; }
        public bool ConflictsWithOther => Other.Length != 0 &&
            !string.Equals(Value, Other, StringComparison.OrdinalIgnoreCase);
    }

    internal static class PipelineEndpointReferencePlanner
    {
        private static readonly string[] Tags = { "DN", "PN", "MEDIUM" };

        /// <summary>仅生成建议，不修改管道或端点数据；空值和只读字段不能覆盖。</summary>
        public static IReadOnlyList<PipelineEndpointReferenceChange> Create(
            IDictionary<string, string> source, IDictionary<string, string> other,
            IDictionary<string, string> current, ISet<string> editable)
        {
            var changes = new List<PipelineEndpointReferenceChange>();
            foreach (string tag in Tags)
            {
                if (!editable.Contains(tag) || !source.TryGetValue(tag, out string value) ||
                    string.IsNullOrWhiteSpace(value)) continue;
                current.TryGetValue(tag, out string existing);
                other.TryGetValue(tag, out string otherValue);
                changes.Add(new PipelineEndpointReferenceChange(tag, value.Trim(),
                    (existing ?? string.Empty).Trim(), (otherValue ?? string.Empty).Trim()));
            }
            return new ReadOnlyCollection<PipelineEndpointReferenceChange>(changes);
        }
    }
}
