using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskTracker.Services
{
    /// <summary>
    /// Registers the global quick-add hotkey (Ctrl+Alt+T). Failure to register
    /// (hotkey taken by another app) is non-fatal and only logged.
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private const int HotkeyId = 0xA11C; // arbitrary app-unique id
        private const uint ModControl = 0x0002;
        private const uint ModAlt = 0x0001;
        private const uint VkT = 0x54;
        private const int WmHotkey = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _source;
        private IntPtr _handle;
        private bool _registered;

        public event EventHandler? HotkeyPressed;

        /// <summary>Call after the main window has a handle.</summary>
        public void Initialize(Window window)
        {
            _handle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
            _registered = RegisterHotKey(_handle, HotkeyId, ModControl | ModAlt, VkT);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_registered)
                UnregisterHotKey(_handle, HotkeyId);
            _source?.RemoveHook(WndProc);
        }
    }
}
