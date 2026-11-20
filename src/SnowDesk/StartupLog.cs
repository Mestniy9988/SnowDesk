namespace SnowDesk;

static class StartupLog
{
    public static void Write(Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SnowDesk");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 200_000)
                File.Delete(path);

            File.AppendAllText(path, $"--- {DateTime.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
