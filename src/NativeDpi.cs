using System;
using System.Runtime.InteropServices;

namespace DupFinder.Core
{
    /// <summary>
    /// 进程 DPI 感知处理。
    /// 若进程没有声明 DPI 感知，系统会用位图拉伸的方式把窗口放大（DPI 虚拟化），
    /// 结果是窗口物理尺寸超出屏幕、底部被切掉，且文字发虚。这里在窗口创建前显式启用。
    /// </summary>
    internal static class NativeDpi
    {
        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        /// <summary>必须在创建任何窗口之前调用。</summary>
        public static void Enable()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return;
            }
            catch (Exception) { }

            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>系统 DPI 缩放系数（如 150% 返回 1.5）。Screen.WorkingArea 为物理像素，需除以该系数得到逻辑像素。</summary>
        public static float SystemScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96) return dpi / 96f;
            }
            catch (Exception) { }
            return 1f;
        }

        /// <summary>获取指定窗口所在显示器的缩放系数（多显示器各自 DPI 不同时必须逐个取）。</summary>
        public static float ScaleForWindow(IntPtr hwnd)
        {
            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return SystemScale();
                uint dpiX, dpiY;
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0 && dpiX >= 96)
                    return dpiX / 96f;
            }
            catch (Exception) { }
            return SystemScale();
        }
    }
}
