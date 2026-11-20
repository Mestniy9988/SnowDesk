namespace SnowDesk;

static class Program
{
    static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) => StartupLog.Write(eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                if (eventArgs.ExceptionObject is Exception exception)
                    StartupLog.Write(exception);
            };

            _mutex = new Mutex(true, @"Local\SnowDesk.SingleInstance", out bool created);
            if (!created)
                return;

            DesktopMonitor.PreviewMode = string.Equals(
                Environment.GetEnvironmentVariable("SNOWDESK_PREVIEW"),
                "1",
                StringComparison.Ordinal);
            bool showSettings = DesktopMonitor.PreviewMode
                || args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));

            ApplicationConfiguration.Initialize();
            Application.Run(new AppController(showSettings));
            GC.KeepAlive(_mutex);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex);
        }
    }
}
