using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>保守记录拾点期间的装配体根级上下文；无法确认时只禁用插入，不影响已有拾点功能。</summary>
    internal sealed class PipelineAssemblyRouteGuard
    {
        private readonly SldWorks _application;
        private readonly ModelDoc2 _document;
        private string? _configuration;

        public PipelineAssemblyRouteGuard(SldWorks application, ModelDoc2 document)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public bool IsValid { get; private set; } = true;
        public string? Configuration => _configuration;

        public void Observe()
        {
            if (!IsValid) return;
            try
            {
                var assembly = _document as AssemblyDoc;
                if (assembly == null) throw new InvalidOperationException("当前文档不是装配体。");
                string configuration = PipelineAssemblyRootContext.Require(_application, _document, assembly);
                if (_configuration != null && !string.Equals(_configuration, configuration, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("拾点期间装配体配置发生变化。");
                _configuration = configuration;
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidComObjectException ||
                                       ex is InvalidOperationException || ex is ArgumentException)
            {
                Invalidate();
                SolidWorksFileLogger.Warning("检查装配体拾点上下文失败；异常类型=" + ex.GetType().Name + "；原因=" + ex.Message);
            }
        }

        private void Invalidate()
        {
            IsValid = false;
            SolidWorksFileLogger.Warning("无法确认拾点始终位于同一装配体根级及配置，禁止本次路线插入装配体。");
        }
    }
}
