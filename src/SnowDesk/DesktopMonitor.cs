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
        var windows = new List<WindowFacts>(64);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            windows.Add(Describe(hwnd));
            return true;
        }, IntPtr.Zero);

        WindowFacts? blocker = DesktopClassifier.FirstBlocker(windows);
        return new DesktopObservation(
            DesktopClassifier.IsDesktopFree(windows),
            DesktopClassifier.HasFullscreenBlocker(windows),
            blocker?.Title,
            blocker?.ClassName,
            BuildScene());
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
        int cloaked = 0;
        bool isCloaked = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int)) == 0
                         && cloaked != 0;
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
        NativeMethods.Rect rect = autoHide ? PinToMonitorEdge(data.rc, edge) : data.rc;
        bars.Add(ToBand(rect, edge, autoHide, originX, originY, "primary"));
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

            TaskbarEdge edge = GuessEdge(hwnd, rect);
            bool thin = edge is TaskbarEdge.Top or TaskbarEdge.Bottom
                ? rect.Bottom - rect.Top <= 4
                : rect.Right - rect.Left <= 4;
            bool hide = autoHide || thin;
            if (hide)
                rect = PinToMonitorEdge(rect, edge);
            bars.Add(ToBand(rect, edge, hide, originX, originY, "secondary" + index));
            index++;
            return true;
        }, IntPtr.Zero);
    }

    static TaskbarEdge GuessEdge(IntPtr hwnd, NativeMethods.Rect rect)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return TaskbarEdge.Bottom;

        NativeMethods.Rect screen = info.rcMonitor;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width >= height)
        {
            int toTop = Math.Abs(rect.Top - screen.Top);
            int toBottom = Math.Abs(screen.Bottom - rect.Bottom);
            return toTop < toBottom ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        int toLeft = Math.Abs(rect.Left - screen.Left);
        int toRight = Math.Abs(screen.Right - rect.Right);
        return toLeft < toRight ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    static NativeMethods.Rect PinToMonitorEdge(NativeMethods.Rect hint, TaskbarEdge edge)
    {
        IntPtr monitor = NativeMethods.MonitorFromRect(ref hint, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return hint;

        NativeMethods.Rect screen = info.rcMonitor;
        return edge switch
        {
            TaskbarEdge.Top => new NativeMethods.Rect { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Top },
            TaskbarEdge.Left => new NativeMethods.Rect { Left = screen.Left, Top = screen.Top, Right = screen.Left, Bottom = screen.Bottom },
            TaskbarEdge.Right => new NativeMethods.Rect { Left = screen.Right, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom },
            _ => new NativeMethods.Rect { Left = screen.Left, Top = screen.Bottom, Right = screen.Right, Bottom = screen.Bottom },
        };
    }

    static TaskbarBand ToBand(NativeMethods.Rect rect, TaskbarEdge edge, bool autoHide, int originX, int originY, string name)
    {
        int x = rect.Left - originX;
        int y = rect.Top - originY;
        int w = Math.Max(edge is TaskbarEdge.Left or TaskbarEdge.Right ? 0 : 1, rect.Right - rect.Left);
        int h = Math.Max(edge is TaskbarEdge.Top or TaskbarEdge.Bottom ? 0 : 1, rect.Bottom - rect.Top);
        if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom && w < 1)
            w = 1;
        if (edge is TaskbarEdge.Left or TaskbarEdge.Right && h < 1)
            h = 1;

        return new TaskbarBand
        {
            Id = $"{name}:{edge}:{x}:{y}:{w}:{h}:{autoHide}",
            Edge = edge,
            X = x,
            Y = y,
            Width = w,
            Height = h,
            AutoHide = autoHide,
        };
    }
}
