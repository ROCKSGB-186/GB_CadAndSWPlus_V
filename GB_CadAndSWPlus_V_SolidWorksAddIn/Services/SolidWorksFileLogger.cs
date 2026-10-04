using System;
using System.IO;
using System.Text;
using System.Threading;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 客户端文件日志，路径与 CAD 客户端一致，但使用 SOLIDWORKS 子目录隔离。
    /// </summary>
    internal static class SolidWorksFileLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GB_CADPLUS",
            "Logs",
            "SOLIDWORKS");

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warning(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string filePath = Path.Combine(
                        LogDirectory,
                        "SolidWorks_" + DateTime.Now.ToString("yyyyMMdd_HH") + ".log");
                    string line = string.Format(
                        "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [SOLIDWORKS] {2}",
                        DateTime.Now,
                        level,
                        message ?? string.Empty);
                    File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 日志失败不能阻断登录或 SolidWorks 主流程，只输出到调试窗口。
                System.Diagnostics.Debug.WriteLine("SolidWorks 日志写入失败：" + ex.Message);
            }
        }
    }
}
