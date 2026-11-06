using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>记录受限单组件试验的可观察基线；校验通过不表示具有事务或回滚能力。</summary>
    internal sealed class PipelineSingleComponentBaseline
    {
        private const double TransformTolerance = 1e-9;
        private readonly ModelDoc2 _assemblyDocument;
        private readonly Component2 _component;
        private readonly ModelDoc2 _part;
        private readonly string _assemblyConfiguration;
        private readonly string _path;
        private readonly string _configuration;
        private readonly bool _fixed;
        private readonly bool _hidden;
        private readonly int _visibility;
        private readonly int _suppression;
        private readonly double[] _transform;

        private PipelineSingleComponentBaseline(ModelDoc2 assemblyDocument, Component2 component,
            ModelDoc2 part, string assemblyConfiguration, string path, string configuration,
            bool isFixed, bool hidden, int visibility, int suppression, double[] transform)
        {
            _assemblyDocument = assemblyDocument;
            _component = component;
            _part = part;
            _assemblyConfiguration = assemblyConfiguration;
            _path = path;
            _configuration = configuration;
            _fixed = isFixed;
            _hidden = hidden;
            _visibility = visibility;
            _suppression = suppression;
            _transform = transform;
        }

        public static PipelineSingleComponentBaseline Capture(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument)
        {
            try
            {
                RequireArguments(app, assembly, assemblyDocument);
                RequireAssemblyIdentity(app, assembly, assemblyDocument, assemblyDocument);
                string assemblyConfiguration = AssemblyConfiguration(assemblyDocument);
                Component2 component = RequireComponents(app, assembly, 1, null, null);
                RequireNoMates(assemblyDocument, component, null);
                int suppression = component.GetSuppression2();
                bool isFixed = component.IsFixed();
                bool hidden = component.IsHidden(false);
                int visibility = component.Visible;
                RequireComponentState(component, isFixed, visibility, suppression);
                ModelDoc2 part = RequirePart(component, out string path, out string configuration);
                double[] transform = ReadTransform(component);
                return new PipelineSingleComponentBaseline(assemblyDocument, component, part,
                    assemblyConfiguration, path, configuration, isFixed, hidden, visibility, suppression, transform);
            }
            catch (Exception ex) when (IsBaselineException(ex))
            {
                throw new InvalidOperationException("无法捕获单组件装配体基线：" + ex.Message, ex);
            }
        }

        public void Verify(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument, Component2? addedComponent)
        {
            try
            {
                RequireArguments(app, assembly, assemblyDocument);
                RequireAssemblyIdentity(app, assembly, assemblyDocument, _assemblyDocument);
                if (!string.Equals(AssemblyConfiguration(assemblyDocument), _assemblyConfiguration, StringComparison.Ordinal))
                    throw new InvalidOperationException("装配体活动配置已变化。");
                Component2 original = RequireComponents(app, assembly, addedComponent == null ? 1 : 2,
                    _component, addedComponent);
                RequireNoMates(assemblyDocument, original, addedComponent);
                int suppression = original.GetSuppression2();
                bool isFixed = original.IsFixed();
                bool hidden = original.IsHidden(false);
                int visibility = original.Visible;
                RequireComponentState(original, isFixed, visibility, suppression);
                if (isFixed != _fixed || hidden != _hidden || visibility != _visibility || suppression != _suppression)
                    throw new InvalidOperationException("原组件的固定、隐藏或压缩状态已变化。");
                ModelDoc2 part = RequirePart(original, out string path, out string configuration);
                if (!Same(app, part, _part) || !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(configuration, _configuration, StringComparison.Ordinal))
                    throw new InvalidOperationException("原组件引用的零件身份、路径或配置已变化。");
                double[] transform = ReadTransform(original);
                for (int i = 0; i < _transform.Length; i++)
                {
                    if (Math.Abs(transform[i] - _transform[i]) > TransformTolerance)
                        throw new InvalidOperationException("原组件变换已变化，索引=" + i + "。");
                }
            }
            catch (Exception ex) when (IsBaselineException(ex))
            {
                throw new InvalidOperationException("单组件装配体基线校验失败：" + ex.Message, ex);
            }
        }

        private static bool IsBaselineException(Exception ex) =>
            ex is InvalidOperationException || ex is ArgumentException || ex is IOException ||
            ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
            ex is COMException || ex is InvalidComObjectException || ex is NotSupportedException;

        private static void RequireArguments(SldWorks app, AssemblyDoc assembly, ModelDoc2 document)
        {
            if (app == null || assembly == null || document == null)
                throw new InvalidOperationException("SolidWorks 应用或装配体接口为空。");
        }

        private static bool Same(SldWorks app, object left, object right)
        {
            int result = app.IsSame(left, right);
            if (result == (int)swObjectEquality.swObjectUnsupported)
                throw new InvalidOperationException("SolidWorks 无法比较对象身份。");
            if (result != (int)swObjectEquality.swObjectSame && result != (int)swObjectEquality.swObjectNotSame)
                throw new InvalidOperationException("SolidWorks 返回未知的对象身份比较结果。");
            return result == (int)swObjectEquality.swObjectSame;
        }

        private static void RequireAssemblyIdentity(SldWorks app, AssemblyDoc assembly, ModelDoc2 document, ModelDoc2 expected)
        {
            var assemblyModel = assembly as ModelDoc2;
            if (document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY || assemblyModel == null ||
                !Same(app, assemblyModel, document) || !Same(app, document, expected))
                throw new InvalidOperationException("无法确认同一装配体文档和装配体接口。");
        }

        private static string AssemblyConfiguration(ModelDoc2 document)
        {
            string? name = document.ConfigurationManager?.ActiveConfiguration?.Name;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("无法确认装配体活动配置。");
            return name;
        }

        private static Component2 RequireComponents(SldWorks app, AssemblyDoc assembly, int count,
            Component2? expected, Component2? added)
        {
            if (assembly.HasUnloadedComponents() || assembly.GetComponentCount(false) != count ||
                assembly.GetComponentCount(true) != count)
                throw new InvalidOperationException("组件总数、根级组件数不符或存在未加载组件。");
            Array all = assembly.GetComponents(false) as Array ??
                throw new InvalidOperationException("无法枚举全部组件。");
            Array roots = assembly.GetComponents(true) as Array ??
                throw new InvalidOperationException("无法枚举根级组件。");
            if (all.Rank != 1 || roots.Rank != 1 || all.Length != count || roots.Length != count)
                throw new InvalidOperationException("组件枚举结果与预期数量不符。");
            if (added != null && (expected == null || Same(app, added, expected)))
                throw new InvalidOperationException("新增组件与原组件身份不唯一。");

            Component2? original = null;
            int originalCount = 0, addedCount = 0;
            foreach (object item in roots)
            {
                var component = item as Component2 ??
                    throw new InvalidOperationException("无法读取根级组件。");
                if (expected == null || Same(app, component, expected))
                {
                    original = component;
                    originalCount++;
                }
                else if (added != null && Same(app, component, added))
                    addedCount++;
                else
                    throw new InvalidOperationException("出现未预期的根级组件。");
            }
            if (original == null || originalCount != 1 || addedCount != (added == null ? 0 : 1))
                throw new InvalidOperationException("原组件或新增组件身份不唯一。");
            // 全部组件与根级组件逐一按 COM 身份匹配，不依赖枚举顺序。
            int allOriginal = 0, allAdded = 0;
            foreach (object item in all)
            {
                var component = item as Component2 ??
                    throw new InvalidOperationException("无法读取组件。");
                if (Same(app, component, original)) allOriginal++;
                else if (added != null && Same(app, component, added)) allAdded++;
                else throw new InvalidOperationException("出现未预期的非根级组件。");
            }
            if (allOriginal != 1 || allAdded != (added == null ? 0 : 1))
                throw new InvalidOperationException("组件身份或层级不唯一。");
            return original;
        }

        private static void RequireComponentState(Component2 component, bool isFixed, int visibility, int suppression)
        {
            if (suppression != (int)swComponentSuppressionState_e.swComponentFullyResolved ||
                !isFixed || component.IsGraphicsOnly || component.IsVirtual ||
                component.HasUnloadedComponents() || component.GetHiddenUnloadedChildrenCount() != 0)
                throw new InvalidOperationException("原组件必须固定、完全解析且非图形或虚拟组件，并且没有未加载子组件。");
            if (visibility != (int)swComponentVisibilityState_e.swComponentHidden &&
                visibility != (int)swComponentVisibilityState_e.swComponentVisible)
                throw new InvalidOperationException("无法确认原组件的隐藏状态。");
        }

        private static ModelDoc2 RequirePart(Component2 component, out string path, out string configuration)
        {
            path = component.GetPathName();
            configuration = component.ReferencedConfiguration;
            var part = component.GetModelDoc2() as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART ||
                string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
                !string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || string.IsNullOrWhiteSpace(configuration) ||
                part.GetConfigurationByName(configuration) == null || part.GetSaveFlag() ||
                !string.Equals(Path.GetFullPath(path), Path.GetFullPath(part.GetPathName()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原组件必须引用已保存且无未保存改动的零件及有效配置。");
            path = Path.GetFullPath(path);
            return part;
        }

        private static double[] ReadTransform(Component2 component)
        {
            MathTransform transform = component.Transform2 ??
                throw new InvalidOperationException("无法读取原组件变换。");
            var data = transform.ArrayData as Array;
            if (data == null || data.Rank != 1 || data.Length != 16)
                throw new InvalidOperationException("原组件变换数据不完整。");
            var values = new double[13];
            int lower = data.GetLowerBound(0);
            for (int i = 0; i < values.Length; i++)
            {
                object value = data.GetValue(lower + i);
                if (!(value is double number) || double.IsNaN(number) || double.IsInfinity(number))
                    throw new InvalidOperationException("原组件变换包含无效数值，索引=" + i + "。");
                values[i] = number;
            }
            return values;
        }

        private static void RequireNoMates(ModelDoc2 document, Component2 original, Component2? added)
        {
            RequireNoComponentMates(original);
            if (added != null) RequireNoComponentMates(added);
            // 组件 GetMates 对无配合可返回 null；独立检查 MateGroup，防止仅靠该结果遗漏配合。
            object current = document.FirstFeature();
            if (current == null) throw new InvalidOperationException("无法读取装配体特征。");
            int count = 0, groups = 0;
            while (current != null)
            {
                if (++count > 100000 || !(current is Feature feature))
                    throw new InvalidOperationException("无法可靠遍历装配体特征。");
                string type = feature.GetTypeName2();
                if (string.IsNullOrEmpty(type))
                    throw new InvalidOperationException("无法确认装配体特征类型。");
                if (string.Equals(type, "MateGroup", StringComparison.Ordinal))
                {
                    groups++;
                    if (feature.GetFirstSubFeature() != null)
                        throw new InvalidOperationException("装配体配合组包含子特征。");
                }
                current = feature.GetNextFeature();
            }
            if (groups == 0) throw new InvalidOperationException("无法确认装配体配合组。");
        }

        private static void RequireNoComponentMates(Component2 component)
        {
            object mates = component.GetMates();
            if (mates != null && (!(mates is Array array) || array.Rank != 1 || array.Length != 0))
                throw new InvalidOperationException("组件存在配合或无法确认配合状态。");
        }
    }
}
