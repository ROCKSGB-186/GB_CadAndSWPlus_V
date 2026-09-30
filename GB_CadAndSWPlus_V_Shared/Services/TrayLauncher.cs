// 引入系统基础类型
using System;
// 引入进程相关类型，用于启动和检测托盘程序
using System.Diagnostics;
// 引入文件与目录操作类型
using System.IO;
// 引入 LINQ 查询，用于判断进程是否存在
using System.Linq;

namespace GB_CadAndSWPlus_V.Shared.Services
{
    /// <summary>
    /// 启动独立托盘程序，并避免重复启动多个托盘实例。
    /// </summary>
    public static class TrayLauncher
    {
        // 托盘程序的进程名（不带 .exe）
        private const string TrayProcessName = "GB_CadAndSWPlus_V_Tray";

        // 托盘程序的可执行文件名
        private const string TrayFileName = "GB_CadAndSWPlus_V_Tray.exe";

        /// <summary>
        /// 确保托盘程序已启动；如果已经存在托盘进程，则直接返回 true。
        /// </summary>
        /// <returns>启动成功或托盘已存在时返回 true，否则返回 false。</returns>
        public static bool EnsureStarted()
        {
            try
            {
                // 检查是否已有同名的托盘进程在运行
                if (Process.GetProcessesByName(TrayProcessName).Any())
                    return true;

                // 查找托盘程序的可执行文件路径
                string trayPath = FindTrayPath();

                // 路径为空或文件不存在，则无法启动
                if (string.IsNullOrWhiteSpace(trayPath) || !File.Exists(trayPath))
                    return false;

                // 启动托盘程序，并设置工作目录为托盘程序所在目录
                Process.Start(new ProcessStartInfo
                {
                    FileName = trayPath,
                    WorkingDirectory = Path.GetDirectoryName(trayPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                // 启动过程中发生任何异常，均视为启动失败
                return false;
            }
        }

        /// <summary>
        /// 查找托盘程序的路径，优先从当前应用程序目录向上查找，如果未找到，则尝试从本地应用数据目录中查找。
        /// </summary>
        /// <returns>托盘程序的路径，如果未找到则返回空字符串。</returns>
        private static string FindTrayPath()
        {
            // 从当前应用程序基目录开始查找
            string current = AppDomain.CurrentDomain.BaseDirectory;

            // 最多向上查找 5 层目录，寻找托盘程序文件
            for (int i = 0; i < 5 && !string.IsNullOrWhiteSpace(current); i++)
            {
                // 拼接候选路径
                string candidate = Path.Combine(current, TrayFileName);

                // 如果找到则直接返回
                if (File.Exists(candidate)) return candidate;

                // 继续向上一级目录查找
                current = Directory.GetParent(current)?.FullName;
            }

            // 未在应用程序目录附近找到，则尝试从本地应用数据目录查找
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 安装目录下的托盘程序路径：%LocalAppData%\GB_CADPLUS\Tray\GB_CadAndSWPlus_V_Tray.exe
            string installedCandidate = Path.Combine(localAppData, "GB_CADPLUS", "Tray", TrayFileName);

            // 存在则返回该路径，否则返回空字符串
            return File.Exists(installedCandidate) ? installedCandidate : string.Empty;
        }
    }
}