using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    [DataContract]
    public sealed class OfflineSyncItem
    {
        [DataMember(Name = "operation")]
        public string Operation { get; set; } = "RegisterSwInstance";

        [DataMember(Name = "payloadJson")]
        public string PayloadJson { get; set; } = string.Empty;

        [DataMember(Name = "createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    [DataContract]
    public sealed class OfflineSyncQueue
    {
        [DataMember(Name = "items")]
        public List<OfflineSyncItem> Items { get; set; } = new List<OfflineSyncItem>();

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GB_CADPLUS", "SolidWorksAddIn", "offline-sync.json");

        public static OfflineSyncQueue Load()
        {
            if (!File.Exists(FilePath)) return new OfflineSyncQueue();
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(OfflineSyncQueue));
                using (var stream = File.OpenRead(FilePath))
                    return serializer.ReadObject(stream) as OfflineSyncQueue ?? new OfflineSyncQueue();
            }
            catch { return new OfflineSyncQueue(); }
        }

        public void Save()
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var serializer = new DataContractJsonSerializer(typeof(OfflineSyncQueue));
            using (var stream = File.Create(FilePath)) serializer.WriteObject(stream, this);
        }
    }
}
