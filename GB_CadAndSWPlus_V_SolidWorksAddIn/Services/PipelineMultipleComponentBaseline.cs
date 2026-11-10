using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>记录两个已有零件的只读试验基线；校验通过不表示具有事务或回滚能力。</summary>
    internal sealed class PipelineMultipleComponentBaseline : IPipelineAssemblyComponentBaseline
    {
        private const double TransformTolerance = 1e-9;
        private readonly ModelDoc2 _assemblyDocument;
        private readonly string _assemblyConfiguration;
        private readonly ComponentSnapshot[] _originals;

        private sealed class ComponentSnapshot
        {
            public readonly Component2 Component;
            public readonly ModelDoc2 Part;
            public readonly string Path;
            public readonly string Configuration;
            public readonly bool Fixed;
            public readonly bool Hidden;
            public readonly int Visibility;
            public readonly int Suppression;
            public readonly double[] Transform;

            public ComponentSnapshot(Component2 component)
            {
                Component = component;
                Suppression = component.GetSuppression2();
                Fixed = component.IsFixed();
                Hidden = component.IsHidden(false);
                Visibility = component.Visible;
                RequireComponentState(component, Fixed, Visibility, Suppression);
                Part = RequirePart(component, out string path, out string configuration);
                Path = path;
                Configuration = configuration;
                Transform = ReadTransform(component);
            }

            public void Verify(SldWorks app, Component2 component)
            {
                int suppression = component.GetSuppression2();
                bool isFixed = component.IsFixed();
                bool hidden = component.IsHidden(false);
                int visibility = component.Visible;
                RequireComponentState(component, isFixed, visibility, suppression);
                if (isFixed != Fixed || hidden != Hidden || visibility != Visibility || suppression != Suppression)
                    throw new InvalidOperationException("原组件的固定、隐藏或压缩状态已变化。");
                ModelDoc2 part = RequirePart(component, out string path, out string configuration);
                if (!Same(app, part, Part) || !string.Equals(path, Path, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(configuration, Configuration, StringComparison.Ordinal))
                    throw new InvalidOperationException("原组件引用的零件身份、路径或配置已变化。");
                double[] transform = ReadTransform(component);
                for (int i = 0; i < Transform.Length; i++)
                {
                    if (Math.Abs(transform[i] - Transform[i]) > TransformTolerance)
                        throw new InvalidOperationException("原组件变换已变化，索引=" + i + "。");
                }
            }
        }

        private PipelineMultipleComponentBaseline(ModelDoc2 document, string configuration, ComponentSnapshot[] originals)
        {
            _assemblyDocument = document;
            _assemblyConfiguration = configuration;
            _originals = originals;
        }

        public static PipelineMultipleComponentBaseline Capture(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument)
        {
            try
            {
                RequireArguments(app, assembly, assemblyDocument);
                RequireAssemblyIdentity(app, assembly, assemblyDocument, assemblyDocument);
                string configuration = AssemblyConfiguration(assemblyDocument);
                Component2[] roots = ReadComponents(assembly, 2, out Array all);
                RequireDistinct(app, roots);
                MatchComponents(app, all, roots);
                RequireNoMates(assemblyDocument, roots);
                return new PipelineMultipleComponentBaseline(assemblyDocument, configuration,
                    new[] { new ComponentSnapshot(roots[0]), new ComponentSnapshot(roots[1]) });
            }
            catch (Exception ex) when (IsBaselineException(ex))
            {
                throw new InvalidOperationException("无法捕获多组件装配体基线：" + ex.Message, ex);
            }
        }

        public void Verify(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument, Component2? addedComponent)
        {
            VerifyAddedComponents(app, assembly, assemblyDocument,
                addedComponent == null ? Array.Empty<Component2>() : new[] { addedComponent });
        }

        internal void VerifyAddedComponents(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument,
            IReadOnlyList<Component2> addedComponents)
        {
            try
            {
                RequireArguments(app, assembly, assemblyDocument);
                RequireAssemblyIdentity(app, assembly, assemblyDocument, _assemblyDocument);
                if (!string.Equals(AssemblyConfiguration(assemblyDocument), _assemblyConfiguration, StringComparison.Ordinal))
                    throw new InvalidOperationException("装配体活动配置已变化。");
                if (addedComponents == null) throw new ArgumentNullException(nameof(addedComponents));
                var expected = new Component2[_originals.Length + addedComponents.Count];
                for (int i = 0; i < _originals.Length; i++)
                    expected[i] = _originals[i].Component;
                for (int i = 0; i < addedComponents.Count; i++)
                    expected[_originals.Length + i] = addedComponents[i] ??
                        throw new InvalidOperationException("新增组件身份不可用。");
                RequireDistinct(app, expected);
                Component2[] roots = ReadComponents(assembly, expected.Length, out Array all);
                // 两个枚举分别与基线身份做一对一匹配，不依赖顺序或零件文件路径。
                Component2[] matched = MatchComponents(app, roots, expected);
                MatchComponents(app, all, expected);
                for (int i = _originals.Length; i < matched.Length; i++)
                    RequireAddedPart(matched[i]);
                RequireNoMates(assemblyDocument, matched);
                for (int i = 0; i < _originals.Length; i++)
                    _originals[i].Verify(app, matched[i]);
            }
            catch (Exception ex) when (IsBaselineException(ex))
            {
                throw new InvalidOperationException("多组件装配体基线校验失败：" + ex.Message, ex);
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
            return name!;
        }

        private static Component2[] ReadComponents(AssemblyDoc assembly, int count, out Array all)
        {
            if (assembly.HasUnloadedComponents() || assembly.GetComponentCount(false) != count ||
                assembly.GetComponentCount(true) != count)
                throw new InvalidOperationException("组件总数、根级组件数不符或存在未加载组件。");
            all = assembly.GetComponents(false) as Array ??
                throw new InvalidOperationException("无法枚举全部组件。");
            Array roots = assembly.GetComponents(true) as Array ??
                throw new InvalidOperationException("无法枚举根级组件。");
            if (all.Rank != 1 || roots.Rank != 1 || all.Length != count || roots.Length != count)
                throw new InvalidOperationException("组件枚举结果与预期数量不符。");
            var result = new Component2[count];
            int i = 0;
            foreach (object item in roots)
                result[i++] = item as Component2 ?? throw new InvalidOperationException("无法读取根级组件。");
            return result;
        }

        private static void RequireDistinct(SldWorks app, Component2[] components)
        {
            for (int i = 0; i < components.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Same(app, components[i], components[j]))
                        throw new InvalidOperationException("组件身份不唯一。");
        }

        private static Component2[] MatchComponents(SldWorks app, Array actual, Component2[] expected)
        {
            var result = new Component2[expected.Length];
            foreach (object item in actual)
            {
                var component = item as Component2 ?? throw new InvalidOperationException("无法读取组件。");
                int match = -1;
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!Same(app, component, expected[i])) continue;
                    if (match != -1) throw new InvalidOperationException("组件身份不唯一。");
                    match = i;
                }
                if (match < 0) throw new InvalidOperationException("出现未预期的组件或非根级组件。");
                if (result[match] != null) throw new InvalidOperationException("组件身份重复或枚举遗漏。");
                result[match] = component;
            }
            for (int i = 0; i < result.Length; i++)
                if (result[i] == null) throw new InvalidOperationException("组件枚举遗漏。");
            return result;
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

        private static void RequireAddedPart(Component2 component)
        {
            // 新增组件不要求固定或干净，但必须可解析为零件且不能含未加载组件。
            var part = component.GetModelDoc2() as ModelDoc2;
            if (component.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentFullyResolved ||
                component.IsGraphicsOnly || component.HasUnloadedComponents() ||
                component.GetHiddenUnloadedChildrenCount() != 0 || part == null ||
                part.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("新增组件必须是完全解析且无未加载子组件的零件。");
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
            // SolidWorks 的前 13 项为旋转、平移和比例；末尾三项未使用。
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

        private static void RequireNoMates(ModelDoc2 document, Component2[] components)
        {
            foreach (Component2 component in components)
            {
                object mates = component.GetMates();
                if (mates != null && (!(mates is Array array) || array.Rank != 1 || array.Length != 0))
                    throw new InvalidOperationException("组件存在配合或无法确认配合状态。");
            }
            // GetMates 对无配合可能返回 null，另行检查整个装配体配合组。
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
    }
}
