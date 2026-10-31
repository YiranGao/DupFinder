using System;
using System.Collections.Generic;
using System.IO;

namespace DupFinder.Core
{
    /// <summary>删除前的路径安全校验：防止误删系统盘根目录、系统目录、扫描根本身等。</summary>
    public static class Safety
    {
        public static bool IsSafeToDelete(string path, string scanRoot, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "路径为空";
                return false;
            }

            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { reason = "路径格式非法"; return false; }

            if (full.Length >= 240)
            {
                reason = "路径过长，Shell 无法处理";
                return false;
            }

            // 盘符根目录不可删
            string driveRoot = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(driveRoot) &&
                string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), driveRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                reason = "不允许删除磁盘根目录";
                return false;
            }

            // 扫描根本身不可删
            if (!string.IsNullOrEmpty(scanRoot))
            {
                try
                {
                    string rootFull = Path.GetFullPath(scanRoot);
                    if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), rootFull.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "不允许删除扫描根目录本身";
                        return false;
                    }

                    // 必须在扫描根之内（防止列表被串改后删到别处）
                    string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                        ? rootFull
                        : rootFull + Path.DirectorySeparatorChar;
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "路径不在扫描目录之内";
                        return false;
                    }
                }
                catch (Exception) { /* 根目录异常时跳过该检查 */ }
            }

            foreach (var protectedPath in ProtectedPaths())
            {
                if (string.IsNullOrEmpty(protectedPath)) continue;
                if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), protectedPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    reason = "受保护的系统/用户目录";
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> ProtectedPaths()
        {
            var specials = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData
            };

            var list = new List<string>();
            foreach (var s in specials)
            {
                try { list.Add(Environment.GetFolderPath(s)); }
                catch (Exception) { }
            }
            return list;
        }
    }

    /// <summary>文件大小/路径的格式化辅助。</summary>
    public static class FormatUtil
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Size(long bytes)
        {
            if (bytes < 0) return "-";
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            if (unit == 0) return bytes + " B";
            return value.ToString(unit >= 2 ? "0.00" : "0.0") + " " + Units[unit];
        }

        public static string Relative(string fullPath, string root)
        {
            if (string.IsNullOrEmpty(root)) return fullPath;
            try
            {
                string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return fullPath.Substring(prefix.Length);
            }
            catch (Exception) { }
            return fullPath;
        }
    }
}
