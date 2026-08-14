namespace Iov.OnvifSimulator.Services;

public static class FfmpegLocator
{
    public static string? Find()
    {
        var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (fromPath != null)
        {
            return fromPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsAvailable() => Find() != null;

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }

        return null;
    }
}
