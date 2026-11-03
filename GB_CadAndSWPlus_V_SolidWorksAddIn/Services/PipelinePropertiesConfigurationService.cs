using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Principal;
using System.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>配置内容损坏或版本不受支持，不能未经确认覆盖。</summary>
    public sealed class PipelinePropertiesInvalidConfigurationException : Exception
    {
        public PipelinePropertiesInvalidConfigurationException(string message, Exception innerException = null)
            : base(message, innerException) { }
    }

    /// <summary>保存用户明确允许复用的管道属性；缺失配置返回空字典，损坏或读写失败则抛出异常。</summary>
    public sealed class PipelinePropertiesConfigurationService
    {
        private static readonly object SyncRoot = new object();
        private readonly string _filePath;

        public PipelinePropertiesConfigurationService()
        {
            _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GB_CADPLUS", "SolidWorksCache", "Pipeline", "last-properties.json");
        }

        public string FilePath => _filePath;

        /// <summary>读取最后属性，仅恢复核心属性和本次字段定义明确允许复用的 Tag。</summary>
        public IDictionary<string, string> Load(IEnumerable<PipelinePropertiesFieldDefinition> fields)
        {
            var allowed = GetAllowedTags(fields);
            SolidWorksFileLogger.Start("读取绘管本地配置");
            try
            {
                lock (SyncRoot)
                using (AcquireConfigurationLock())
                {
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in ReadStoredAttributes())
                        if (allowed.Contains(pair.Key)) result.Add(pair.Key, pair.Value);
                    SolidWorksFileLogger.Complete("读取绘管本地配置");
                    return result;
                }
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Error("绘管本地配置读取失败；异常类型=" + ex.GetType().Name + "。");
                throw;
            }
        }

        /// <summary>
        /// 合并已保存的其他可复用字段并原子替换；仅在用户明确同意丢弃损坏配置后允许传入 true。
        /// </summary>
        public void Save(IEnumerable<PipelinePropertiesFieldDefinition> fields, IDictionary<string, string> values,
            bool overwriteInvalidConfiguration = false)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var definitions = fields.ToList();
            var allowed = GetAllowedTags(definitions);
            var present = new HashSet<string>(definitions.Where(field => field != null && !string.IsNullOrWhiteSpace(field.Tag))
                .Select(field => field.Tag), StringComparer.OrdinalIgnoreCase);

            SolidWorksFileLogger.Start("保存绘管本地配置");
            string temporaryPath = null;
            try
            {
                lock (SyncRoot)
                using (AcquireConfigurationLock())
                {
                    Dictionary<string, string> filtered;
                    try
                    {
                        // 只覆盖当前目录中出现的属性，保留离线精简目录未提供的其他可复用属性。
                        filtered = ReadStoredAttributes();
                    }
                    catch (PipelinePropertiesInvalidConfigurationException)
                    {
                        if (!overwriteInvalidConfiguration) throw;
                        SolidWorksFileLogger.Warning("用户已确认覆盖损坏的绘管配置，无法保留其中的原属性。");
                        filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (string tag in filtered.Keys.ToList())
                        if (!IsReusableTag(tag) || (present.Contains(tag) && !allowed.Contains(tag)))
                            filtered.Remove(tag);
                    foreach (var pair in values)
                        if (allowed.Contains(pair.Key))
                            filtered[pair.Key] = pair.Value ?? string.Empty;

                    string directory = Path.GetDirectoryName(_filePath);
                    Directory.CreateDirectory(directory);
                    temporaryPath = Path.Combine(directory, "last-properties." + Guid.NewGuid().ToString("N") + ".new");
                    string json = JsonConvert.SerializeObject(new { Version = 1, Attributes = filtered }, Formatting.Indented);
                    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(json);
                        writer.Flush();
                        stream.Flush(true);
                    }

                    if (File.Exists(_filePath))
                        File.Replace(temporaryPath, _filePath, null);
                    else
                        File.Move(temporaryPath, _filePath);
                    temporaryPath = null;
                }
                SolidWorksFileLogger.Complete("保存绘管本地配置");
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Error("绘管本地配置保存失败；异常类型=" + ex.GetType().Name + "。");
                throw;
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex)
                    {
                        SolidWorksFileLogger.Error("绘管配置临时文件清理失败；异常类型=" + ex.GetType().Name + "。");
                    }
                }
            }
        }

        private Dictionary<string, string> ReadStoredAttributes()
        {
            string json;
            try
            {
                using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    json = reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                SolidWorksFileLogger.Info("绘管本地配置不存在，使用空属性。");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (DirectoryNotFoundException)
            {
                SolidWorksFileLogger.Info("绘管本地配置目录不存在，使用空属性。");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (DecoderFallbackException ex)
            {
                throw new PipelinePropertiesInvalidConfigurationException("绘管配置编码无效。", ex);
            }

            try
            {
                JObject root;
                using (var textReader = new StringReader(json))
                using (var jsonReader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(jsonReader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (root["Version"]?.Type != JTokenType.Integer || root["Version"].ToString() != "1" ||
                    !(root["Attributes"] is JObject attributes))
                    throw new PipelinePropertiesInvalidConfigurationException("绘管配置格式或版本无效。");

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty property in attributes.Properties())
                {
                    if (property.Value.Type != JTokenType.String)
                        throw new PipelinePropertiesInvalidConfigurationException("绘管配置属性值必须为字符串。");
                    if (!seen.Add(property.Name))
                        throw new PipelinePropertiesInvalidConfigurationException("绘管配置包含重复属性。");
                    // 配置可能来自旧版本；读入时重新过滤实例与敏感字段。
                    if (property.Name == property.Name.Trim() && IsReusableTag(property.Name))
                        result.Add(property.Name, (string)property.Value);
                }
                return result;
            }
            catch (JsonException ex)
            {
                throw new PipelinePropertiesInvalidConfigurationException("绘管配置 JSON 无效。", ex);
            }
        }

        private static HashSet<string> GetAllowedTags(IEnumerable<PipelinePropertiesFieldDefinition> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var coreTags = new HashSet<string>(PipelinePropertiesCoreFields.Create().Select(field => field.Tag),
                StringComparer.OrdinalIgnoreCase);
            return new HashSet<string>(fields.Where(field => field != null && field.Editable &&
                !string.IsNullOrWhiteSpace(field.Tag) && field.Tag == field.Tag.Trim() &&
                (field.SaveAsLastProperty || coreTags.Contains(field.Tag)) && IsReusableTag(field.Tag))
                .Select(field => field.Tag), StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsReusableTag(string tag)
        {
            // 即使调用方误将这些实例、身份、路线或敏感字段标为可保存，也不能写入配置。
            string key = tag.Trim().ToUpperInvariant();
            if (key == "PIPE_ID") return true; // CAD 管道字段 PIPE_ID 表示内径，不是实例标识。
            return key != "TAG_NO" && key != "PIPELINETITLE" && key != "START_POINT" && key != "END_POINT" &&
                key != "PIPE_LENGTH" && key != "PIPE_WEIGHT" && key != "QTY" && key != "WEIGHT" &&
                key != "SW_MODEL" && key != "NAME" && key != "MODEL" &&
                !key.Contains("ROUTE") && !key.Contains("PATH") && !key.Contains("POINT") &&
                !key.Contains("COORD") && !key.Contains("POSITION") && !key.Contains("IDENTITY") &&
                !key.Contains("INSTANCE") && !key.Contains("SEGMENT") && !key.Contains("DEVICE") &&
                !key.Contains("EQUIPMENT") && !key.Contains("SERIAL") &&
                key != "LINE_NO" && key != "LINE_NUMBER" && key != "PIPE_NO" && key != "PIPE_NUMBER" &&
                key != "PIPELINE_NO" && key != "TAG" && !key.Contains("USER") &&
                !key.Contains("ACCOUNT") && !key.Contains("TOKEN") && !key.Contains("API_KEY") &&
                !key.Contains("AUTH") && !key.Contains("CREDENTIAL") && !key.Contains("SESSION") &&
                !key.Contains("DEPARTMENT") && !key.Contains("DEPT") &&
                !key.Contains("PASSWORD") && !key.Contains("SECRET") && !key.Contains("GUID") &&
                key != "ID" && !key.EndsWith("_ID", StringComparison.Ordinal);
        }

        private static IDisposable AcquireConfigurationLock()
        {
            string userSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                userSid = identity.User?.Value ?? throw new InvalidOperationException("无法确定当前用户，不能安全访问绘管配置。");

            var mutex = new Mutex(false, @"Global\GB_CADPLUS_PipelineProperties_" + userSid);
            try
            {
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                        throw new IOException("绘管配置正在由其他程序使用，请稍后重试。");
                }
                catch (AbandonedMutexException)
                {
                    SolidWorksFileLogger.Warning("检测到上次绘管配置操作中断，将重新检查配置文件。");
                }
                return new ConfigurationLock(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        private sealed class ConfigurationLock : IDisposable
        {
            private readonly Mutex _mutex;

            public ConfigurationLock(Mutex mutex) { _mutex = mutex; }

            public void Dispose()
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
    }
}
