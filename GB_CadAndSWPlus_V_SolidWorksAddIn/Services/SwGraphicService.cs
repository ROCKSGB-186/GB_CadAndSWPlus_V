using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 端图元查询和文件下载服务。
    /// </summary>
    public sealed class SwGraphicService
    {
        private readonly SwApiClient _apiClient;

        /// <summary>创建图元服务。</summary>
        public SwGraphicService(SwApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// 查询服务器图元详情。
        /// </summary>
        public Task<SwGraphicDetailsResponse> GetDetailsAsync(int graphicId, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 第一步：调用服务器图元详情接口，获取统一属性和文件元数据。
            if (graphicId <= 0)
                throw new ArgumentOutOfRangeException(nameof(graphicId), "图元 ID 必须大于 0。");
            return _apiClient.GetJsonAsync<SwGraphicDetailsResponse>("api/graphics/" + graphicId + "/details", cancellationToken);
        }

        /// <summary>
        /// 下载服务器受控图元文件。
        /// </summary>
        public Task<byte[]> DownloadFileAsync(int graphicId, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 第二步：只通过受控 API 下载文件，禁止使用服务器返回的物理 FilePath。
            if (graphicId <= 0)
                throw new ArgumentOutOfRangeException(nameof(graphicId), "图元 ID 必须大于 0。");
            return _apiClient.GetBytesAsync("api/graphics/" + graphicId + "/file", cancellationToken);
        }
    }
}
