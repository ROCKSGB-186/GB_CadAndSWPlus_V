using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>失败时不自动删除文件或关闭零件；保存结果可能需要用户检查。</summary>
    internal sealed class PipelineStraightModelSaveException : Exception
    {
        internal PipelineStraightModelSaveException(string message, string path, bool reserved, bool attempted, Exception inner)
            : base(message, inner)
        {
            DestinationPath = path;
            DestinationReserved = reserved;
            SaveAttempted = attempted;
        }

        public string DestinationPath { get; }
        public bool DestinationReserved { get; }
        public bool SaveAttempted { get; }
    }

    /// <summary>在本机 NTFS 上为新零件预留不存在的路径，再尝试保存；不处理装配体或关闭文档。</summary>
    internal static class PipelineStraightModelSaver
    {
        private const uint GenericRead = 0x80000000;
        private const uint ShareReadWrite = 0x00000003;
        private const uint ShareReadOnly = 0x00000001;
        private const uint CreateNew = 1;
        private const uint OpenExisting = 3;
        private const uint NormalAttributes = 0x00000080;
        private const uint ListDirectory = 0x00000001;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint DeleteAccess = 0x00010000;
        private const uint DeleteOnClose = 0x04000000;
        private const uint VolumeNameGuid = 1;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(SafeFileHandle file, out long size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct HandleInformation
        {
            public uint Attributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        public static void Save(SldWorks application, ModelDoc2 document, IntPtr mainWindowHandle, string destinationPath)
        {
            Save(application, document, mainWindowHandle, destinationPath, "直管");
        }

        /// <summary>使用同一安全保存流程，并按零件描述报告日志和异常。</summary>
        public static void Save(SldWorks application, ModelDoc2 document, IntPtr mainWindowHandle,
            string destinationPath, string partDescription)
        {
            SaveCore(application, document, mainWindowHandle, destinationPath, partDescription, null);
        }

        /// <summary>在预留文件及目录仍受保护时执行复验，并检测文件身份和元数据变化。</summary>
        public static void Save(SldWorks application, ModelDoc2 document, IntPtr mainWindowHandle,
            string destinationPath, string partDescription, Action verifyAfterSave)
        {
            if (verifyAfterSave == null) throw new ArgumentNullException(nameof(verifyAfterSave));
            SaveCore(application, document, mainWindowHandle, destinationPath, partDescription, verifyAfterSave);
        }

        private static void SaveCore(SldWorks application, ModelDoc2 document, IntPtr mainWindowHandle,
            string destinationPath, string partDescription, Action? verifyAfterSave)
        {
            if (string.IsNullOrWhiteSpace(partDescription))
                throw new ArgumentException("必须指定零件描述。", nameof(partDescription));
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (document == null) throw new ArgumentNullException(nameof(document));
            PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
            string path = ValidatePath(destinationPath);
            bool reserved = false;
            bool attempted = false;
            string stage = "检查新零件";
            try
            {
                var frame = application.Frame() as Frame;
                if (frame == null || new IntPtr(frame.GetHWndx64()) != mainWindowHandle)
                    throw new InvalidOperationException("传入的窗口不是该 SolidWorks 实例的主窗口。");
                RequireActiveUnsavedPart(application, document);
                if (application.GetOpenDocumentByName(path) != null)
                    throw new InvalidOperationException("所选目标路径已被 SolidWorks 文档使用，请选择其他文件名。");

                stage = "保护目标目录";
                // 保持逐级目录句柄，避免保存期间父目录被移动后路径指向其他文件。
                using (DirectoryReservations directories = DirectoryReservations.Acquire(Path.GetDirectoryName(path)))
                {
                    stage = "预留新文件";
                    // 保持读取句柄并禁止删除共享，使其他操作不能替换本次预留的文件。
                    using (SafeFileHandle reservation = CreateFileW(path, GenericRead, ShareReadWrite,
                        IntPtr.Zero, CreateNew, NormalAttributes, IntPtr.Zero))
                    {
                        if (reservation.IsInvalid)
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error == 80 || error == 183)
                                throw new IOException("目标文件已存在，未覆盖；请选择新的文件名。", new Win32Exception(error));
                            throw new Win32Exception(error, "无法预留目标文件，请检查目录和权限。");
                        }
                        reserved = true;
                        SolidWorksFileLogger.Info("已预留新的" + partDescription + "零件文件，准备保存。");
                        stage = "保存新零件";
                        RequireActiveUnsavedPart(application, document);
                        ModelDocExtension extension = document.Extension ??
                            throw new InvalidOperationException("新零件的保存接口不可用。");
                        document.ClearSelection2(true);
                        RequireActiveUnsavedPart(application, document);
                        int errors = 0;
                        int warnings = 0;
                        attempted = true;
                        SolidWorksFileLogger.Info(partDescription + "诊断：开始调用 SaveAs3。");
                        bool saved = extension.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref errors, ref warnings);
                        SolidWorksFileLogger.Info(partDescription + "诊断：SaveAs3 已返回；结果=" + saved +
                            "；错误码=" + errors + "；警告码=" + warnings + "。");
                        if (!saved || errors != 0 || warnings != 0)
                            throw new InvalidOperationException("SolidWorks 保存返回未完全成功；返回=" + saved +
                                "；错误码=" + errors + "；警告码=" + warnings + "。");

                        stage = "校验保存结果";
                        RequireActivePart(application, document);
                        string actualPath = document.GetPathName();
                        if (string.IsNullOrWhiteSpace(actualPath) ||
                            !string.Equals(Path.GetFullPath(actualPath), path, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("保存后的零件路径与选择的路径不一致。");
                        if (document.GetSaveFlag())
                            throw new InvalidOperationException("保存后零件仍有未保存的更改。");
                        if (!GetFileSizeEx(reservation, out long size))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法确认已预留文件的大小。");
                        if (size <= 0 || !File.Exists(path))
                            throw new IOException("保存后目标文件不存在或为空。");
                        SolidWorksFileLogger.Info("新" + partDescription + "零件已保存，路径、文件大小及文档保存状态校验通过；零件保持打开。");
                        if (verifyAfterSave != null)
                        {
                            stage = "复验前核对文件身份、状态和内容指纹";
                            FileSnapshot before = CheckFileSnapshot(path, reservation, null);
                            byte[] beforeHash = ReadFileHash(path, reservation, before);
                            SolidWorksFileLogger.Info("已确认" + partDescription + "文件身份、状态和内容指纹，开始保存后复验；请勿并发访问文件。");
                            stage = "执行保存后复验";
                            try
                            {
                                verifyAfterSave();
                            }
                            catch (Exception callbackError) when (callbackError is PipelineElbowModelRevalidationException ||
                                callbackError is IOException || callbackError is UnauthorizedAccessException ||
                                callbackError is InvalidOperationException || callbackError is ArgumentException ||
                                callbackError is NotSupportedException || callbackError is System.Security.SecurityException ||
                                callbackError is COMException || callbackError is InvalidComObjectException ||
                                callbackError is Win32Exception || callbackError is CryptographicException)
                            {
                                // 回调失败仍检查文件；同时失败时保留两个异常。
                                stage = "复验失败后核对文件身份、状态和内容指纹";
                                try
                                {
                                    RequireUnchangedFile(path, reservation, before, beforeHash);
                                }
                                catch (Exception checkError) when (checkError is IOException ||
                                    checkError is UnauthorizedAccessException || checkError is Win32Exception ||
                                    checkError is ArgumentException || checkError is NotSupportedException ||
                                    checkError is System.Security.SecurityException || checkError is CryptographicException)
                                {
                                    SolidWorksFileLogger.Error("保存后复验失败且文件核对失败；异常类型=" +
                                        checkError.GetType().Name + "；请检查文件，未删除或回滚。");
                                    var failures = new AggregateException(callbackError, checkError);
                                    if (callbackError is PipelineElbowModelRevalidationException elbowError)
                                        throw new PipelineElbowModelRevalidationException(
                                            "复验失败且目标文件核对失败", elbowError.ObservedPath, failures);
                                    throw new IOException("复验失败且目标文件核对失败，请检查文件。", failures);
                                }
                                throw;
                            }
                            stage = "复验后核对文件身份、状态和内容指纹";
                            RequireUnchangedFile(path, reservation, before, beforeHash);
                            SolidWorksFileLogger.Info("保存后复验完成，" + partDescription + "文件身份、大小、最后写入时间及内容指纹核对通过。");
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is PipelineElbowModelRevalidationException) &&
                                       (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is InvalidOperationException || ex is ArgumentException ||
                                       ex is NotSupportedException || ex is System.Security.SecurityException ||
                                       ex is COMException || ex is InvalidComObjectException || ex is Win32Exception ||
                                       ex is CryptographicException))
            {
                SolidWorksFileLogger.Error("独立" + partDescription + "保存失败；阶段=" + stage + "；异常类型=" + ex.GetType().Name +
                    (ex is Win32Exception win32 ? "；Windows 错误码=" + win32.NativeErrorCode : string.Empty) +
                    "；已预留文件=" + reserved + "；已尝试保存=" + attempted + "。");
                string state = reserved
                    ? "目标文件可能存在且可能不完整；零件的保存状态也可能已改变。未自动删除文件、关闭零件或回滚，请检查后再操作。"
                    : "未预留目标文件；新零件仍可能打开，未自动关闭或回滚。";
                throw new PipelineStraightModelSaveException("保存" + partDescription + "失败；阶段=" + stage + "；原因=" + ex.Message +
                    "\n目标路径：" + path + "\n" + state, path, reserved, attempted, ex);
            }
        }

        private struct FileSnapshot
        {
            public uint Volume, IndexHigh, IndexLow;
            public long Size;
            public int LastWriteHigh, LastWriteLow;
        }

        private static FileSnapshot ReadFileSnapshot(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out HandleInformation info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核对目标文件身份和状态。");
            if ((info.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory)) != 0)
                throw new IOException("目标文件变成了重解析点或目录，无法安全复验。");
            if (!GetFileSizeEx(handle, out long size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核对目标文件大小。");
            ulong informationSize = ((ulong)info.FileSizeHigh << 32) | info.FileSizeLow;
            if (size <= 0 || informationSize != (ulong)size)
                throw new IOException("目标文件为空或检查期间大小发生变化。");
            return new FileSnapshot
            {
                Volume = info.VolumeSerialNumber,
                IndexHigh = info.FileIndexHigh,
                IndexLow = info.FileIndexLow,
                Size = size,
                LastWriteHigh = info.LastWriteTime.dwHighDateTime,
                LastWriteLow = info.LastWriteTime.dwLowDateTime
            };
        }

        private static bool SameFileSnapshot(FileSnapshot left, FileSnapshot right)
        {
            return left.Volume == right.Volume && left.IndexHigh == right.IndexHigh &&
                left.IndexLow == right.IndexLow && left.Size == right.Size &&
                left.LastWriteHigh == right.LastWriteHigh && left.LastWriteLow == right.LastWriteLow;
        }

        private static FileSnapshot CheckFileSnapshot(string path, SafeFileHandle reservation, FileSnapshot? expected)
        {
            // 重新按路径打开，不跟随末端重解析点，再与一直持有的预留句柄比较。
            using (SafeFileHandle current = CreateFileW(path, GenericRead, ShareReadWrite,
                IntPtr.Zero, OpenExisting, NormalAttributes | OpenReparsePoint, IntPtr.Zero))
            {
                if (current.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法重新打开目标文件核对身份；文件可能已删除、替换或不可访问。");
                FileSnapshot reserved = ReadFileSnapshot(reservation);
                FileSnapshot opened = ReadFileSnapshot(current);
                if (!SameFileSnapshot(reserved, opened))
                    throw new IOException("目标路径对应的文件身份、大小或最后写入时间与预留文件不一致。");
                if (expected.HasValue && !SameFileSnapshot(reserved, expected.Value))
                    throw new IOException("保存后复验期间目标文件身份、大小或最后写入时间发生变化。");
                return reserved;
            }
        }

        private static byte[] ReadFileHash(string path, SafeFileHandle reservation, FileSnapshot expected)
        {
            // 与 SolidWorks 的写入句柄兼容；指纹用于发现复验期间的内容变化，并非独占锁。
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha256 = SHA256.Create())
            {
                if (!SameFileSnapshot(ReadFileSnapshot(stream.SafeFileHandle), expected))
                    throw new IOException("计算内容指纹前目标文件身份或状态发生变化。");
                byte[] hash = sha256.ComputeHash(stream);
                if (!SameFileSnapshot(ReadFileSnapshot(stream.SafeFileHandle), expected))
                    throw new IOException("计算内容指纹期间目标文件身份或状态发生变化。");
                CheckFileSnapshot(path, reservation, expected);
                return hash;
            }
        }

        private static void RequireUnchangedFile(string path, SafeFileHandle reservation, FileSnapshot expected, byte[] expectedHash)
        {
            CheckFileSnapshot(path, reservation, expected);
            if (!ReadFileHash(path, reservation, expected).SequenceEqual(expectedHash))
                throw new IOException("保存后复验期间目标文件内容指纹发生变化。");
        }

        internal static string ValidatePath(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("请选择新的零件文件路径。", nameof(destinationPath));
            string root = Path.GetPathRoot(destinationPath);
            if (string.IsNullOrEmpty(root) || root.Length != 3 || root[1] != ':' || root[2] != '\\')
                throw new ArgumentException("试运行仅支持本机固定磁盘上的绝对路径。", nameof(destinationPath));
            string path = Path.GetFullPath(destinationPath);
            if (!string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("目标文件必须使用 .sldprt 扩展名。", nameof(destinationPath));
            if (Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("目标文件名包含无效字符或数据流标记。", nameof(destinationPath));
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady ||
                !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("试运行仅支持本机固定 NTFS 磁盘，不支持网络或可移动磁盘。");
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("目标目录不存在或不可访问。");
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new NotSupportedException("目标目录包含重解析点，试运行暂不支持，请选择普通本地目录。");
            return path;
        }

        private static void RequireActiveUnsavedPart(SldWorks application, ModelDoc2 document)
        {
            RequireActivePart(application, document);
            if (!string.IsNullOrEmpty(document.GetPathName()))
                throw new InvalidOperationException("当前零件已有保存路径，不能作为本次新零件保存。");
        }

        private static void RequireActivePart(SldWorks application, ModelDoc2 document)
        {
            object active = application.ActiveDoc;
            if (active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame ||
                document.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("本次新零件不再是活动文档，已停止保存。");
        }

        private sealed class DirectoryReservations : IDisposable
        {
            private readonly List<SafeFileHandle> _handles = new List<SafeFileHandle>();

            public static DirectoryReservations Acquire(string path)
            {
                var result = new DirectoryReservations();
                bool complete = false;
                try
                {
                    var directories = new List<DirectoryInfo>();
                    for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                        directories.Add(current);
                    directories.Reverse();
                    foreach (DirectoryInfo directory in directories)
                    {
                        SafeFileHandle handle = CreateFileW(directory.FullName, ListDirectory, ShareReadOnly,
                            IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                        if (handle.IsInvalid)
                        {
                            int error = Marshal.GetLastWin32Error();
                            handle.Dispose();
                            throw new Win32Exception(error, "无法保护目标目录，请检查权限或选择其他目录。");
                        }
                        result._handles.Add(handle);
                        directory.Refresh();
                        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new NotSupportedException("目标目录包含重解析点，试运行暂不支持，请选择普通本地目录。");
                    }

                    // NTFS 非空目录不能设置重解析点；哨兵锁定后先核实它确实位于目标目录。
                    string markerPath = Path.Combine(path, ".gb-pipeline-" + Guid.NewGuid().ToString("N") + ".tmp");
                    SafeFileHandle marker = CreateFileW(markerPath, GenericRead | DeleteAccess, ShareReadWrite,
                        IntPtr.Zero, CreateNew, NormalAttributes | DeleteOnClose, IntPtr.Zero);
                    if (marker.IsInvalid)
                    {
                        int error = Marshal.GetLastWin32Error();
                        marker.Dispose();
                        throw new Win32Exception(error, "无法保护目标目录，请检查权限或选择其他目录。");
                    }
                    result._handles.Add(marker);
                    for (int i = 0; i < result._handles.Count - 1; i++)
                    {
                        if (!GetFileInformationByHandle(result._handles[i], out HandleInformation information))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核实目标目录状态。");
                        if ((information.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                            throw new NotSupportedException("目标目录发生变化，已停止保存。");
                    }
                    string markerDirectory = Path.GetDirectoryName(FinalPath(marker));
                    string expectedDirectory = FinalPath(result._handles[result._handles.Count - 2]).TrimEnd('\\');
                    if (!string.Equals(markerDirectory, expectedDirectory, StringComparison.Ordinal))
                        throw new InvalidOperationException("目标目录身份发生变化，已停止保存。");
                    complete = true;
                    return result;
                }
                finally
                {
                    if (!complete) result.Dispose();
                }
            }

            public void Dispose()
            {
                for (int i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose();
                _handles.Clear();
            }

            private static string FinalPath(SafeFileHandle handle)
            {
                var buffer = new StringBuilder(512);
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, VolumeNameGuid);
                if (length == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核实目标目录身份。");
                if (length >= buffer.Capacity)
                {
                    if (length > 32767) throw new PathTooLongException("目标目录路径过长。");
                    buffer = new StringBuilder(checked((int)length + 1));
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, VolumeNameGuid);
                    if (length == 0 || length >= buffer.Capacity)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核实目标目录身份。");
                }
                return buffer.ToString();
            }
        }
    }
}
