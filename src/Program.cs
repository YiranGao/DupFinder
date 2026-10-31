using System;
using System.IO;
using System.Windows.Forms;
using DupFinder.Core;

namespace DupFinder
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // 命令行模式：DupFinder.exe --cli --dir <path>
            foreach (var a in args)
            {
                if (string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase)) return Cli.Run(args);
                if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)) return Cli.Run(args);
            }

            // 必须在创建任何窗口前调用，否则窗口会被系统拉伸放大而超出屏幕
            NativeDpi.Enable();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(InitialDirectory(args)));
            return 0;
        }

        private static string InitialDirectory(string[] args)
        {
            foreach (var a in args)
            {
                if (a.StartsWith("-")) continue;
                try
                {
                    if (Directory.Exists(a)) return Path.GetFullPath(a);
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}
