using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DupFinder.Core
{
    /// <summary>通过 Windows Shell API 把文件/文件夹移入回收站（可恢复）。</summary>
    public static class RecycleBin
    {
        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        private const ushort Flags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>把一批路径移入回收站，返回是否全部成功。</summary>
        public static bool SendToRecycleBin(IList<string> paths, out string error)
        {
            error = null;
            if (paths == null || paths.Count == 0) return true;

            // SHFileOperation 受 MAX_PATH 限制，超长路径单独提示，不参与批量
            var batch = new List<string>();
            foreach (var p in paths)
            {
                if (p == null) continue;
                if (p.Length >= 240)
                {
                    error = "路径过长（超过 240 字符），已跳过: " + p;
                    continue;
                }
                batch.Add(p);
            }
            if (batch.Count == 0) return error == null;

            // 多路径以 \0 分隔，整体以 \0\0 结尾
            var sb = new StringBuilder();
            foreach (var p in batch)
            {
                sb.Append(p);
                sb.Append('\0');
            }
            sb.Append('\0');

            IntPtr fromPtr = IntPtr.Zero;
            try
            {
                fromPtr = Marshal.StringToHGlobalUni(sb.ToString());
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = fromPtr,
                    pTo = IntPtr.Zero,
                    fFlags = Flags,
                    fAnyOperationsAborted = 0,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = null
                };

                int rc = SHFileOperation(ref op);
                if (rc != 0)
                {
                    error = "Shell 删除失败，错误码: " + rc;
                    return false;
                }
                if (op.fAnyOperationsAborted != 0)
                {
                    error = "操作被系统中止（可能部分文件被占用）。";
                    return false;
                }
                return true;
            }
            finally
            {
                if (fromPtr != IntPtr.Zero) Marshal.FreeHGlobal(fromPtr);
            }
        }

        /// <summary>分批删除（每批 100 个），返回逐项结果。</summary>
        public static DeleteResult DeleteAll(IList<string> paths, Action<int, int> onProgress)
        {
            var result = new DeleteResult();
            int total = paths.Count;
            int done = 0;

            for (int i = 0; i < paths.Count; i += 100)
            {
                int take = Math.Min(100, paths.Count - i);
                var batch = new List<string>(take);
                for (int j = 0; j < take; j++) batch.Add(paths[i + j]);

                string error;
                bool ok = SendToRecycleBin(batch, out error);

                if (ok)
                {
                    result.Succeeded.AddRange(batch);
                }
                else
                {
                    // 整批失败时逐项重试，定位具体失败项
                    foreach (var p in batch)
                    {
                        string singleError;
                        var single = new List<string> { p };
                        if (SendToRecycleBin(single, out singleError)) result.Succeeded.Add(p);
                        else result.Failed.Add(p + "  —— " + (singleError ?? "未知错误"));
                    }
                }

                done += take;
                if (onProgress != null) onProgress(done, total);
            }

            return result;
        }
    }
}
