using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    [DataContract]
    public sealed class SwTemplateOptions
    {
        [DataMember(Name = "platformCode")]
        public string PlatformCode { get; set; } = "GB-PIPELINE";

        [DataMember(Name = "serverDownloadPath")]
        public string? ServerDownloadPath { get; set; }

        [DataMember(Name = "bundledResourcePath")]
        public string? BundledResourcePath { get; set; }
    }
}
