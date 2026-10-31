using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DupFinder.Core
{
    /// <summary>
    /// 无界面的命令行模式，用于自动化验证与批量处理。
    /// DupFinder.exe --cli --dir "D:\test" [--no-recursive] [--with-extension] [--min 2] [--json]
    /// </summary>
    public static class Cli
    {
        public static int Run(string[] args)
        {
            var options = new ScanOptions();
            bool json = false;
            bool delete = false;
            bool yes = false;

            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--cli":
                        break;
                    case "--dir":
                    case "-d":
                        options.Root = Next(args, ref i);
                        break;
                    case "--no-recursive":
                        options.Recursive = false;
                        break;
                    case "--with-extension":
                        options.IgnoreExtension = false;
                        break;
                    case "--files-only":
                        options.IncludeDirectories = false;
                        break;
                    case "--dirs-only":
                        options.IncludeFiles = false;
                        break;
                    case "--folder-size":
                        options.ComputeFolderSize = true;
                        break;
                    case "--min":
                        int min;
                        options.MinGroupSize = int.TryParse(Next(args, ref i), out min) ? min : 2;
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--delete":
                        delete = true;
                        break;
                    case "--yes":
                        yes = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;
                    default:
                        if (a.StartsWith("-"))
                        {
                            Console.Error.WriteLine("未知参数: " + a);
                            PrintUsage();
                            return 2;
                        }
                        if (options.Root == null) options.Root = a;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(options.Root))
            {
                Console.Error.WriteLine("缺少 --dir 参数。");
                PrintUsage();
                return 2;
            }

            var scanner = new Scanner();
            ScanResult result;
            try
            {
                result = scanner.Scan(options, CancellationToken.None, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("扫描失败: " + ex.Message);
                return 1;
            }

            var output = new StringBuilder();
            if (json)
            {
                output.AppendLine("{");
                output.AppendLine("  \"root\": " + JsonEscape(options.Root) + ",");
                output.AppendLine("  \"groups\": " + result.Groups.Count + ",");
                output.AppendLine("  \"items\": " + result.TotalItems + ",");
                output.AppendLine("  \"scannedFiles\": " + result.ScannedFiles + ",");
                output.AppendLine("  \"scannedDirectories\": " + result.ScannedDirectories + ",");
                output.AppendLine("  \"result\": [");
                for (int gi = 0; gi < result.Groups.Count; gi++)
                {
                    var g = result.Groups[gi];
                    output.AppendLine("    {\"key\": " + JsonEscape(g.Key) + ", \"items\": [");
                    for (int ii = 0; ii < g.Items.Count; ii++)
                    {
                        var it = g.Items[ii];
                        output.AppendLine("      {\"path\": " + JsonEscape(it.Path) +
                                          ", \"kind\": \"" + (it.IsDirectory ? "dir" : "file") + "\"" +
                                          ", \"size\": " + it.Size +
                                          ", \"modified\": \"" + it.Modified.ToString("yyyy-MM-dd HH:mm:ss") + "\"}" +
                                          (ii < g.Items.Count - 1 ? "," : ""));
                    }
                    output.AppendLine("    ]}" + (gi < result.Groups.Count - 1 ? "," : ""));
                }
                output.AppendLine("  ]");
                output.AppendLine("}");
            }
            else
            {
                output.AppendLine("扫描目录: " + options.Root);
                output.AppendLine("匹配模式: " + (options.IgnoreExtension ? "忽略扩展名" : "完整文件名") +
                                  " | 递归: " + (options.Recursive ? "是" : "否"));
                output.AppendLine("重名组: " + result.Groups.Count + " 组，共 " + result.TotalItems + " 项" +
                                  "（扫描文件 " + result.ScannedFiles + " 个，文件夹 " + result.ScannedDirectories + " 个）");
                output.AppendLine(new string('-', 60));
                foreach (var g in result.Groups)
                {
                    output.AppendLine("[" + g.Key + "]  " + g.Items.Count + " 项");
                    foreach (var it in g.Items)
                    {
                        output.AppendLine("    " + (it.IsDirectory ? "[DIR ] " : "[FILE] ") +
                                          (it.IsDirectory ? "        -" : FormatUtil.Size(it.Size).PadLeft(9)) +
                                          "  " + it.Modified.ToString("yyyy-MM-dd HH:mm") + "  " + it.Path);
                    }
                }
                if (result.Warnings.Count > 0)
                {
                    output.AppendLine(new string('-', 60));
                    output.AppendLine("警告 " + result.Warnings.Count + " 条:");
                    foreach (var w in result.Warnings) output.AppendLine("  ! " + w);
                }
            }

            Console.Out.Write(output.ToString());

            if (delete)
            {
                if (!yes)
                {
                    Console.Error.WriteLine("删除是一步危险操作，请追加 --yes 明确确认。" + Environment.NewLine +
                                            "说明：每组会保留「最新修改」的那一项，其余移入回收站。");
                    return 2;
                }
                return DeleteDuplicates(result, options.Root);
            }

            return 0;
        }

        /// <summary>每组保留最新修改的一项，其余移入回收站。</summary>
        private static int DeleteDuplicates(ScanResult result, string root)
        {
            var targets = new List<string>();
            var blocked = new List<string>();

            foreach (var g in result.Groups)
            {
                DuplicateEntry keeper = null;
                foreach (var it in g.Items)
                    if (keeper == null || it.Modified > keeper.Modified) keeper = it;

                foreach (var it in g.Items)
                {
                    if (ReferenceEquals(it, keeper)) continue;
                    string reason;
                    if (Safety.IsSafeToDelete(it.Path, root, out reason)) targets.Add(it.Path);
                    else blocked.Add(it.Path + " (" + reason + ")");
                }
            }

            Console.Out.WriteLine();
            Console.Out.WriteLine("待删除 " + targets.Count + " 项，每组保留最新修改的一项。");
            if (targets.Count == 0)
            {
                Console.Out.WriteLine("没有需要删除的项。");
                return 0;
            }

            var res = RecycleBin.DeleteAll(targets,
                delegate (int done, int total) { Console.Out.WriteLine("  进度 " + done + "/" + total); });

            Console.Out.WriteLine("已移入回收站: " + res.Succeeded.Count + " 项");
            if (res.Failed.Count > 0)
            {
                Console.Out.WriteLine("失败: " + res.Failed.Count + " 项");
                foreach (var f in res.Failed) Console.Out.WriteLine("  x " + f);
            }
            if (blocked.Count > 0)
            {
                Console.Out.WriteLine("被安全策略拦截: " + blocked.Count + " 项");
                foreach (var b in blocked) Console.Out.WriteLine("  ! " + b);
            }
            return res.Failed.Count > 0 ? 1 : 0;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 < args.Length) return args[++i];
            return null;
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static void PrintUsage()
        {
            Console.Out.Write(
                "DupFinder 命令行模式\r\n" +
                "用法: DupFinder.exe --cli --dir <目录> [选项]\r\n" +
                "  --dir <目录>        要扫描的目录（必填）\r\n" +
                "  --no-recursive      只扫描顶层，不进入子目录\r\n" +
                "  --with-extension    按完整文件名匹配（默认忽略扩展名）\r\n" +
                "  --files-only        只统计文件\r\n" +
                "  --dirs-only         只统计文件夹\r\n" +
                "  --folder-size       计算文件夹体积（较慢）\r\n" +
                "  --min <N>           至少 N 项才算重名（默认 2）\r\n" +
                "  --json              以 JSON 输出\r\n" +
                "  --delete --yes      每组保留最新修改的一项，其余移入回收站（必须带 --yes）\r\n");
        }
    }
}
