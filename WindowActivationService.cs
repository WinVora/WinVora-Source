using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace WinVora
{
    internal static class WindowActivationService
    {
        private const int GwlHwndParent = -8;
        private const int SwRestore = 9;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly IntPtr HwndNotTopmost = new(-2);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        public static void ShowOwnedInFront(Window owner, Window child)
        {
            try
            {
                IntPtr ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
                IntPtr childHandle = WinRT.Interop.WindowNative.GetWindowHandle(child);

                // Ein echtes Besitzerfenster verhindert dauerhaft, dass das
                // Nebenfenster hinter WinVora einsortiert wird.
                SetWindowLongPtr(childHandle, GwlHwndParent, ownerHandle);
                ShowWindow(childHandle, SwRestore);

                // Kurzer Topmost-Wechsel überwindet Windows' Fokus-Sperre,
                // ohne das Fenster anschließend dauerhaft "Always on top" zu
                // lassen.
                const uint flags = SwpNoMove | SwpNoSize | SwpShowWindow;
                SetWindowPos(childHandle, HwndTopmost, 0, 0, 0, 0, flags);
                SetWindowPos(childHandle, HwndNotTopmost, 0, 0, 0, 0, flags);
                BringWindowToTop(childHandle);
                SetForegroundWindow(childHandle);
            }
            catch (Exception ex)
            {
                Logger.LogError("Nebenfenster konnte nicht in den Vordergrund gebracht werden", ex);
            }
        }

        public static void PlaceWindow(
            Window owner,
            Window child,
            int? savedX,
            int? savedY,
            int width,
            int height)
        {
            int x = savedX ?? owner.AppWindow.Position.X + Math.Max(0, (owner.AppWindow.Size.Width - width) / 2);
            int y = savedY ?? owner.AppWindow.Position.Y + Math.Max(0, (owner.AppWindow.Size.Height - height) / 2);
            child.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        }
    }
}
