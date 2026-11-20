namespace SnowDesk;

static class Program
{
    static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, @"Local\SnowDesk.SingleInstance", out bool created);
        if (!created)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new AppController());
        GC.KeepAlive(_mutex);
    }
}
