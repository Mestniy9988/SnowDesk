namespace SnowDesk;

/// <summary>
/// Facts about one top-level window. The Win32 monitor fills these in;
/// the decision itself has no P/Invoke so it can be tested.
/// </summary>
public readonly record struct WindowFacts(
    bool Visible,
    bool Iconic,
    bool Cloaked,
    bool ToolWindow,
    bool AppWindow,
    bool ClickThroughOverlay,
    bool Fullscreen,
    bool OwnProcess,
    bool ZeroArea,
    string ClassName,
    string Title)
{
    public static WindowFacts App(string className, string title = "Window") => new(
        Visible: true,
        Iconic: false,
        Cloaked: false,
        ToolWindow: false,
        AppWindow: false,
        ClickThroughOverlay: false,
        Fullscreen: false,
        OwnProcess: false,
        ZeroArea: false,
        ClassName: className,
        Title: title);
}

public static class DesktopClassifier
{
    public static bool IsDesktopFree(IEnumerable<WindowFacts> windows)
    {
        foreach (WindowFacts window in windows)
        {
            if (IsBlocking(window))
                return false;
        }

        return true;
    }

    public static bool IsBlocking(in WindowFacts window)
    {
        if (!window.Visible || window.Iconic || window.Cloaked || window.OwnProcess || window.ZeroArea)
            return false;
        if (IsIgnoredShellClass(window.ClassName))
            return false;
        if (window.Fullscreen)
            return true;
        if (window.ClickThroughOverlay)
            return false;
        if (window.ToolWindow && !window.AppWindow)
            return false;
        return true;
    }

    public static WindowFacts? FirstBlocker(IEnumerable<WindowFacts> windows)
    {
        foreach (WindowFacts window in windows)
        {
            if (IsBlocking(window))
                return window;
        }

        return null;
    }

    public static bool HasFullscreenBlocker(IEnumerable<WindowFacts> windows)
    {
        foreach (WindowFacts window in windows)
        {
            if (IsBlocking(window) && window.Fullscreen)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Desktop and taskbar shells. Focusing these (or Win+D) means the desktop is empty
    /// only when no other fact is blocking — these windows themselves are not apps.
    /// </summary>
    public static bool IsIgnoredShellClass(string? className)
    {
        return className is
            "Progman" or
            "WorkerW" or
            "Shell_TrayWnd" or
            "Shell_SecondaryTrayWnd" or
            "NotifyIconOverflowWindow" or
            "DummyDWMListenerWindow" or
            "EdgeUiInputWndClass" or
            "NativeHWNDHost" or
            "IME" or
            "MSCTFIME UI" or
            "SysShadow" or
            "CiceroUIWndFrame";
    }
}
