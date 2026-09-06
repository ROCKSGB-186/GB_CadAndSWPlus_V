using System;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn
{
    /// <summary>
    /// 加入状态命令，用于检查 GB 工程平台在 SolidWorks 中的加载状态以及适配器配置情况。
    /// </summary>
    public sealed class AddInStatusCommand
    {
        /// <summary>
        /// 存储所有可用的 SolidWorks 版本适配器，用于检查当前 SolidWorks 版本是否有对应的适配器。
        /// </summary>
        private readonly ISolidWorksVersionAdapter[] _versionAdapters;
        /// <summary>
        /// 初始化一个新的 <see cref="AddInStatusCommand"/> 实例，并使用默认的 SolidWorks 版本适配器。
        /// </summary>
        public AddInStatusCommand()
            : this(new ISolidWorksVersionAdapter[]
            {
                new SolidWorks2022VersionAdapter()
            })
        {
        }
        /// <summary>
        /// 初始化一个新的 <see cref="AddInStatusCommand"/> 实例，并使用指定的 SolidWorks 版本适配器。
        /// </summary>
        /// <param name="versionAdapters">要使用的 SolidWorks 版本适配器数组。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="versionAdapters"/> 为 null 时抛出。</exception>
        public AddInStatusCommand(ISolidWorksVersionAdapter[] versionAdapters)
        {
            _versionAdapters = versionAdapters ?? throw new ArgumentNullException(nameof(versionAdapters));
        }
        /// <summary>
        /// 获取 GB 工程平台在 SolidWorks 中的加载状态，并检查当前 SolidWorks 版本是否有对应的适配器。
        /// </summary>
        /// <param name="solidWorksRevision">当前 SolidWorks 的版本号。</param>
        /// <returns>返回 GB 工程平台的加载状态描述。</returns>
        public string GetStatus(string? solidWorksRevision)
        {
            foreach (ISolidWorksVersionAdapter adapter in _versionAdapters)
            {
                if (adapter.CanHandle(solidWorksRevision))
                {
                    return $"GB 工程平台已加载，已配置 SolidWorks {adapter.SupportedMajorVersion} 适配器。";
                }
            }

            return "GB 工程平台已加载，但尚未配置当前 SolidWorks 版本的专用适配器。";
        }
    }
}
