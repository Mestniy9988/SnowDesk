namespace SnowDesk;

public readonly record struct MusicResolution(bool UseBuiltin, string? Path, string? Error)
{
    public static MusicResolution Builtin { get; } = new(true, null, null);

    public static MusicResolution Fallback(string error) => new(true, null, error);

    public static MusicResolution File(string path) => new(false, path, null);
}

/// <summary>
/// Picks the builtin loop or a user file without opening an audio device.
/// A missing or unreadable file falls back instead of throwing on the UI thread.
/// </summary>
public static class MusicResolver
{
    public static MusicResolution Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return MusicResolution.Builtin;

        if (!File.Exists(path))
            return MusicResolution.Fallback("Файл не найден.");

        string extension = Path.GetExtension(path);
        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) == false
            && extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) == false
            && extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) == false)
        {
            return MusicResolution.Fallback("Нужен файл mp3, wav или ogg.");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 16)
                return MusicResolution.Fallback("Файл слишком маленький.");

            var header = new byte[16];
            int read = stream.Read(header, 0, header.Length);
            if (!LooksLikeAudio(extension, header.AsSpan(0, read)))
                return MusicResolution.Fallback("Файл не похож на музыку.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MusicResolution.Fallback("Не удалось прочитать файл.");
        }

        return MusicResolution.File(path);
    }

    public static bool LooksLikeAudio(string extension, ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return false;

        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            return header.Length >= 12
                   && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                   && header[8] == (byte)'W' && header[9] == (byte)'A' && header[10] == (byte)'V' && header[11] == (byte)'E';

        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return header[0] == (byte)'O' && header[1] == (byte)'g' && header[2] == (byte)'g' && header[3] == (byte)'S';

        if (header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
            return true;

        return header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;
    }
}
