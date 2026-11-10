using System;
using System.Runtime.Remoting.Messaging;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class AssemblyFaceContextRegression
{
    private sealed class Fixture
    {
        public string AssemblyConfiguration = "装配配置";
        public string PartConfiguration = "零件配置";
        public string ReferencedConfiguration = "零件配置";
        public string ComponentPath = @"C:\parts\valve.sldprt";
        public string PartPath = @"C:\parts\valve.sldprt";
        public string ComponentName = "阀门-1";
        public int Suppression = (int)swComponentSuppressionState_e.swComponentFullyResolved;
        public int PartType = (int)swDocumentTypes_e.swDocPART;
        public int DocumentType = (int)swDocumentTypes_e.swDocASSEMBLY;
        public bool GraphicsOnly;
        public bool Virtual;
        public bool Dirty;
        public bool Active = true;
        public bool RootEditing = true;
        public object? TransformData = Identity();
        public object? RootComponents;
        public Component2? SelectedComponent;
        public ModelDoc2? Part;
        public ModelDoc2 Document;
        public AssemblyDoc Assembly;
        public readonly SldWorks Application;
        public readonly SelectionMgr Selection;

        private static T Fake<T>(Func<IMethodCallMessage, object?> call) where T : class =>
            new InterfaceProxy<T>(call).Object;

        private static object Unexpected(IMethodCallMessage call) =>
            throw new NotSupportedException("未预期的 COM 调用：" + call.MethodName);

        public Fixture()
        {
            Part = MakePart();
            Document = MakeDocument();
            Assembly = MakeAssembly();
            SelectedComponent = MakeComponent();
            RootComponents = new object[] { SelectedComponent };
            Selection = Fake<SelectionMgr>(call =>
            {
                if (call.MethodName != "GetSelectedObjectsComponent4") return Unexpected(call);
                if ((int)call.Args[0] != 1 || (int)call.Args[1] != -1)
                    throw new NotSupportedException("必须读取第一项选择，且不限制选择标记。");
                return SelectedComponent;
            });
            Application = Fake<SldWorks>(call =>
                call.MethodName == "get_ActiveDoc" ? (Active ? Document : null) :
                call.MethodName == "IsSame" ? (ReferenceEquals(call.Args[0], call.Args[1])
                    ? (int)swObjectEquality.swObjectSame : (int)swObjectEquality.swObjectNotSame) : Unexpected(call));
        }

        public ModelDoc2 MakeDocument()
        {
            var config = Fake<Configuration>(call =>
                call.MethodName == "get_Name" ? AssemblyConfiguration : Unexpected(call));
            var manager = Fake<ConfigurationManager>(call =>
                call.MethodName == "get_ActiveConfiguration" ? config : Unexpected(call));
            return Fake<ModelDoc2>(call =>
                call.MethodName == "GetType" ? DocumentType :
                call.MethodName == "get_ConfigurationManager" ? manager : Unexpected(call));
        }

        public ModelDoc2 MakePart()
        {
            var config = Fake<Configuration>(call =>
                call.MethodName == "get_Name" ? PartConfiguration : Unexpected(call));
            var manager = Fake<ConfigurationManager>(call =>
                call.MethodName == "get_ActiveConfiguration" ? config : Unexpected(call));
            return Fake<ModelDoc2>(call =>
                call.MethodName == "GetType" ? PartType :
                call.MethodName == "GetPathName" ? PartPath :
                call.MethodName == "GetSaveFlag" ? Dirty :
                call.MethodName == "get_ConfigurationManager" ? manager : Unexpected(call));
        }

        public AssemblyDoc MakeAssembly() => Fake<AssemblyDoc>(call =>
        {
            if (call.MethodName == "GetEditTarget") return RootEditing ? Document : null;
            if (call.MethodName == "GetEditTargetComponent") return null;
            if (call.MethodName == "GetComponents")
            {
                if (!(bool)call.Args[0]) throw new NotSupportedException("必须仅枚举根级组件。");
                return RootComponents;
            }
            return Unexpected(call);
        });

        public Component2 MakeComponent()
        {
            var transform = Fake<MathTransform>(call =>
                call.MethodName == "get_ArrayData" ? TransformData : Unexpected(call));
            return Fake<Component2>(call =>
                call.MethodName == "GetSuppression2" ? Suppression :
                call.MethodName == "get_IsGraphicsOnly" ? GraphicsOnly :
                call.MethodName == "get_IsVirtual" ? Virtual :
                call.MethodName == "GetModelDoc2" ? Part :
                call.MethodName == "GetPathName" ? ComponentPath :
                call.MethodName == "get_ReferencedConfiguration" ? ReferencedConfiguration :
                call.MethodName == "get_Name2" ? ComponentName :
                call.MethodName == "get_Transform2" ? transform : Unexpected(call));
        }

        public PipelineAssemblyFaceContext Capture() =>
            PipelineAssemblyFaceContext.Capture(Application, Document, Assembly, Selection);
    }

    private static double[] Identity() =>
        new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d };

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { checks++; return; }
            throw new InvalidOperationException(message);
        }
        void Near(double actual, double expected, string message) =>
            Check(Math.Abs(actual - expected) < 1e-8, message);
        void InvalidCapture(Action<Fixture> change, string message)
        {
            var fixture = new Fixture();
            change(fixture);
            Reject(() => fixture.Capture(), message);
        }
        void ChangedSnapshot(Action<Fixture> change, string message)
        {
            var fixture = new Fixture();
            var before = fixture.Capture();
            change(fixture);
            var after = fixture.Capture();
            Reject(() => before.VerifySame(fixture.Application, after), message);
        }

        var f = new Fixture();
        var identity = f.Capture();
        Check(identity.ComponentName == "阀门-1", "组件名称应来自选中组件");
        var point = identity.TransformPoint(new PipePoint3D(23, -4, 7));
        Near(point.X, 23, "单位变换点 X 错误");
        Near(point.Y, -4, "单位变换点 Y 错误");
        Near(point.Z, 7, "单位变换点 Z 错误");
        var direction = identity.TransformDirection(new PipeDirection3D(1, 2, 3));
        Near(direction.X, 1, "单位变换方向 X 错误");
        Near(direction.Y, 2, "单位变换方向 Y 错误");
        Near(direction.Z, 3, "单位变换方向 Z 错误");
        identity.VerifySame(f.Application, f.Capture());
        checks++;
        f.RootComponents = new object[] { f.MakeComponent(), f.SelectedComponent! };
        f.Capture();
        checks++;

        // SolidWorks 的旋转矩阵按行向量排列，平移单位为米。
        f.TransformData = new[] { 0d, 1d, 0d, -1d, 0d, 0d, 0d, 0d, 1d, 0.1, 0.2, 0.3, 1d, 0d, 0d, 0d };
        var rotated = f.Capture();
        point = rotated.TransformPoint(new PipePoint3D(23, 0, 0));
        Near(point.X, 100, "旋转平移后点 X 错误");
        Near(point.Y, 223, "旋转平移后点 Y 错误");
        Near(point.Z, 300, "旋转平移后点 Z 错误");
        direction = rotated.TransformDirection(new PipeDirection3D(1, 0, 0));
        Near(direction.X, 0, "旋转方向 X 错误");
        Near(direction.Y, 1, "旋转方向 Y 错误");
        Near(direction.Z, 0, "旋转方向 Z 错误");

        InvalidCapture(x => x.Active = false, "非活动装配体必须拒绝");
        InvalidCapture(x => x.DocumentType = (int)swDocumentTypes_e.swDocPART, "非装配文档必须拒绝");
        InvalidCapture(x => x.RootEditing = false, "非根级编辑必须拒绝");
        InvalidCapture(x => x.AssemblyConfiguration = "", "装配配置为空必须拒绝");
        InvalidCapture(x => x.SelectedComponent = null, "无选中组件必须拒绝");
        InvalidCapture(x => x.RootComponents = Array.Empty<object>(), "非根级组件必须拒绝");
        InvalidCapture(x => x.RootComponents = null, "无法枚举根级组件必须拒绝");
        InvalidCapture(x => x.RootComponents = new object[] { x.MakeComponent() }, "不同根组件必须拒绝");
        InvalidCapture(x => x.RootComponents = new object[] { x.SelectedComponent!, x.SelectedComponent! }, "重复根组件必须拒绝");
        InvalidCapture(x => x.Virtual = true, "虚拟组件必须拒绝");
        InvalidCapture(x => x.GraphicsOnly = true, "仅图形组件必须拒绝");
        InvalidCapture(x => x.Suppression = (int)swComponentSuppressionState_e.swComponentLightweight, "轻化组件必须拒绝");
        InvalidCapture(x => x.Suppression = (int)swComponentSuppressionState_e.swComponentSuppressed, "压缩组件必须拒绝");
        InvalidCapture(x => x.Part = null, "无法取得零件文档必须拒绝");
        InvalidCapture(x => x.PartType = (int)swDocumentTypes_e.swDocASSEMBLY, "子装配体必须拒绝");
        InvalidCapture(x => x.Dirty = true, "零件未保存必须拒绝");
        InvalidCapture(x => x.ComponentPath = "", "组件路径为空必须拒绝");
        InvalidCapture(x => x.PartPath = "", "零件路径为空必须拒绝");
        InvalidCapture(x => x.PartPath = @"C:\parts\other.sldprt", "组件和零件路径不一致必须拒绝");
        InvalidCapture(x => x.ReferencedConfiguration = "", "引用配置为空必须拒绝");
        InvalidCapture(x => x.PartConfiguration = "", "零件活动配置为空必须拒绝");
        InvalidCapture(x => x.ReferencedConfiguration = "其他", "引用配置不匹配必须拒绝");
        InvalidCapture(x => x.TransformData = null, "缺失变换必须拒绝");
        InvalidCapture(x => x.TransformData = new double[15], "变换数组长度不足必须拒绝");
        InvalidCapture(x => x.TransformData = new double[17], "变换数组长度超出必须拒绝");
        InvalidCapture(x => x.TransformData = new object[16], "非 double 数组必须拒绝");
        InvalidCapture(x => { var m = Identity(); m[12] = 2; x.TransformData = m; }, "缩放必须拒绝");
        InvalidCapture(x => { var m = Identity(); m[0] = -1; x.TransformData = m; }, "镜像必须拒绝");
        InvalidCapture(x => { var m = Identity(); m[1] = 0.1; x.TransformData = m; }, "非正交变换必须拒绝");
        InvalidCapture(x => { var m = Identity(); m[0] = double.MaxValue; x.TransformData = m; }, "矩阵运算溢出必须拒绝");
        for (int i = 0; i < 16; i++)
        {
            int index = i;
            InvalidCapture(x => { var m = Identity(); m[index] = double.NaN; x.TransformData = m; }, "变换所有项必须有限：" + index);
        }
        InvalidCapture(x => { var m = Identity(); m[9] = double.PositiveInfinity; x.TransformData = m; }, "无限平移必须拒绝");

        ChangedSnapshot(x => x.AssemblyConfiguration = "另一装配配置", "装配配置变化必须拒绝");
        ChangedSnapshot(x => { x.ReferencedConfiguration = "另一零件配置"; x.PartConfiguration = "另一零件配置"; }, "组件配置变化必须拒绝");
        ChangedSnapshot(x => { x.SelectedComponent = x.MakeComponent(); x.RootComponents = new object[] { x.SelectedComponent! }; }, "组件身份变化必须拒绝");
        ChangedSnapshot(x => x.Part = x.MakePart(), "零件文档身份变化必须拒绝");
        ChangedSnapshot(x => { x.ComponentPath = @"C:\parts\new.sldprt"; x.PartPath = x.ComponentPath; }, "零件路径变化必须拒绝");
        ChangedSnapshot(x => x.ComponentName = "阀门-2", "组件名称变化必须拒绝");
        ChangedSnapshot(x => { var m = Identity(); m[9] = 0.01; x.TransformData = m; }, "组件平移变化必须拒绝");
        ChangedSnapshot(x => x.TransformData = new[] { 0d, 1d, 0d, -1d, 0d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d }, "组件旋转变化必须拒绝");

        // 测试 COM 返回的原数组随后被原地修改时，快照仍保持独立。
        f = new Fixture();
        var data = (double[])f.TransformData!;
        var saved = f.Capture();
        data[9] = 0.01;
        Near(saved.TransformPoint(new PipePoint3D(0, 0, 0)).X, 0, "快照不得引用可变 COM 数组");
        var moved = f.Capture();
        Reject(() => saved.VerifySame(f.Application, moved), "原地修改变换后必须拒绝");
        ChangedSnapshot(x => x.Document = x.MakeDocument(), "装配文档身份变化必须拒绝");
        return checks;
    }
}
