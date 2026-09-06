using System;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn
{
    /// <summary>
    /// 定义了一个接口，用于适配不同版本的 SolidWorks，以便在 GB 工程平台中进行版本兼容性检查。
    /// </summary>
    public interface ISolidWorksVersionAdapter
    {
        /// <summary>
        /// 获取适配器所支持的 SolidWorks 主版本号。
        /// </summary>
        int SupportedMajorVersion { get; }
        /// <summary>
        /// 确定当前 SolidWorks 版本是否可以由该适配器处理。
        /// </summary>
        /// <param name="revision">SolidWorks 版本的修订号。</param>
        /// <returns>如果适配器可以处理该版本，则返回 true；否则返回 false。</returns>
        bool CanHandle(string? revision);
    }
    /// <summary>
    /// 实现了 <see cref="ISolidWorksVersionAdapter"/> 接口的 SolidWorks 2022 版本适配器，用于检查 GB 工程平台是否可以与 SolidWorks 2022 版本兼容。
    /// </summary>
    public sealed class SolidWorks2022VersionAdapter : ISolidWorksVersionAdapter
    {
        /// <summary>
        /// 获取适配器所支持的 SolidWorks 主版本号，这里为 2022。
        /// </summary>
        public int SupportedMajorVersion => 2022;
        /// <summary>
        /// 确定当前 SolidWorks 版本是否可以由该适配器处理。适配器将检查传入的修订号是否以 "2022" 开头，以判断是否支持该版本。
        /// </summary>
        /// <param name="revision">SolidWorks 版本的修订号。</param>
        /// <returns>如果适配器可以处理该版本，则返回 true；否则返回 false。</returns>
        public bool CanHandle(string? revision)
        {
            if (string.IsNullOrWhiteSpace(revision))
            {
                return false;
            }

            return revision!.StartsWith(
                SupportedMajorVersion.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
