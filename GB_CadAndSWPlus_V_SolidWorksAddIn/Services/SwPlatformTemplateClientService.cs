using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 平台模板 API 客户端。
    /// </summary>
    public sealed class SwPlatformTemplateClientService
    {
        private readonly SwApiClient _apiClient;

        /// <summary>
        /// 创建平台模板 API 客户端。
        /// </summary>
        public SwPlatformTemplateClientService(SwApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// 从服务器查询指定平台的模板下载路径。
        /// </summary>
        public async Task<SwPlatformTemplateResponse?> GetAsync(
            string platformCode,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // 第一步：校验平台代码，避免发送无效请求。
            if (string.IsNullOrWhiteSpace(platformCode))
                throw new ArgumentException("平台代码不能为空。", nameof(platformCode));

            // 第二步：调用服务器统一模板接口；服务器异常由上层捕获并触发离线回退。
            return await _apiClient.GetJsonAsync<SwPlatformTemplateResponse>(
                "api/sw-platform-templates/" + Uri.EscapeDataString(platformCode.Trim()),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
