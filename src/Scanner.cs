using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DupFinder.Core
{
    /// <summary>
    /// 扫描指定目录，按"归一化名称"把文件/文件夹分组，找出重名项。
    /// 归一化规则：默认去掉扩展名并忽略大小写，因此 aaa 文件夹、aaa.zip、aaa.exe 视为同名。
    /// </summary>
    public sealed class Scanner
    {
        private static readonly HashSet<string> SystemDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$recycle.bin",
            "system volume information",
            ".git",
            ".svn",
            ".hg"
        };

        private const int FolderSizeFileLimit = 50000;

        public ScanResult Scan(ScanOptions options, CancellationToken token, IProgress<ScanProgress> progress)
        {
            if (options == null) throw new ArgumentNullException("options");
            var result = new ScanResult();
            var sw = Stopwatch.StartNew();

            string root = NormalizeRoot(options.Root);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                throw new DirectoryNotFoundException("目录不存在: " + options.Root);

            // key -> group
            var map = new Dictionary<string, DuplicateGroup>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(root);

            int fileCount = 0;
            int dirCount = 0;
            int reportCounter = 0;

            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                string current = stack.Pop();

                FileSystemInfo[] entries;
                try
                {
                    entries = new DirectoryInfo(current).EnumerateFileSystemInfos().ToArrayEx();
                }
                catch (UnauthorizedAccessException ex)
                {
                    result.Warnings.Add("无访问权限: " + current + " (" + ex.Message + ")");
                    continue;
                }
                catch (IOException ex)
                {
                    result.Warnings.Add("读取失败: " + current + " (" + ex.Message + ")");
                    continue;
                }

                foreach (var info in entries)
                {
                    if (token.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    bool isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                    if (isDir)
                    {
                        dirCount++;
                        if (options.Recursive)
                        {
                            if (options.SkipSystemDirs && SystemDirNames.Contains(info.Name) && !IsSamePath(info.FullName, root))
                            {
                                // 跳过系统/版本控制目录
                            }
                            else
                            {
                                stack.Push(info.FullName);
                            }
                        }
                    }
                    else
                    {
                        fileCount++;
                    }

                    if (isDir && !options.IncludeDirectories) continue;
                    if (!isDir && !options.IncludeFiles) continue;

                    // 跳过重解析点（快捷方式/符号链接/挂载点），避免误删与死循环
                    if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;

                    string key = NormalizeName(info.Name, isDir, options.IgnoreExtension);
                    if (string.IsNullOrEmpty(key)) continue;

                    var entry = new DuplicateEntry
                    {
                        Path = info.FullName,
                        Kind = isDir ? EntryKind.Directory : EntryKind.File,
                        Modified = SafeLastWrite(info),
                        Size = 0
                    };

                    if (!isDir)
                    {
                        var fi = info as FileInfo;
                        entry.Size = fi != null ? fi.Length : 0;
                    }
                    else if (options.ComputeFolderSize)
                    {
                        entry.Size = ComputeDirectorySize(info.FullName);
                    }

                    DuplicateGroup group;
                    if (!map.TryGetValue(key, out group))
                    {
                        group = new DuplicateGroup(key);
                        map.Add(key, group);
                    }
                    group.Items.Add(entry);

                    if (progress != null && ++reportCounter % 200 == 0)
                    {
                        progress.Report(new ScanProgress { Files = fileCount, Directories = dirCount, Current = current });
                    }
                }
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            result.ScannedFiles = fileCount;
            result.ScannedDirectories = dirCount;

            foreach (var pair in map)
            {
                if (pair.Value.Items.Count < options.MinGroupSize) continue;
                pair.Value.Items.Sort(CompareEntry);
                result.Groups.Add(pair.Value);
            }

            result.Groups.Sort(delegate (DuplicateGroup a, DuplicateGroup b)
            {
                return string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase);
            });

            if (progress != null)
                progress.Report(new ScanProgress { Files = fileCount, Directories = dirCount, Current = null });

            return result;
        }

        private static int CompareEntry(DuplicateEntry a, DuplicateEntry b)
        {
            int c = string.Compare(a.Path, b.Path, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.Ordinal);
        }

        /// <summary>归一化名称：文件夹用原名，文件按选项决定是否去掉扩展名；统一小写。</summary>
        public static string NormalizeName(string name, bool isDir, bool ignoreExtension)
        {
            string key = name;
            if (!isDir && ignoreExtension)
            {
                key = System.IO.Path.GetFileNameWithoutExtension(name);
            }
            if (string.IsNullOrWhiteSpace(key)) key = name;
            return key.ToLowerInvariant();
        }

        private static DateTime SafeLastWrite(FileSystemInfo info)
        {
            try { return info.LastWriteTime; }
            catch { return DateTime.MinValue; }
        }

        private static bool IsSamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(a).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    System.IO.Path.GetFullPath(b).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        internal static string NormalizeRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Trim('"');
            try { return System.IO.Path.GetFullPath(path); }
            catch { return null; }
        }

        public static long ComputeDirectorySize(string path)
        {
            long total = 0;
            int count = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (++count > FolderSizeFileLimit) break;
                    try { total += new FileInfo(file).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }
    }

    internal static class EnumerableExtensions
    {
        public static T[] ToArrayEx<T>(this IEnumerable<T> source)
        {
            return new List<T>(source).ToArray();
        }
    }
}
