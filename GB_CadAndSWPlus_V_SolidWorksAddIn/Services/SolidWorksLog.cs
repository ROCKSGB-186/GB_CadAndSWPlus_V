using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 插件独立日志，避免依赖 CAD 主项目，记录到用户本地日志目录。
    /// </summary>
    internal static class SolidWorksLog
    {
        private static readonly object SyncRoot = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Warning(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GB_CADPLUS", "Logs", "SolidWorks");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"solidworks_{DateTime.Now:yyyyMMddHH}.log");
                lock (SyncRoot)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SolidWorks 日志写入失败：{ex.Message}；原始日志：{line}");
            }
        }
    }
}
