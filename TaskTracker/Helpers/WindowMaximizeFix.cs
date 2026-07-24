using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// A <c>WindowStyle="None"</c> window maximizes to the whole monitor rectangle
    /// rather than its work area, so it overflows the screen edges and hides the
    /// taskbar. Answering WM_GETMINMAXINFO with the work area of the monitor the
    /// window is currently on fixes both, and keeps working across monitors with
    /// different resolutions and DPI.
    ///
    /// WindowChrome restores the *interactions* (snap, resize, caption drag);
    /// this restores the maximized *bounds*. Both are needed.
    /// </summary>
    public static class WindowMaximizeFix
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x0002;

        public static void Attach(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                Hook(handle);
            else
                window.SourceInitialized += (s, _) => Hook(new WindowInteropHelper((Window)s!).Handle);
        }

        private static void Hook(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO)
                return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return IntPtr.Zero;

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return IntPtr.Zero;

            var minMax = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            // Everything here is in physical pixels, and ptMaxPosition is relative
            // to the monitor origin — not the desktop origin.
            var work = monitorInfo.rcWork;
            var screen = monitorInfo.rcMonitor;

            minMax.ptMaxPosition.x = work.left - screen.left;
            minMax.ptMaxPosition.y = work.top - screen.top;
            minMax.ptMaxSize.x = work.right - work.left;
            minMax.ptMaxSize.y = work.bottom - work.top;

            // Without this the window can still be sized past the work area while
            // maximized, which is what clips content under the taskbar.
            minMax.ptMaxTrackSize.x = minMax.ptMaxSize.x;
            minMax.ptMaxTrackSize.y = minMax.ptMaxSize.y;

            Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);
            handled = true;
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
