using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 实例注册客户端。
    /// </summary>
    public sealed class SwInstanceClientService
    {
        private readonly SwApiClient _apiClient;

        /// <summary>创建实例注册客户端。</summary>
        public SwInstanceClientService(SwApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// 向服务器注册或更新一个 SolidWorks 装配体实例。
        /// </summary>
        public Task<SwInstanceResponse> RegisterAsync(
            SwInstanceRegisterRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // 第一步：校验客户端生成的稳定实例标识。
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SwInstanceId))
                throw new ArgumentException("SwInstanceId 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.PidInstanceId))
                throw new ArgumentException("PidInstanceId 不能为空。", nameof(request));

            // 第二步：通过统一服务器接口提交实例，数据库版本检查由服务器负责。
            return _apiClient.PostJsonAsync<SwInstanceRegisterRequest, SwInstanceResponse>(
                "api/sw-instances/register", request, cancellationToken);
        }
    }
}
