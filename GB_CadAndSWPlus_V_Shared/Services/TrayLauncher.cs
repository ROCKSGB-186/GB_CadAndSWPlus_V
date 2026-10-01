// 引入系统基础类型
using System;
// 引入进程相关类型，用于启动和检测托盘程序
using System.Diagnostics;
// 引入文件与目录操作类型
using System.IO;
// 引入 LINQ 查询，用于判断进程是否存在
using System.Linq;
using System.Text;

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
                {
                    WriteDiagnostic("未找到托盘程序。当前目录：" + AppDomain.CurrentDomain.BaseDirectory);
                    return false;
                }

                // 启动托盘程序，并设置工作目录为托盘程序所在目录
                Process.Start(new ProcessStartInfo
                {
                    FileName = trayPath,
                    WorkingDirectory = Path.GetDirectoryName(trayPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true
                });
                WriteDiagnostic("已启动托盘程序：" + trayPath);
                return true;
            }
            catch (Exception ex)
            {
                // 启动过程中发生任何异常，均视为启动失败
                WriteDiagnostic("启动托盘程序失败：" + ex);
                return false;
            }
        }

        /// <summary>
        /// 判断托盘程序当前是否运行，作为 CAD/SolidWorks 自动登录的有效条件。
        /// </summary>
        public static bool IsRunning()
        {
            try { return Process.GetProcessesByName(TrayProcessName).Any(); }
            catch { return false; }
        }

        /// <summary>
        /// 判断 CAD 或 SolidWorks 客户端是否仍在运行。
        /// </summary>
        public static bool IsCadOrSolidWorksRunning()
        {
            try
            {
                return Process.GetProcessesByName("acad").Any()
                    || Process.GetProcessesByName("acadlt").Any()
                    || Process.GetProcessesByName("sldworks").Any();
            }
            catch
            {
                // 无法读取进程列表时采取保守策略，禁止托盘退出。
                return true;
            }
        }

        /// <summary>
        /// 查找托盘程序的路径，优先从当前应用程序目录向上查找，如果未找到，则尝试从本地应用数据目录中查找。
        /// </summary>
        /// <returns>托盘程序的路径，如果未找到则返回空字符串。</returns>
        private static string FindTrayPath()
        {
            // AutoCAD/SolidWorks 会把当前目录设为宿主程序目录，不能使用它定位插件文件。
            // 共享 DLL 的 Location 才是当前实际加载的 CAD/SolidWorks 输出目录。
            string assemblyDirectory = Path.GetDirectoryName(typeof(TrayLauncher).Assembly.Location);
            string current = string.IsNullOrWhiteSpace(assemblyDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : assemblyDirectory;
            WriteDiagnostic("托盘查找起点：程序集目录=" + current + "，进程目录=" + AppDomain.CurrentDomain.BaseDirectory);

            // 逐级检查当前程序集目录及其父目录，避免目录到达根目录后继续使用 null。
            DirectoryInfo directory = new DirectoryInfo(current);
            for (int i = 0; i < 12 && directory != null; i++, directory = directory.Parent)
            {
                string[] candidates =
                {
                    Path.Combine(directory.FullName, TrayFileName),
                    Path.Combine(directory.FullName, "Tray", TrayFileName),
                    Path.Combine(directory.FullName, "GB_CadAndSWPlus_V_Tray", TrayFileName),
                    Path.Combine(directory.FullName, "GB_CadAndSWPlus_V_Tray", "bin", "Debug", "net48", TrayFileName),
                    Path.Combine(directory.FullName, "GB_CadAndSWPlus_V_Tray", "bin", "Release", "net48", TrayFileName)
                };

                foreach (string candidate in candidates)
                {
                    WriteDiagnostic("检查托盘路径：" + candidate);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // 未在应用程序目录附近找到，则尝试从本地应用数据目录查找
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 安装目录下的托盘程序路径：%LocalAppData%\GB_CADPLUS\Tray\GB_CadAndSWPlus_V_Tray.exe
            string installedCandidate = Path.Combine(localAppData, "GB_CADPLUS", "Tray", TrayFileName);

            // 存在则返回该路径，否则返回空字符串
            return File.Exists(installedCandidate) ? installedCandidate : string.Empty;
        }

        private static void WriteDiagnostic(string message)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GB_CADPLUS", "Logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "tray-launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // 诊断日志失败不能影响登录流程。
            }
        }
    }
}