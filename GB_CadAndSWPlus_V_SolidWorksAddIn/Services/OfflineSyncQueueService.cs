using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    public sealed class OfflineSyncQueueService
    {
        public void Enqueue<T>(string operation, T payload)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, payload);
                var queue = OfflineSyncQueue.Load();
                queue.Items.Add(new OfflineSyncItem
                {
                    Operation = operation,
                    PayloadJson = Encoding.UTF8.GetString(stream.ToArray()),
                    CreatedAt = DateTime.Now
                });
                queue.Save();
            }
        }

        public int Count => OfflineSyncQueue.Load().Items.Count;
    }
}
