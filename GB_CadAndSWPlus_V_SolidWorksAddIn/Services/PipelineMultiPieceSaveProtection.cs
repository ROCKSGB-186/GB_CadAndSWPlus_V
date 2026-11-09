using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    // 此保护仅用于多段试验，不改变两点、三点的 saver。
    internal static class PipelineMultiPieceSaveProtection
    {
        private const uint GenericRead = 0x80000000, DeleteAccess = 0x00010000;
        private const uint ListDirectory = 1, ShareRead = 1, ShareReadWrite = 3;
        private const uint CreateNew = 1, OpenExisting = 3, Normal = 0x80;
        private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, DeleteOnClose = 0x04000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information information);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct Information
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        private static Information Read(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out Information info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核对文件或目录身份。");
            return info;
        }

        private static bool SameIdentity(Information a, Information b)
        {
            return a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
        }

        private static bool SameFile(Information a, Information b)
        {
            return SameIdentity(a, b) && a.Attributes == b.Attributes && a.Links == b.Links &&
                a.SizeHigh == b.SizeHigh && a.SizeLow == b.SizeLow &&
                a.Write.dwHighDateTime == b.Write.dwHighDateTime && a.Write.dwLowDateTime == b.Write.dwLowDateTime;
        }

        private static SafeFileHandle Open(string path, uint access, uint share, uint creation, uint flags)
        {
            SafeFileHandle handle = CreateFileW(path, access, share, IntPtr.Zero, creation, flags, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "无法安全打开或保护文件、目录：" + path);
            }
            return handle;
        }

        private static string FinalPath(SafeFileHandle handle)
        {
            var buffer = new StringBuilder(512);
            uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 1);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取最终路径。");
            if (length >= buffer.Capacity)
            {
                if (length > 32767) throw new PathTooLongException("路径过长。");
                buffer = new StringBuilder(checked((int)length + 1));
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 1);
                if (length == 0 || length >= buffer.Capacity)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取最终路径。");
            }
            return buffer.ToString();
        }

        internal sealed class DirectoryGuard : IDisposable
        {
            private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            private readonly List<string> paths = new List<string>();
            private readonly List<Information> identities = new List<Information>();
            private string markerPath = "";
            private SafeFileHandle? marker;
            private bool disposed;

            internal static DirectoryGuard Acquire(string folder)
            {
                var result = new DirectoryGuard();
                try
                {
                    var chain = new List<string>();
                    for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
                        chain.Add(dir.FullName);
                    chain.Reverse();
                    foreach (string path in chain)
                    {
                        SafeFileHandle handle = Open(path, ListDirectory, ShareRead, OpenExisting,
                            BackupSemantics | OpenReparsePoint);
                        result.handles.Add(handle);
                        result.paths.Add(path);
                        Information info = Read(handle);
                        if ((info.Attributes & (uint)FileAttributes.Directory) == 0 ||
                            (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                            throw new IOException("目录链包含非目录或重解析点：" + path);
                        result.identities.Add(info);
                    }
                    // 空目录可能被转换为重解析点；独占新建本次哨兵使目标目录保持非空。
                    result.markerPath = Path.Combine(folder, ".gb-multi-" + Guid.NewGuid().ToString("N") + ".tmp");
                    result.marker = Open(result.markerPath, GenericRead | DeleteAccess, ShareReadWrite,
                        CreateNew, Normal | DeleteOnClose);
                    result.CheckIdentity();
                    string actualParent = Path.GetDirectoryName(FinalPath(result.marker)) ?? "";
                    string expectedParent = FinalPath(result.handles[result.handles.Count - 1]).TrimEnd('\\');
                    if (!string.Equals(actualParent, expectedParent, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("保护哨兵不在确认的目录中。");
                    return result;
                }
                catch { result.Dispose(); throw; }
            }

            internal void CheckIdentity()
            {
                if (disposed || marker == null) throw new ObjectDisposedException(nameof(DirectoryGuard));
                for (int i = 0; i < paths.Count; i++)
                {
                    Information held = Read(handles[i]);
                    using (SafeFileHandle current = Open(paths[i], ListDirectory, ShareRead, OpenExisting,
                        BackupSemantics | OpenReparsePoint))
                    {
                        Information byPath = Read(current);
                        if ((held.Attributes & (uint)FileAttributes.Directory) == 0 ||
                            (byPath.Attributes & (uint)FileAttributes.Directory) == 0 ||
                            (held.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                            (byPath.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                            !SameIdentity(held, identities[i]) || !SameIdentity(held, byPath))
                            throw new IOException("确认的目录身份或类型已改变：" + paths[i]);
                    }
                }
                using (SafeFileHandle current = Open(markerPath, GenericRead, ShareReadWrite | 4, OpenExisting, Normal | OpenReparsePoint))
                {
                    Information held = Read(marker);
                    Information byPath = Read(current);
                    if ((byPath.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory)) != 0 ||
                        !SameIdentity(held, byPath))
                        throw new IOException("目录保护哨兵身份已改变。");
                }
            }

            internal void CheckEntries(IEnumerable<string> expectedFiles)
            {
                CheckIdentity();
                var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in expectedFiles)
                {
                    if (!string.Equals(Path.GetDirectoryName(path)?.TrimEnd('\\'),
                        paths[paths.Count - 1].TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                        !expected.Add(Path.GetFileName(path)))
                        throw new IOException("预期文件不属于确认的目录或名称重复。");
                }
                string markerName = Path.GetFileName(markerPath);
                var locks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in expected)
                    if (!locks.Add("~$" + name) || expected.Contains("~$" + name) ||
                        string.Equals("~$" + name, markerName, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("锁文件名称与确认目标或保护哨兵冲突。");
                var seenLocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var heldLocks = new List<KeyValuePair<string, SafeFileHandle>>();
                try
                {
                    bool sawMarker = false;
                    foreach (string entry in Directory.EnumerateFileSystemEntries(paths[paths.Count - 1]))
                    {
                        string name = Path.GetFileName(entry);
                        if (string.Equals(name, markerName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (sawMarker) throw new IOException("目录保护哨兵重复。");
                            sawMarker = true;
                        }
                        else if (expected.Remove(name)) { }
                        else if (locks.Contains(name) && seenLocks.Add(name))
                        {
                            // 只检查元数据，不读取、写入或删除锁文件；名称并不能证明其所有权。
                            SafeFileHandle held = Open(entry, 0, ShareReadWrite | 4, OpenExisting,
                                BackupSemantics | OpenReparsePoint);
                            heldLocks.Add(new KeyValuePair<string, SafeFileHandle>(entry, held));
                            ValidateLock(Read(held));
                        }
                        else
                            throw new IOException("专用目录出现非预期成员：" + entry);
                    }
                    if (!sawMarker || expected.Count != 0)
                        throw new IOException("目录保护哨兵或已保存文件缺失。");
                    foreach (var item in heldLocks)
                    {
                        Information held = Read(item.Value);
                        ValidateLock(held);
                        using (SafeFileHandle current = Open(item.Key, 0, ShareReadWrite | 4, OpenExisting,
                            BackupSemantics | OpenReparsePoint))
                        {
                            Information byPath = Read(current);
                            ValidateLock(byPath);
                            if (!SameIdentity(held, byPath))
                                throw new IOException("锁文件检查期间身份已改变：" + item.Key);
                        }
                    }
                    CheckIdentity();
                }
                finally
                {
                    foreach (var item in heldLocks) item.Value.Dispose();
                }
            }

            private static void ValidateLock(Information info)
            {
                if ((info.Attributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0 ||
                    info.Links != 1)
                    throw new IOException("锁文件不是普通文件或存在硬链接。");
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                // 只删除本次独占创建的临时哨兵；任何用户文件均不删除。
                marker?.Dispose();
                for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
            }
        }

        internal sealed class FileGuard : IDisposable
        {
            private readonly string path;
            private readonly SafeFileHandle held;
            private readonly Information baseline;
            private readonly byte[] hash;

            private FileGuard(string path, SafeFileHandle held, Information baseline, byte[] hash)
            { this.path = path; this.held = held; this.baseline = baseline; this.hash = hash; }

            // saver 回调期间获取保护句柄；其自己的保留句柄及复验前后指纹仍同时生效。
            internal static FileGuard Capture(string path)
            {
                SafeFileHandle held = Open(path, GenericRead, ShareReadWrite, OpenExisting, Normal | OpenReparsePoint);
                try
                {
                    Information baseline = Read(held);
                    ValidateFile(baseline);
                    byte[] hash = Hash(path, held, baseline);
                    return new FileGuard(path, held, baseline, hash);
                }
                catch { held.Dispose(); throw; }
            }

            internal void Check()
            {
                if (!SameFile(Read(held), baseline) || !Hash(path, held, baseline).SequenceEqual(hash))
                    throw new IOException("已保存文件身份、状态或内容指纹发生变化：" + path);
            }

            public void Dispose() { held.Dispose(); }

            private static void ValidateFile(Information info)
            {
                if ((info.Attributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0 ||
                    info.Links != 1 || (info.SizeHigh == 0 && info.SizeLow == 0))
                    throw new IOException("保存文件为空、存在硬链接或不是普通文件。");
            }

            private static byte[] Hash(string path, SafeFileHandle held, Information baseline)
            {
                ValidateFile(baseline);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                {
                    if (!SameFile(Read(held), baseline) || !SameFile(Read(stream.SafeFileHandle), baseline))
                        throw new IOException("计算指纹前文件身份或状态发生变化：" + path);
                    byte[] value = sha.ComputeHash(stream);
                    if (!SameFile(Read(held), baseline) || !SameFile(Read(stream.SafeFileHandle), baseline))
                        throw new IOException("计算指纹期间文件身份或状态发生变化：" + path);
                    using (SafeFileHandle current = Open(path, GenericRead, ShareReadWrite, OpenExisting, Normal | OpenReparsePoint))
                        if (!SameFile(Read(current), baseline))
                            throw new IOException("计算指纹后文件路径或状态发生变化：" + path);
                    return value;
                }
            }
        }
    }
}
