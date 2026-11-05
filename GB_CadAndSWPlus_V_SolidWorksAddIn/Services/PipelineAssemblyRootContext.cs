using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>确认活动文档处于装配体根级编辑状态并返回活动配置名称。</summary>
    internal static class PipelineAssemblyRootContext
    {
        public static string Require(SldWorks application, ModelDoc2 document, AssemblyDoc assembly)
        {
            object active = application.ActiveDoc;
            if (document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY ||
                active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("目标装配体不是当前活动文档。");

            var configuration = document.ConfigurationManager?.ActiveConfiguration;
            string? name = configuration?.Name;
            if (configuration == null || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("无法确认装配体活动配置。");
            object editTarget = assembly.GetEditTarget();
            if (editTarget == null || application.IsSame(editTarget, document) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("装配体不处于根级编辑状态。");

            // SW 2022 根级编辑时可返回根组件，而不是 null；不能误认为进入了组件编辑。
            Component2 editComponent = assembly.GetEditTargetComponent();
            if (editComponent != null)
            {
                Component2 root = configuration.GetRootComponent3(false);
                if (root == null || application.IsSame(editComponent, root) != (int)swObjectEquality.swObjectSame)
                    throw new InvalidOperationException("正在编辑装配体中的组件，不能作为根级拾点。");
            }
            return name;
        }
    }
}
