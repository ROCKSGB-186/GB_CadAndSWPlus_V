using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

        /// <summary>记录 SolidWorks 操作开始，统一使用中文日志。</summary>
        public static void Start(string operation)
        {
            Info("操作开始：" + (operation ?? "未命名操作") + "。");
        }

        /// <summary>记录 SolidWorks 操作完成，统一使用中文日志。</summary>
        public static void Complete(string operation)
        {
            Info("操作完成：" + (operation ?? "未命名操作") + "。");
        }

        /// <summary>记录 SolidWorks 操作被跳过及跳过原因。</summary>
        public static void Skipped(string operation, string reason)
        {
            Info("操作跳过：" + (operation ?? "未命名操作") + "；原因=" + (reason ?? "未说明") + "。");
        }

        /// <summary>记录 SolidWorks 操作异常；调用方不得将密码、令牌或连接字符串传入。</summary>
        public static void Failed(string operation, Exception exception)
        {
            Error("操作异常：" + (operation ?? "未命名操作") + "；异常=" + (exception?.Message ?? "未知异常") + "。");
        }

        /// <summary>获取当前 SolidWorks 日志文件路径，供日志页面打开和读取。</summary>
        public static string GetCurrentLogFilePath()
        {
            return Path.Combine(
                LogDirectory,
                "SolidWorks_" + DateTime.Now.ToString("yyyyMMdd_HH") + ".log");
        }

        /// <summary>获取 SolidWorks 日志目录路径，供日志页面浏览全部日志。</summary>
        public static string GetLogDirectoryPath()
        {
            return LogDirectory;
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
                         Sanitize(message));
                    File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 日志失败不能阻断登录或 SolidWorks 主流程，只输出到调试窗口。
                System.Diagnostics.Debug.WriteLine("SolidWorks 日志写入失败：" + ex.Message);
            }
        }

        private static string Sanitize(string message)
        {
            string value = message ?? string.Empty;
            value = Regex.Replace(value, @"(?i)(bearer\s+)[^\s;。]+", "$1***");
            value = Regex.Replace(value, @"(?i)((?:password|pwd|token|secret|authorization|access_token)\s*[=:：]\s*)[^;，。\s]+", "$1***");
            value = Regex.Replace(value, @"(?i)(连接字符串\s*[=:：]\s*)[^；。]+", "$1***");
            return value;
        }
    }
}
