using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>仅捕获根级刚性零件实例的端面坐标上下文，不建立持久关联。</summary>
    internal sealed class PipelineAssemblyFaceContext
    {
        private const double RigidTolerance = 1e-8;
        private const double SnapshotTolerance = 1e-12;
        private readonly ModelDoc2 _document;
        private readonly Component2 _component;
        private readonly ModelDoc2 _part;
        private readonly string _assemblyConfiguration;
        private readonly string _partConfiguration;
        private readonly string _path;
        private readonly double[] _transform;

        private PipelineAssemblyFaceContext(ModelDoc2 document, Component2 component, ModelDoc2 part, string assemblyConfiguration,
            string partConfiguration, string path, string componentName, double[] transform)
        {
            _document = document;
            _component = component;
            _part = part;
            _assemblyConfiguration = assemblyConfiguration;
            _partConfiguration = partConfiguration;
            _path = path;
            ComponentName = componentName;
            _transform = transform;
        }

        public string ComponentName { get; }

        public static PipelineAssemblyFaceContext Capture(SldWorks app, ModelDoc2 document,
            AssemblyDoc assembly, SelectionMgr selection)
        {
            if (app == null || document == null || assembly == null || selection == null)
                throw new InvalidOperationException("装配体端面上下文不可用。");
            string assemblyConfiguration = PipelineAssemblyRootContext.Require(app, document, assembly);
            var component = selection.GetSelectedObjectsComponent4(1, -1) as Component2 ??
                throw new InvalidOperationException("无法确认所选端面所属的组件实例。");
            var roots = assembly.GetComponents(true) as Array;
            if (roots == null || roots.Rank != 1)
                throw new InvalidOperationException("无法读取装配体根级组件。");
            int matches = 0;
            foreach (object item in roots)
            {
                var root = item as Component2 ??
                    throw new InvalidOperationException("无法确认根级组件身份。");
                int same = app.IsSame(root, component);
                if (same == (int)swObjectEquality.swObjectSame) matches++;
                else if (same != (int)swObjectEquality.swObjectNotSame)
                    throw new InvalidOperationException("无法比较组件实例身份。");
            }
            if (matches != 1)
                throw new InvalidOperationException("当前试验仅支持唯一的根级零件组件，不支持嵌套组件。");
            if (component.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentFullyResolved ||
                component.IsGraphicsOnly || component.IsVirtual)
                throw new InvalidOperationException("所选组件必须完全解析，且不能是图形组件或虚拟组件。");
            var part = component.GetModelDoc2() as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART || part.GetSaveFlag())
                throw new InvalidOperationException("所选组件必须引用无未保存改动的零件文档。");
            string path = component.GetPathName();
            string configuration = component.ReferencedConfiguration;
            string name = component.Name2;
            string? activeConfiguration = part.ConfigurationManager?.ActiveConfiguration?.Name;
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, part.GetPathName(), StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(configuration) ||
                !string.Equals(configuration, activeConfiguration, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("无法确认组件零件、名称或引用配置；请先切换到一致的零件配置。");

            var transform = component.Transform2?.ArrayData as double[];
            if (transform == null || transform.Length != 16)
                throw new InvalidOperationException("无法读取完整的组件变换。");
            var copy = (double[])transform.Clone();
            foreach (double value in copy)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("组件变换包含无效数值。");
            RequireRigid(copy);
            return new PipelineAssemblyFaceContext(document, component, part, assemblyConfiguration, configuration, path, name, copy);
        }

        public void VerifySame(SldWorks app, PipelineAssemblyFaceContext other)
        {
            if (other == null || app.IsSame(_document, other._document) != (int)swObjectEquality.swObjectSame ||
                app.IsSame(_component, other._component) != (int)swObjectEquality.swObjectSame ||
                app.IsSame(_part, other._part) != (int)swObjectEquality.swObjectSame ||
                !string.Equals(_assemblyConfiguration, other._assemblyConfiguration, StringComparison.Ordinal) ||
                !string.Equals(_partConfiguration, other._partConfiguration, StringComparison.Ordinal) ||
                !string.Equals(_path, other._path, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ComponentName, other.ComponentName, StringComparison.Ordinal))
                throw new InvalidOperationException("读取或确认期间，装配体配置、组件或零件身份已变化。");
            for (int i = 0; i < 13; i++)
                if (Math.Abs(_transform[i] - other._transform[i]) > SnapshotTolerance)
                    throw new InvalidOperationException("读取或确认期间，组件位置或方向已变化。");
        }

        public PipePoint3D TransformPoint(PipePoint3D point)
        {
            // SolidWorks 的平移以米为单位；路线坐标以毫米为单位。
            return new PipePoint3D(
                point.X * _transform[0] + point.Y * _transform[3] + point.Z * _transform[6] + _transform[9] * 1000,
                point.X * _transform[1] + point.Y * _transform[4] + point.Z * _transform[7] + _transform[10] * 1000,
                point.X * _transform[2] + point.Y * _transform[5] + point.Z * _transform[8] + _transform[11] * 1000);
        }

        public PipeDirection3D TransformDirection(PipeDirection3D direction)
        {
            // 方向仅旋转，不叠加组件平移。
            return new PipeDirection3D(
                direction.X * _transform[0] + direction.Y * _transform[3] + direction.Z * _transform[6],
                direction.X * _transform[1] + direction.Y * _transform[4] + direction.Z * _transform[7],
                direction.X * _transform[2] + direction.Y * _transform[5] + direction.Z * _transform[8]);
        }

        private static void RequireRigid(double[] t)
        {
            if (Math.Abs(t[12] - 1) > RigidTolerance)
                throw new InvalidOperationException("当前试验不支持缩放组件。");
            for (int i = 0; i < 3; i++)
                for (int j = i; j < 3; j++)
                {
                    double dot = t[i * 3] * t[j * 3] + t[i * 3 + 1] * t[j * 3 + 1] +
                        t[i * 3 + 2] * t[j * 3 + 2];
                    if (double.IsNaN(dot) || double.IsInfinity(dot) ||
                        Math.Abs(dot - (i == j ? 1 : 0)) > RigidTolerance)
                        throw new InvalidOperationException("组件变换不是正交刚性旋转，不能读取端面。");
                }
            double determinant = t[0] * (t[4] * t[8] - t[5] * t[7]) -
                t[1] * (t[3] * t[8] - t[5] * t[6]) + t[2] * (t[3] * t[7] - t[4] * t[6]);
            if (double.IsNaN(determinant) || double.IsInfinity(determinant) ||
                Math.Abs(determinant - 1) > RigidTolerance)
                throw new InvalidOperationException("当前试验不支持镜像或非刚性组件变换。");
        }
    }
}
