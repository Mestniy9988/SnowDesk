using System.Runtime.InteropServices;
using System.Text;

namespace SnowDesk;

sealed record DesktopObservation(
    bool Free,
    bool FullscreenBusy,
    string? BlockerTitle,
    string? BlockerClass,
    Scene Scene);

/// <summary>
/// Event-driven foreground / minimize watcher plus a scene snapshot (virtual screen and taskbars).
/// The hook only signals; enumeration happens on the UI thread.
/// </summary>
sealed class DesktopMonitor : IDisposable
{
    readonly NativeMethods.WinEventDelegate _proc;
    IntPtr _foregroundHook;
    IntPtr _minimizeHook;
    bool _disposed;

    public DesktopMonitor()
    {
        _proc = OnWinEvent;
    }

    public static bool PreviewMode { get; set; }

    public static string PreviewBusyFile =>
        Environment.GetEnvironmentVariable("SNOWDESK_PREVIEW_BUSY")
        ?? Path.Combine(Path.GetTempPath(), "SnowDesk.preview-busy");

    public event Action? Activity;

    public void Start()
    {
        uint flags = NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _proc,
            0,
            0,
            flags);
        _minimizeHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemMinimizeStart,
            NativeMethods.EventSystemMinimizeEnd,
            IntPtr.Zero,
            _proc,
            0,
            0,
            flags);
    }

    public DesktopObservation Query()
    {
        if (PreviewMode)
        {
            bool busy = File.Exists(PreviewBusyFile);
            Scene scene = TryBuildScene();
            if (scene.Width < 320 || scene.Height < 240)
                scene = FallbackScene();
            return busy
                ? new DesktopObservation(false, false, "Окно проверки", "PreviewWindow", scene)
                : new DesktopObservation(true, false, null, null, scene);
        }

        try
        {
            var windows = new List<WindowFacts>(64);
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                try
                {
                    windows.Add(Describe(hwnd));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }

                return true;
            }, IntPtr.Zero);

            WindowFacts? blocker = DesktopClassifier.FirstBlocker(windows);
            return new DesktopObservation(
                DesktopClassifier.IsDesktopFree(windows),
                DesktopClassifier.HasFullscreenBlocker(windows),
                blocker?.Title,
                blocker?.ClassName,
                TryBuildScene());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return new DesktopObservation(false, false, "ошибка опроса", "SnowDesk", TryBuildScene());
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_minimizeHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_minimizeHook);
        _foregroundHook = IntPtr.Zero;
        _minimizeHook = IntPtr.Zero;
    }

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        Activity?.Invoke();
    }

    static WindowFacts Describe(IntPtr hwnd)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd);
        bool iconic = NativeMethods.IsIconic(hwnd);
        NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        var title = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        bool isCloaked = IsCloaked(hwnd);
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        bool tool = (exStyle & NativeMethods.WsExToolWindow) != 0;
        bool app = (exStyle & NativeMethods.WsExAppWindow) != 0;
        bool clickThrough = (exStyle & NativeMethods.WsExLayered) != 0
                            && (exStyle & NativeMethods.WsExTransparent) != 0
                            && (exStyle & NativeMethods.WsExNoActivate) != 0;
        bool fullscreen = visible && !iconic && width > 0 && height > 0 && CoversMonitor(hwnd, rect);
        return new WindowFacts(
            visible,
            iconic,
            isCloaked,
            tool,
            app,
            clickThrough,
            fullscreen,
            pid == (uint)Environment.ProcessId,
            width <= 0 || height <= 0,
            className.ToString(),
            title.ToString());
    }

    static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            int cloaked = 0;
            return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int)) == 0
                   && cloaked != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    static bool CoversMonitor(IntPtr hwnd, NativeMethods.Rect rect)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        NativeMethods.Rect screen = info.rcMonitor;
        return rect.Left <= screen.Left + 4
               && rect.Top <= screen.Top + 4
               && rect.Right >= screen.Right - 4
               && rect.Bottom >= screen.Bottom - 4;
    }

    static Scene TryBuildScene()
    {
        try
        {
            return BuildScene();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return FallbackScene();
        }
    }

    public static Scene FallbackScene() => new()
    {
        Width = 1280,
        Height = 720,
        Taskbars =
        [
            new TaskbarBand
            {
                Id = "fallback:Bottom:0:680:1280:40:False",
                Edge = TaskbarEdge.Bottom,
                X = 0,
                Y = 680,
                Width = 1280,
                Height = 40,
            },
        ],
    };

    static Scene BuildScene()
    {
        int originX = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        int originY = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        int width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen));
        int height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
        var bars = new List<TaskbarBand>(2);
        bool autoHide = ReadAutoHide();
        AddPrimaryTaskbar(bars, originX, originY, autoHide);
        AddSecondaryTaskbars(bars, originX, originY, autoHide);
        return new Scene
        {
            OriginX = originX,
            OriginY = originY,
            Width = width,
            Height = height,
            Taskbars = bars,
        };
    }

    static bool ReadAutoHide()
    {
        var data = new NativeMethods.AppBarData { cbSize = Marshal.SizeOf<NativeMethods.AppBarData>() };
        long state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref data).ToInt64();
        return (state & NativeMethods.AbsAutohide) != 0;
    }

    static void AddPrimaryTaskbar(List<TaskbarBand> bars, int originX, int originY, bool autoHide)
    {
        var data = new NativeMethods.AppBarData { cbSize = Marshal.SizeOf<NativeMethods.AppBarData>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) == IntPtr.Zero)
            return;

        var edge = (TaskbarEdge)Math.Clamp((int)data.uEdge, 0, 3);
        PixelRect rect = ToPixel(data.rc);
        if (autoHide)
            rect = PinToMonitorEdge(rect, edge);
        bars.Add(TaskbarLayout.ToBand(rect, edge, autoHide, originX, originY, "primary"));
    }

    static void AddSecondaryTaskbars(List<TaskbarBand> bars, int originX, int originY, bool autoHide)
    {
        int index = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var className = new StringBuilder(64);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            if (!className.ToString().Equals("Shell_SecondaryTrayWnd", StringComparison.Ordinal))
                return true;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect rect))
                return true;

            PixelRect pixels = ToPixel(rect);
            TaskbarEdge edge = GuessEdge(hwnd, pixels);
            bool thin = edge is TaskbarEdge.Top or TaskbarEdge.Bottom
                ? pixels.Height <= 4
                : pixels.Width <= 4;
            bool hide = autoHide || thin;
            if (hide)
                pixels = PinToMonitorEdge(pixels, edge);
            bars.Add(TaskbarLayout.ToBand(pixels, edge, hide, originX, originY, "secondary" + index));
            index++;
            return true;
        }, IntPtr.Zero);
    }

    static TaskbarEdge GuessEdge(IntPtr hwnd, PixelRect rect)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return TaskbarEdge.Bottom;

        return TaskbarLayout.GuessEdge(rect, ToPixel(info.rcMonitor));
    }

    static PixelRect PinToMonitorEdge(PixelRect hint, TaskbarEdge edge)
    {
        NativeMethods.Rect native = new()
        {
            Left = hint.Left,
            Top = hint.Top,
            Right = hint.Right,
            Bottom = hint.Bottom,
        };
        IntPtr monitor = NativeMethods.MonitorFromRect(ref native, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return hint;

        return TaskbarLayout.PinToEdge(ToPixel(info.rcMonitor), edge);
    }

    static PixelRect ToPixel(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
