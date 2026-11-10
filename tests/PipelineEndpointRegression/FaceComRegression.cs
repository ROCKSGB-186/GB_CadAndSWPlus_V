using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal sealed class InterfaceProxy<T> : RealProxy where T : class
{
    private readonly Func<IMethodCallMessage, object?> _call;
    public InterfaceProxy(Func<IMethodCallMessage, object?> call) : base(typeof(T)) { _call = call; }
    public T Object => (T)GetTransparentProxy();
    public override IMessage Invoke(IMessage message)
    {
        var call = (IMethodCallMessage)message;
        try { return new ReturnMessage(_call(call), null, 0, call.LogicalCallContext, call); }
        catch (Exception ex) { return new ReturnMessage(ex, call); }
    }
}

internal static class FaceComRegression
{
    internal sealed class Fixture
    {
        public int Count = 1;
        public int SelectedType = (int)swSelectType_e.swSelFACES;
        public int DocumentType = (int)swDocumentTypes_e.swDocPART;
        public bool Planar = true;
        public bool Active = true;
        public Face2? AlternateFace;
        public bool SwitchFaceDuringRead;
        private int _selectedReads;
        public string Configuration = "默认";
        public double CenterX = 0.023;
        public double OuterCenterOffset;
        public double InnerLengthFactor = 1;
        public readonly SldWorks Application;
        public readonly ModelDoc2 Document;
        public Face2 SelectedFace;

        private static T Fake<T>(Func<IMethodCallMessage, object?> call) where T : class =>
            new InterfaceProxy<T>(call).Object;

        public Fixture()
        {
            var config = Fake<Configuration>(call => call.MethodName == "get_Name" ? Configuration : Unexpected(call));
            var manager = Fake<ConfigurationManager>(call => call.MethodName == "get_ActiveConfiguration" ? config : Unexpected(call));
            var surface = Fake<Surface>(call => call.MethodName == "IsPlane" ? Planar : Unexpected(call));
            Edge MakeEdge(bool outer)
            {
                var range = Fake<CurveParamData>(call =>
                    call.MethodName == "get_UMinValue" ? 0d :
                    call.MethodName == "get_UMaxValue" ? 2 * Math.PI : Unexpected(call));
                var curve = Fake<Curve>(call =>
                    call.MethodName == "IsCircle" ? true :
                    call.MethodName == "get_CircleParams" ? new[] { CenterX + (outer ? OuterCenterOffset : 0), 0d, 0d, 1d, 0d, 0d, outer ? 0.0355 : 0.0325 } :
                    call.MethodName == "GetLength3" ? 2 * Math.PI * (outer ? 0.0355 : 0.0325) * (outer ? 1 : InnerLengthFactor) :
                    Unexpected(call));
                return Fake<Edge>(call =>
                    call.MethodName == "GetCurve" ? curve :
                    call.MethodName == "GetCurveParams3" ? range : Unexpected(call));
            }
            var innerEdge = MakeEdge(false);
            var outerEdge = MakeEdge(true);
            SelectedFace = Fake<Face2>(call =>
                call.MethodName == "GetSurface" ? surface :
                call.MethodName == "get_Normal" ? new[] { 1d, 0d, 0d } :
                call.MethodName == "GetEdges" ? new object[] { innerEdge, outerEdge } : Unexpected(call));
            var selection = Fake<SelectionMgr>(call =>
                call.MethodName == "GetSelectedObjectCount2" ? Count :
                call.MethodName == "GetSelectedObjectType3" ? SelectedType :
                call.MethodName == "GetSelectedObject6" ?
                    (SwitchFaceDuringRead && ++_selectedReads > 1 ? AlternateFace : SelectedFace) : Unexpected(call));
            Document = Fake<ModelDoc2>(call =>
                call.MethodName == "GetType" ? DocumentType :
                call.MethodName == "get_ConfigurationManager" ? manager :
                call.MethodName == "get_SelectionManager" ? selection : Unexpected(call));
            Application = Fake<SldWorks>(call =>
                call.MethodName == "get_ActiveDoc" ? (Active ? Document : null) :
                call.MethodName == "IsSame" ? (ReferenceEquals(call.Args[0], call.Args[1])
                    ? (int)swObjectEquality.swObjectSame : (int)swObjectEquality.swObjectNotSame) : Unexpected(call));
        }

        private static object Unexpected(IMethodCallMessage call) =>
            throw new InvalidOperationException("未预期的 COM 调用：" + call.MethodName);
    }

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

        var f = new Fixture();
        PipelineSelectedAnnularFace snapshot = PipelineSelectedAnnularFace.Read(f.Application, f.Document);
        Check(snapshot.Geometry.Center.X == 23 && snapshot.Geometry.InnerRadiusMm == 32.5,
            "选中端面的米毫米转换或几何读取错误");
        snapshot.Revalidate(f.Application, f.Document);
        checks++;
        f.Count = 2;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "多选必须拒绝");
        f.Count = 0;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "空选择必须拒绝");
        f.Count = 1;
        f.SelectedType = 0;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "非端面必须拒绝");
        f.SelectedType = (int)swSelectType_e.swSelFACES;
        f.DocumentType = (int)swDocumentTypes_e.swDocASSEMBLY;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "未验证的装配体必须拒绝");
        f.DocumentType = (int)swDocumentTypes_e.swDocPART;
        f.Planar = false;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "非平面必须拒绝");
        f.Planar = true;
        f.InnerLengthFactor = 0.5;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "圆弧必须拒绝");
        f.InnerLengthFactor = 1;
        f.OuterCenterOffset = 0.001;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "不同心边界必须拒绝");
        f.OuterCenterOffset = 0;
        f.Active = false;
        Reject(() => PipelineSelectedAnnularFace.Read(f.Application, f.Document), "活动文档变化必须拒绝");
        f.Active = true;
        f.CenterX = 0.024;
        Reject(() => snapshot.Revalidate(f.Application, f.Document), "确认后几何变化必须拒绝");
        f.CenterX = 0.023;
        f.Configuration = "另一配置";
        Reject(() => snapshot.Revalidate(f.Application, f.Document), "确认后配置变化必须拒绝");
        f.Configuration = "默认";
        f.SelectedFace = new Fixture().SelectedFace;
        Reject(() => snapshot.Revalidate(f.Application, f.Document), "确认后选择变化必须拒绝");
        var switching = new Fixture { SwitchFaceDuringRead = true, AlternateFace = new Fixture().SelectedFace };
        Reject(() => PipelineSelectedAnnularFace.Read(switching.Application, switching.Document), "读取期间选择变化必须拒绝");
        var negative = new Fixture { CenterX = -0.023 };
        Check(PipelineSelectedAnnularFace.Read(negative.Application, negative.Document).Geometry.Center.X == -23,
            "负侧端面坐标转换错误");
        return checks;
    }
}
