using System.Runtime.InteropServices;
using System.Text;

namespace SnowDesk;

static class AppLog
{
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnowDesk",
        "log.txt");

    public static void Write(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 400_000)
                File.Delete(FilePath);

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(FilePath, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void WriteStartup(Scene scene)
    {
        var os = new OsVersion();
        os.dwOSVersionInfoSize = Marshal.SizeOf<OsVersion>();
        RtlGetVersion(ref os);
        uint dpi = 96;
        try
        {
            dpi = GetDpiForSystem();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        var monitors = new StringBuilder();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeMethods.Rect rect, IntPtr data) =>
        {
            monitors.Append($" [{rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top}]");
            return true;
        }, IntPtr.Zero);

        string bars = scene.Taskbars.Count == 0
            ? "none"
            : string.Join("; ", scene.Taskbars.Select(bar =>
                $"{bar.Edge} x={bar.X} y={bar.Y} {bar.Width}x{bar.Height} autoHide={bar.AutoHide}"));

        Write($"start OS={os.dwMajorVersion}.{os.dwMinorVersion}.{os.dwBuildNumber} dpi={dpi} " +
              $"virtual={scene.OriginX},{scene.OriginY} {scene.Width}x{scene.Height} " +
              $"monitors={monitors} taskbars={bars}");
    }

    [DllImport("ntdll.dll")]
    static extern int RtlGetVersion(ref OsVersion info);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OsVersion
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
    }
}
