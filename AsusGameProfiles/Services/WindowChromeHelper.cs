using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AsusGameProfiles.Services;

/// <summary>
/// Bascule la barre de titre native (Windows 10 1809+ / 11) en sombre ou clair pour rester cohérente
/// avec <see cref="ThemeService.IsDarkTheme"/>. Best-effort : ne doit jamais faire planter l'app
/// si l'API DWM est indisponible (ancienne version de Windows, etc.).
/// </summary>
public static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void SyncTitleBarWithTheme(Window window)
    {
        void Apply()
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int useDark = ThemeService.IsDarkTheme ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            }
            catch
            {
                // Amelioration purement visuelle : jamais bloquant si l'API DWM n'est pas disponible.
            }
        }

        if (PresentationSource.FromVisual(window) != null)
            Apply();
        else
            window.SourceInitialized += (_, _) => Apply();
    }

    // ---------- Correction de la zone maximisee (WindowStyle="None" + WindowChrome) ----------

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Corrige un bug connu de <c>WindowStyle="None"</c> + <c>WindowChrome</c> : sans ce hook,
    /// maximiser la fenêtre la fait déborder de l'écran (et recouvrir la barre des tâches) au lieu de
    /// se caler sur la zone de travail du moniteur -- vérifié en pratique sur cette app (rect
    /// -10,-10,3860,2180 sur un écran 3840x2160 avant ce correctif). Intercepte
    /// <c>WM_GETMINMAXINFO</c> et calcule nous-mêmes la position/taille maximisée d'après le moniteur
    /// réel (gère le multi-écran via <c>MonitorFromWindow</c>), au lieu de laisser Windows utiliser la
    /// taille de l'écran entier.
    /// </summary>
    public static void FixMaximizedBounds(Window window)
    {
        void Hook()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        if (PresentationSource.FromVisual(window) != null)
            Hook();
        else
            window.SourceInitialized += (_, _) => Hook();
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref monitorInfo);

                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var work = monitorInfo.rcWork;
                var bounds = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = work.Left - bounds.Left;
                mmi.ptMaxPosition.Y = work.Top - bounds.Top;
                mmi.ptMaxSize.X = work.Right - work.Left;
                mmi.ptMaxSize.Y = work.Bottom - work.Top;

                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        return IntPtr.Zero;
    }
}
