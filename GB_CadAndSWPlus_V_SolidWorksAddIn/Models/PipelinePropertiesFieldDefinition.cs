using System;
using System.Collections.Generic;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>由调用方根据服务端管道字段目录转换；Tag 必须使用服务端原值。</summary>
    public sealed class PipelinePropertiesFieldDefinition
    {
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool Required { get; set; }
        public bool Editable { get; set; } = true;
        public bool Multiline { get; set; }
        public IList<string> Options { get; set; } = new List<string>();

        /// <summary>只有明确允许复用的非实例属性才可设为 true；实例字段仍会被服务拒绝持久化。</summary>
        public bool SaveAsLastProperty { get; set; }
    }

    /// <summary>离线可用的最小字段集，不包含工程默认值或推断选项。</summary>
    public static class PipelinePropertiesCoreFields
    {
        public static IList<PipelinePropertiesFieldDefinition> Create()
        {
            return new List<PipelinePropertiesFieldDefinition>
            {
                Field("DN", "公称通径", "设计条件", 110),
                Field("PN", "公称压力", "设计条件", 160),
                Field("PIPE_MATL", "管道材质", "材料与连接", 270),
                Field("MEDIUM", "适用介质", "介质与分类", 230),
                Field("PIPE_OD", "管道外径（mm）", "尺寸计算", 120),
                Field("PIPE_THK", "管道壁厚（mm）", "尺寸计算", 140),
                Field("SCHEDULE", "壁厚等级", "尺寸计算", 150)
            };
        }

        private static PipelinePropertiesFieldDefinition Field(string tag, string label, string group, int order)
        {
            return new PipelinePropertiesFieldDefinition
            {
                Tag = tag, Label = label, Group = group, SortOrder = order, SaveAsLastProperty = true
            };
        }
    }
}
