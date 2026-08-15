using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Services
{
    /// <summary>
    /// Registers the global quick-add hotkey, Ctrl+Alt+T unless settings say otherwise.
    /// Failure to register (the combination is taken by another app) is non-fatal —
    /// <see cref="IsRegistered"/> is how the settings page says so.
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private const int HotkeyId = 0xA11C; // arbitrary app-unique id
        private const int WmHotkey = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _source;
        private IntPtr _handle;
        private bool _registered;

        public event EventHandler? HotkeyPressed;

        /// <summary>True when the current combination was actually claimed from Windows.</summary>
        public bool IsRegistered => _registered;

        /// <summary>Call after the main window has a handle.</summary>
        public void Initialize(Window window, string? hotkey = null)
        {
            _handle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
            Rebind(hotkey);
        }

        /// <summary>
        /// Claims a new combination, releasing the old one first. Returns false when
        /// the text does not parse or Windows refuses it — the caller keeps whatever it
        /// had rather than silently ending up with no hotkey at all.
        /// </summary>
        public bool Rebind(string? hotkey)
        {
            if (_handle == IntPtr.Zero)
                return false;

            var binding = HotkeyBinding.Parse(hotkey) ?? HotkeyBinding.Parse(HotkeyBinding.Default)!;

            if (_registered)
            {
                UnregisterHotKey(_handle, HotkeyId);
                _registered = false;
            }

            _registered = RegisterHotKey(_handle, HotkeyId, binding.Modifiers, binding.VirtualKey);
            if (!_registered)
                AppLog.Write("hotkey", $"Could not register '{binding.Text}'; it is probably in use by another application.");
            return _registered;
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
