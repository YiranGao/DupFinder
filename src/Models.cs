using System;
using System.Collections.Generic;

namespace DupFinder.Core
{
    public enum EntryKind
    {
        File,
        Directory
    }

    /// <summary>一个具体的重名项（文件或文件夹）。</summary>
    public sealed class DuplicateEntry
    {
        public string Path { get; set; }
        public EntryKind Kind { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }

        /// <summary>界面上的勾选状态，由 UI 层维护。</summary>
        public bool Selected { get; set; }

        public bool IsDirectory
        {
            get { return Kind == EntryKind.Directory; }
        }
    }

    /// <summary>一组同名项（归一化名称相同，数量 >= 阈值）。</summary>
    public sealed class DuplicateGroup
    {
        public string Key { get; set; }
        public List<DuplicateEntry> Items { get; private set; }

        public DuplicateGroup(string key)
        {
            Key = key;
            Items = new List<DuplicateEntry>();
        }
    }

    public sealed class ScanOptions
    {
        public string Root { get; set; }
        public bool Recursive { get; set; }
        public bool IgnoreExtension { get; set; }
        public bool IncludeFiles { get; set; }
        public bool IncludeDirectories { get; set; }
        public bool SkipSystemDirs { get; set; }
        public bool ComputeFolderSize { get; set; }
        public int MinGroupSize { get; set; }

        public ScanOptions()
        {
            Recursive = true;
            IgnoreExtension = true;
            IncludeFiles = true;
            IncludeDirectories = true;
            SkipSystemDirs = true;
            ComputeFolderSize = false;
            MinGroupSize = 2;
        }
    }

    public sealed class ScanProgress
    {
        public int Files { get; set; }
        public int Directories { get; set; }
        public string Current { get; set; }
    }

    public sealed class ScanResult
    {
        public List<DuplicateGroup> Groups { get; private set; }
        public List<string> Warnings { get; private set; }
        public int ScannedFiles { get; set; }
        public int ScannedDirectories { get; set; }
        public bool Cancelled { get; set; }
        public TimeSpan Elapsed { get; set; }

        public ScanResult()
        {
            Groups = new List<DuplicateGroup>();
            Warnings = new List<string>();
        }

        public int TotalItems
        {
            get
            {
                int n = 0;
                foreach (var g in Groups) n += g.Items.Count;
                return n;
            }
        }
    }

    /// <summary>删除操作的逐项结果。</summary>
    public sealed class DeleteResult
    {
        public List<string> Succeeded { get; private set; }
        public List<string> Failed { get; private set; }
        public List<string> Blocked { get; private set; }

        public DeleteResult()
        {
            Succeeded = new List<string>();
            Failed = new List<string>();
            Blocked = new List<string>();
        }
    }
}
