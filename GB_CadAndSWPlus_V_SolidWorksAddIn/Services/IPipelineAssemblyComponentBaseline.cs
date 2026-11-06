using SolidWorks.Interop.sldworks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal interface IPipelineAssemblyComponentBaseline
    {
        void Verify(SldWorks app, AssemblyDoc assembly, ModelDoc2 assemblyDocument, Component2? addedComponent);
    }
}
