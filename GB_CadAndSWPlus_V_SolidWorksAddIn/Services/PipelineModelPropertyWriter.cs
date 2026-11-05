using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>仅为本次新建的未保存零件添加文件级文本属性，不覆盖模板中的现有属性。</summary>
    internal static class PipelineModelPropertyWriter
    {
        public static void Write(SldWorks application, ModelDoc2 document, IDictionary<string, string> attributes)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in attributes)
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Key != item.Key.Trim() ||
                    item.Key.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 || item.Value == null)
                    throw new ArgumentException("管道属性包含无效字段名称或空引用。", nameof(attributes));
                if (values.ContainsKey(item.Key))
                    throw new ArgumentException("管道属性包含重复字段。", nameof(attributes));
                values.Add(item.Key, item.Value);
            }

            RequireNewActivePart(application, document);
            ModelDocExtension extension = document.Extension ??
                throw new InvalidOperationException("新零件的属性接口不可用。");
            CustomPropertyManager fileProperties = extension.CustomPropertyManager[string.Empty] ??
                throw new InvalidOperationException("新零件的文件属性接口不可用。");
            object configurationNames = document.GetConfigurationNames();
            var configurations = configurationNames as Array;
            if (configurations == null || configurations.Length == 0)
                throw new InvalidOperationException("无法确认新零件的配置列表。");

            // 仅检查实际写入的字段；未填写的字段保留模板原值，包括模板中的重量公式。
            foreach (var item in values)
            {
                if (item.Value.Length == 0) continue;
                string tag = item.Key;
                RequireNewActivePart(application, document);
                if (ContainsProperty(fileProperties, tag))
                    throw new InvalidOperationException("零件模板已包含管道字段“" + tag + "”，未覆盖现有属性。");
                foreach (object configurationName in configurations)
                {
                    var name = configurationName as string;
                    if (string.IsNullOrWhiteSpace(name))
                        throw new InvalidOperationException("零件配置名称无效，无法检查属性冲突。");
                    RequireNewActivePart(application, document);
                    CustomPropertyManager configurationProperties = extension.CustomPropertyManager[name] ??
                        throw new InvalidOperationException("零件配置属性接口不可用。");
                    if (ContainsProperty(configurationProperties, tag))
                        throw new InvalidOperationException("零件配置已包含管道字段“" + tag + "”，未覆盖现有属性。");
                }
            }

            SolidWorksFileLogger.Info("开始写入新直管零件的文件级属性；字段数=" + values.Count + "。");
            int written = 0;
            foreach (var item in values)
            {
                // 空值不创建、修改或删除属性。
                if (item.Value.Length == 0) continue;
                RequireNewActivePart(application, document);
                int result = fileProperties.Add3(item.Key, (int)swCustomInfoType_e.swCustomInfoText,
                    item.Value, (int)swCustomPropertyAddOption_e.swCustomPropertyOnlyIfNew);
                if (result != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                    throw new InvalidOperationException("写入管道字段“" + item.Key + "”失败，返回码=" + result +
                        "；新零件可能已写入部分属性，未自动回滚。");
                RequireNewActivePart(application, document);
                string raw;
                if (!TryReadProperty(fileProperties, item.Key, out raw) ||
                    !string.Equals(raw, item.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException("管道字段“" + item.Key + "”写入后校验失败；新零件可能已写入部分属性，未自动回滚。");
                written++;
            }
            RequireNewActivePart(application, document);
            SolidWorksFileLogger.Info("新直管零件文件级属性已写入并逐项校验；已写入字段数=" + written + "；尚未保存。");
        }

        private static bool ContainsProperty(CustomPropertyManager properties, string tag)
        {
            string value;
            return TryReadProperty(properties, tag, out value);
        }

        private static bool TryReadProperty(CustomPropertyManager properties, string tag, out string value)
        {
            string resolved;
            bool wasResolved, linked;
            // 读取原始文本即可，避免为解析属性切换零件配置。
            int result = properties.Get6(tag, true, out value, out resolved, out wasResolved, out linked);
            if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent) return false;
            if (result != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_CachedValue &&
                result != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_ResolvedValue)
                throw new InvalidOperationException("读取管道字段“" + tag + "”失败，返回码=" + result + "。");
            return true;
        }

        private static void RequireNewActivePart(SldWorks application, ModelDoc2 document)
        {
            object active = application.ActiveDoc;
            if (active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame ||
                document.GetType() != (int)swDocumentTypes_e.swDocPART || !string.IsNullOrEmpty(document.GetPathName()))
                throw new InvalidOperationException("新建的未保存零件不再是活动文档，已停止写入属性。");
        }
    }
}
