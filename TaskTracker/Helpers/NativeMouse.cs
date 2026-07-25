using System.Runtime.InteropServices;
using System.Windows;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// The cursor position during an OLE drag.
    ///
    /// <see cref="System.Windows.Input.Mouse.GetPosition"/> is unreliable while
    /// <c>DragDrop.DoDragDrop</c> is running — the drag loop owns the mouse, so it
    /// reports the position from before the drag began. Asking Win32 directly is the
    /// only way to track the pointer for the duration of a drag.
    /// </summary>
    internal static class NativeMouse
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT point);

        /// <summary>
        /// The cursor in physical screen pixels — the space
        /// <see cref="System.Windows.Media.Visual.PointFromScreen"/> expects, which is
        /// what keeps the math right across monitors with different DPI scaling.
        /// </summary>
        public static Point ScreenPosition()
        {
            return GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point();
        }
    }
}
