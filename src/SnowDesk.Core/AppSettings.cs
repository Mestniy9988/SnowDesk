using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnowDesk;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public int Intensity { get; set; } = 40;
    public int Volume { get; set; } = 25;
    public bool Muted { get; set; }
    public string? MusicPath { get; set; }
    public string PlowMessage { get; set; } = SpeechBubble.DefaultText;
    public bool ShowPlowMessage { get; set; } = true;
    public bool TaskbarSnow { get; set; } = true;
    public int PlowIntervalMinutes { get; set; } = 12;
    public bool StartWithWindows { get; set; }
    public bool PauseOnBatterySaver { get; set; }
    public bool PauseOnFullscreen { get; set; }

    public void Clamp()
    {
        Intensity = Math.Clamp(Intensity, 1, 100);
        Volume = Math.Clamp(Volume, 0, 100);
        PlowIntervalMinutes = Math.Clamp(PlowIntervalMinutes, 1, 120);
        if (string.IsNullOrWhiteSpace(MusicPath))
            MusicPath = null;
        PlowMessage = SpeechBubble.Normalize(PlowMessage);
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.Clamp();
        return settings;
    }
}

public static class Presets
{
    public static readonly (string Name, int Level)[] Intensity =
    {
        ("Очень слабый", 8),
        ("Лёгкий", 18),
        ("Средний", 40),
        ("Сильный", 70),
        ("Метель", 100),
    };

    public static readonly (string Name, int Volume)[] Volume =
    {
        ("Тихо", 15),
        ("Средне", 25),
        ("Громко", 70),
    };

    public static readonly int[] PlowMinutes = { 5, 10, 12, 15, 30 };
}

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultPath() : filePath;
    }

    public static string DefaultPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "SnowDesk", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return AppSettings.CreateDefault();

            string json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.CreateDefault();
            settings.Clamp();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Clamp();
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, overwrite: true);
    }
}
