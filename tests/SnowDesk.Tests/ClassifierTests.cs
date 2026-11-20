namespace SnowDesk.Tests;

public class ClassifierTests
{
    [Fact]
    public void Empty_desktop_is_free()
    {
        var windows = new[]
        {
            Shell("Progman"),
            Shell("WorkerW"),
            Shell("Shell_TrayWnd"),
        };
        Assert.True(DesktopClassifier.IsDesktopFree(windows));
        Assert.False(DesktopClassifier.HasFullscreenBlocker(windows));
    }

    [Fact]
    public void Ordinary_window_is_busy()
    {
        WindowFacts notepad = WindowFacts.App("Notepad", "Безымянный — Блокнот");
        Assert.True(DesktopClassifier.IsBlocking(notepad));
        Assert.False(DesktopClassifier.IsDesktopFree(new[] { Shell("Progman"), notepad }));
    }

    [Theory]
    [InlineData("minimized")]
    [InlineData("cloaked")]
    [InlineData("tool")]
    [InlineData("own")]
    [InlineData("overlay")]
    [InlineData("hidden")]
    public void Ignored_windows_do_not_block(string kind)
    {
        WindowFacts facts = kind switch
        {
            "minimized" => WindowFacts.App("Notepad") with { Iconic = true },
            "cloaked" => WindowFacts.App("ApplicationFrameWindow") with { Cloaked = true },
            "tool" => WindowFacts.App("tooltips_class32") with { ToolWindow = true },
            "own" => WindowFacts.App("SnowDesk") with { OwnProcess = true },
            "overlay" => WindowFacts.App("SnowDesk Overlay") with { ClickThroughOverlay = true },
            _ => WindowFacts.App("Ghost") with { Visible = false },
        };
        Assert.False(DesktopClassifier.IsBlocking(facts));
    }

    [Fact]
    public void Fullscreen_is_busy_even_for_a_tool_window()
    {
        WindowFacts game = WindowFacts.App("UnityWndClass", "Game") with
        {
            Fullscreen = true,
            ToolWindow = true,
        };
        Assert.True(DesktopClassifier.IsBlocking(game));
        Assert.True(DesktopClassifier.HasFullscreenBlocker(new[] { game }));
    }

    [Fact]
    public void Taskbar_focus_is_free_when_nothing_else_is_open()
    {
        Assert.True(DesktopClassifier.IsIgnoredShellClass("Shell_TrayWnd"));
        Assert.True(DesktopClassifier.IsIgnoredShellClass("Shell_SecondaryTrayWnd"));
        Assert.False(DesktopClassifier.IsIgnoredShellClass("CabinetWClass"));
    }

    [Fact]
    public void First_blocker_reports_the_window_the_user_can_see()
    {
        WindowFacts? blocker = DesktopClassifier.FirstBlocker(new[]
        {
            Shell("Progman"),
            WindowFacts.App("Chrome_WidgetWin_1", "Документы"),
        });
        Assert.Equal("Документы", blocker?.Title);
    }

    static WindowFacts Shell(string className) => new(
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
        Title: "");
}
